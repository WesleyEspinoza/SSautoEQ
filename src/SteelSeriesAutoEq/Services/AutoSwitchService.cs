using SteelSeriesAutoEq.Models;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// The heart of the app. Ties together API discovery, foreground detection, profile matching,
/// and switching. It owns the current connection state and raises <see cref="StateChanged"/>
/// whenever something the UI cares about changes.
/// </summary>
public sealed class AutoSwitchService : IDisposable
{
    private readonly AppLogger _logger;
    private readonly ApiDiscoveryService _discovery;
    private readonly SonarApiClient _api;
    private readonly ForegroundMonitor _foreground;
    private readonly ProfileCacheService _cache;
    private readonly SteelSeriesLauncher _launcher;
    private ProfileMatcher _matcher;
    private AppSettings _settings;

    private CancellationTokenSource? _healthCts;
    private Task? _healthTask;
    private int _switchGate;
    private int _consecutivePingFailures;
    private ForegroundAppInfo? _pendingApp;
    private string? _lastSwitchedProfileId;
    private string? _lastLoggedProcessKey;
    private DateTime _lastRediscoverAttempt = DateTime.MinValue;
    private CancellationTokenSource? _debounceCts;
    private readonly SemaphoreSlim _rediscoverGate = new(1, 1);

    public event Action? StateChanged;

    public AutoSwitchService(
        AppLogger logger,
        ApiDiscoveryService discovery,
        SonarApiClient api,
        ForegroundMonitor foreground,
        ProfileCacheService cache,
        SteelSeriesLauncher launcher,
        AppSettings settings)
    {
        _logger = logger;
        _discovery = discovery;
        _api = api;
        _foreground = foreground;
        _cache = cache;
        _launcher = launcher;
        _settings = settings;
        _matcher = new ProfileMatcher(settings.FuzzyMatchThreshold);
    }

    public bool IsConnected => _api.IsConnected;
    public bool AutoSwitchEnabled => _settings.AutoSwitchEnabled;
    public string Status { get; private set; } = "Starting...";
    public string CurrentGame { get; private set; } = "—";
    public string CurrentProfile { get; private set; } = "—";
    public string ApiEndpoint { get; private set; } = "—";
    public IReadOnlyList<SonarProfile> Profiles { get; private set; } = [];

    /// <summary>
    /// The last real foreground app (never this app or the shell). Persists while the
    /// user interacts with the Auto EQ window so it can assign a config to it.
    /// </summary>
    public ForegroundAppInfo? LastForegroundApp { get; private set; }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _matcher = new ProfileMatcher(settings.FuzzyMatchThreshold);
        _cache.SaveSettings(settings);
        RaiseStateChanged();
    }

    public AppSettings GetSettings() => _settings;

    public void SetAutoSwitchEnabled(bool enabled)
    {
        _settings.AutoSwitchEnabled = enabled;
        _cache.SaveSettings(_settings);
        Status = enabled ? (IsConnected ? "Connected" : Status) : "Auto switching disabled";
        _logger.Info($"Auto switching {(enabled ? "enabled" : "disabled")}");
        RaiseStateChanged();
    }

    public async Task StartAsync()
    {
        await ConnectAndRefreshAsync();

        _foreground.ForegroundChanged += OnForegroundChanged;
        _foreground.Start();

        _healthCts = new CancellationTokenSource();
        _healthTask = Task.Run(() => RunHealthLoopAsync(_healthCts.Token));
    }

    public async Task ConnectAndRefreshAsync()
    {
        Status = "Discovering API...";
        RaiseStateChanged();

        var uri = await DiscoverOrLaunchAsync();
        if (uri is null)
        {
            _api.Clear();
            ApiEndpoint = "—";
            Status = "SteelSeries GG not found";
            RaiseStateChanged();
            return;
        }

        _api.SetBaseUri(uri);
        ApiEndpoint = uri.ToString().TrimEnd('/');
        await RefreshProfilesAsync();
        await RefreshSelectedProfileAsync();

        Status = "Connected";
        RaiseStateChanged();
    }

    /// <summary>
    /// Runs discovery and, if the API isn't found, auto-starts SteelSeries GG and waits for it
    /// to come up. If GG is already running but the API isn't answering yet (still booting),
    /// it simply waits and keeps probing.
    /// </summary>
    private async Task<Uri?> DiscoverOrLaunchAsync()
    {
        var uri = await _discovery.DiscoverAsync();
        if (uri is not null)
        {
            return uri;
        }

        if (_launcher.IsRunning())
        {
            _logger.Info("SteelSeries GG is running but the Sonar API isn't answering yet — waiting.");
        }
        else
        {
            Status = "Starting SteelSeries GG...";
            RaiseStateChanged();
            if (!_launcher.TryLaunch())
            {
                return null;
            }
        }

        return await WaitForApiAsync();
    }

    /// <summary>
    /// Polls discovery until the Sonar API responds or a timeout elapses. Used after launching
    /// (or while waiting on) SteelSeries GG, which can take several seconds to expose its API.
    /// </summary>
    private async Task<Uri?> WaitForApiAsync()
    {
        Status = "Waiting for SteelSeries GG...";
        RaiseStateChanged();

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));

            var uri = await _discovery.DiscoverAsync();
            if (uri is not null)
            {
                _logger.Info("SteelSeries GG is up and the Sonar API responded.");
                return uri;
            }
        }

        _logger.Warn("Timed out waiting for the SteelSeries GG Sonar API to come up.");
        return null;
    }

    public async Task RefreshProfilesAsync()
    {
        if (!_api.IsConnected)
        {
            await ConnectAndRefreshAsync();
            return;
        }

        try
        {
            var apiProfiles = await _api.GetGameProfilesAsync();
            Profiles = _cache.MergeWithCache(apiProfiles);
            _cache.SaveProfiles(Profiles);
            _logger.Info($"Loaded {Profiles.Count} game EQ profile(s).");
            Status = "Connected";
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to refresh profiles.", ex);
            Status = "API unavailable";
            await TryRediscoverAsync(force: true);
        }
    }

    private void OnForegroundChanged(ForegroundAppInfo app)
    {
        // Debounce rapid focus flicker (alt-tab animations, etc.).
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, token);
                await HandleForegroundAsync(app, token);
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer focus event
            }
        }, token);
    }

    private async Task HandleForegroundAsync(ForegroundAppInfo app, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _switchGate, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _pendingApp, app);
            if (!IsSelfOrShell(app) && !string.IsNullOrWhiteSpace(app.ExecutableName))
            {
                CurrentGame = app.DisplayName;
                RaiseStateChanged();
            }

            return;
        }

        try
        {
            var current = app;
            while (true)
            {
                await EvaluateAsync(current, cancellationToken);
                var pending = Interlocked.Exchange(ref _pendingApp, null);
                if (pending is null)
                {
                    break;
                }

                current = pending;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _switchGate, 0);

            // If something arrived in the tiny window after clearing pending but before unlocking, pick it up.
            var late = Interlocked.Exchange(ref _pendingApp, null);
            if (late is not null)
            {
                OnForegroundChanged(late);
            }
        }
    }

    private async Task EvaluateAsync(ForegroundAppInfo app, CancellationToken cancellationToken)
    {
        if (!_api.IsConnected)
        {
            await TryRediscoverAsync(force: false);
            return;
        }

        // Ignore our own window and the shell so the "current process" stays the game
        // even while the user is interacting with the Auto EQ window.
        if (string.IsNullOrWhiteSpace(app.ExecutableName) ||
            IsSelfOrShell(app))
        {
            return;
        }

        LastForegroundApp = app;
        CurrentGame = app.DisplayName;
        var processKey = $"{app.ExecutableName}|{app.WindowTitle}";

        if (!_settings.AutoSwitchEnabled)
        {
            RaiseStateChanged();
            return;
        }

        if (Profiles.Count == 0)
        {
            await RefreshProfilesAsync();
        }

        var target = ResolveProfileForApp(app, out var reason);
        if (target is null)
        {
            if (_lastLoggedProcessKey != processKey)
            {
                _logger.Block(
                    $"Detected process:{Environment.NewLine}{app.ExecutableName}",
                    $"Window:{Environment.NewLine}{app.WindowTitle}",
                    "No assigned config and no default set");
                _lastLoggedProcessKey = processKey;
            }

            RaiseStateChanged();
            return;
        }

        if (_lastLoggedProcessKey != processKey)
        {
            _logger.Block(
                $"Detected process:{Environment.NewLine}{app.ExecutableName}",
                $"Selected profile:{Environment.NewLine}{target.Name}",
                $"Reason:{Environment.NewLine}{reason}");
            _lastLoggedProcessKey = processKey;
        }

        await SwitchToProfileAsync(target, cancellationToken);
    }

    /// <summary>
    /// Resolves the profile for a process: explicit per-process assignment first, then default.
    /// </summary>
    private SonarProfile? ResolveProfileForApp(ForegroundAppInfo app, out string reason)
    {
        var assigned = GetAssignedProfile(app.ExecutableName);
        if (assigned is not null)
        {
            reason = $"assigned to {app.ExecutableName}";
            return assigned;
        }

        var def = ResolveDefaultProfile();
        if (def is not null)
        {
            reason = "default profile (no assignment)";
            return def;
        }

        reason = "no assignment, no default";
        return null;
    }

    public SonarProfile? ResolveDefaultProfile()
    {
        if (string.IsNullOrWhiteSpace(_settings.DefaultProfileId))
        {
            return null;
        }

        return Profiles.FirstOrDefault(p =>
            p.Id.Equals(_settings.DefaultProfileId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Config id explicitly assigned to an executable, if any.</summary>
    public string? GetAssignedProfileId(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        foreach (var kv in _settings.ProcessProfileMap)
        {
            if (kv.Key.Equals(executableName, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }

        return null;
    }

    public SonarProfile? GetAssignedProfile(string executableName)
    {
        var id = GetAssignedProfileId(executableName);
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Non-switching suggestion used only to pre-fill the assignment UI.</summary>
    public SonarProfile? SuggestProfile(ForegroundAppInfo app) =>
        _matcher.FindBestMatch(app, Profiles)?.Profile;

    public IReadOnlyList<(string Executable, string ProfileName, string ConfigId)> GetAssignments()
    {
        var result = new List<(string, string, string)>();
        foreach (var kv in _settings.ProcessProfileMap)
        {
            var name = Profiles.FirstOrDefault(p =>
                p.Id.Equals(kv.Value, StringComparison.OrdinalIgnoreCase))?.Name
                ?? "(missing profile)";
            result.Add((kv.Key, name, kv.Value));
        }

        return result
            .OrderBy(r => r.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Assigns (or with null configId, clears) the config for an executable. If that process
    /// is currently focused, applies the change immediately.
    /// </summary>
    public async Task AssignProfileToProcessAsync(string executableName, string? configId)
    {
        var exe = executableName?.Trim();
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        // Remove any existing (case-insensitive) key first.
        var existingKey = _settings.ProcessProfileMap.Keys
            .FirstOrDefault(k => k.Equals(exe, StringComparison.OrdinalIgnoreCase));
        if (existingKey is not null)
        {
            _settings.ProcessProfileMap.Remove(existingKey);
        }

        if (!string.IsNullOrWhiteSpace(configId))
        {
            _settings.ProcessProfileMap[exe.ToLowerInvariant()] = configId;
            var name = Profiles.FirstOrDefault(p =>
                p.Id.Equals(configId, StringComparison.OrdinalIgnoreCase))?.Name ?? configId;
            _logger.Info($"Assigned '{exe}' → {name}");
        }
        else
        {
            _logger.Info($"Cleared assignment for '{exe}' (will use default)");
        }

        _cache.SaveSettings(_settings);

        // Apply immediately if the assigned process is the one currently in focus.
        var current = LastForegroundApp;
        if (current is not null &&
            current.ExecutableName.Equals(exe, StringComparison.OrdinalIgnoreCase))
        {
            _lastLoggedProcessKey = null;
            var target = ResolveProfileForApp(current, out _);
            if (target is not null)
            {
                await SwitchToProfileAsync(target, CancellationToken.None);
            }
        }

        RaiseStateChanged();
    }

    private async Task SwitchToProfileAsync(SonarProfile profile, CancellationToken cancellationToken)
    {
        if (string.Equals(_lastSwitchedProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentProfile = profile.Name;
            RaiseStateChanged();
            return;
        }

        try
        {
            await _api.SelectProfileAsync(profile.Id, cancellationToken);
            var selected = await _api.GetSelectedGameProfileAsync(cancellationToken);
            CurrentProfile = selected?.Name ?? profile.Name;
            _lastSwitchedProfileId = profile.Id;
            Status = "Connected";
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to switch profile '{profile.Name}'.", ex);
            // Don't thrash rediscovery on a single failed switch — verify with a ping first.
            if (!await _api.PingAsync(CancellationToken.None))
            {
                Status = "API unavailable — rediscovering...";
                RaiseStateChanged();
                await TryRediscoverAsync(force: true);
            }
            else
            {
                Status = "Connected";
                RaiseStateChanged();
            }
        }
    }

    private async Task RunHealthLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(45), cancellationToken);

                if (!_api.IsConnected)
                {
                    _logger.Warn("Health check: not connected — rediscovering.");
                    Status = "API unavailable — rediscovering...";
                    RaiseStateChanged();
                    await TryRediscoverAsync(force: true);
                    continue;
                }

                if (await _api.PingAsync(cancellationToken))
                {
                    _consecutivePingFailures = 0;
                    if (Status.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                        Status.Contains("rediscover", StringComparison.OrdinalIgnoreCase))
                    {
                        Status = "Connected";
                        RaiseStateChanged();
                    }

                    continue;
                }

                _consecutivePingFailures++;
                _logger.Warn($"Health check ping failed ({_consecutivePingFailures}/3).");

                // Require a few failures so a single slow Sonar response doesn't flap status.
                if (_consecutivePingFailures < 3)
                {
                    continue;
                }

                Status = "API unavailable — rediscovering...";
                RaiseStateChanged();
                await TryRediscoverAsync(force: true);
                _consecutivePingFailures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Health check error.", ex);
            }
        }
    }

    private async Task RefreshSelectedProfileAsync()
    {
        try
        {
            var selected = await _api.GetSelectedGameProfileAsync();
            if (selected is not null)
            {
                CurrentProfile = selected.Name;
                _lastSwitchedProfileId = selected.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not read selected profile: {ex.Message}");
        }
    }

    private async Task TryRediscoverAsync(bool force)
    {
        if (!force && DateTime.UtcNow - _lastRediscoverAttempt < TimeSpan.FromSeconds(10))
        {
            return;
        }

        if (!await _rediscoverGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            _lastRediscoverAttempt = DateTime.UtcNow;

            // Keep the old endpoint until a new one is confirmed — avoids blanking IsConnected mid-flight.
            var previous = _api.BaseUri;
            if (previous is not null && await _discovery.IsValidSonarApiAsync(previous))
            {
                _logger.Info($"Existing Sonar endpoint still valid: {previous}");
                _api.SetBaseUri(previous);
                Status = "Connected";
                _consecutivePingFailures = 0;
                RaiseStateChanged();
                return;
            }

            Status = "Discovering API...";
            RaiseStateChanged();

            var uri = await DiscoverOrLaunchAsync();
            if (uri is null)
            {
                _api.Clear();
                ApiEndpoint = "—";
                Status = "SteelSeries GG not found";
                RaiseStateChanged();
                return;
            }

            _api.SetBaseUri(uri);
            ApiEndpoint = uri.ToString().TrimEnd('/');
            await RefreshProfilesAsync();
            await RefreshSelectedProfileAsync();
            _consecutivePingFailures = 0;
            Status = "Connected";
            RaiseStateChanged();
        }
        finally
        {
            _rediscoverGate.Release();
        }
    }

    private static bool IsSelfOrShell(ForegroundAppInfo app)
    {
        var name = Path.GetFileNameWithoutExtension(app.ExecutableName);
        return name.Equals("SteelSeriesAutoEq", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        _foreground.ForegroundChanged -= OnForegroundChanged;
        _foreground.Stop();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        _healthCts?.Cancel();
        try
        {
            _healthTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignored
        }

        _healthCts?.Dispose();
        _rediscoverGate.Dispose();
        _api.Dispose();
    }
}
