using System.Text.Json;
using SteelSeriesAutoEq.Models;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Reads and writes the on-disk state (settings.json and profiles.json). The profile cache
/// lets the app show something immediately at startup before Sonar has been queried, and it
/// preserves any manual per-profile overrides across refreshes.
/// </summary>
public sealed class ProfileCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _profilesPath;
    private readonly string _settingsPath;
    private readonly AppLogger _logger;

    public ProfileCacheService(AppLogger logger, string? baseDirectory = null)
    {
        _logger = logger;
        var root = baseDirectory ?? AppContext.BaseDirectory;
        _profilesPath = Path.Combine(root, "profiles.json");
        _settingsPath = Path.Combine(root, "settings.json");
    }

    public string ProfilesPath => _profilesPath;

    public IReadOnlyList<SonarProfile> LoadProfiles()
    {
        if (!File.Exists(_profilesPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_profilesPath);
            var profiles = JsonSerializer.Deserialize<List<SonarProfile>>(json, JsonOptions);
            return profiles ?? [];
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load profiles.json: {ex.Message}");
            return [];
        }
    }

    public void SaveProfiles(IEnumerable<SonarProfile> profiles)
    {
        var existing = LoadProfiles().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        // Carry any manual processMatches from the cached copy onto the fresh API data.
        var merged = profiles.Select(p =>
        {
            if (existing.TryGetValue(p.Id, out var prior) && prior.ProcessMatches.Count > 0)
            {
                p.ProcessMatches = prior.ProcessMatches;
            }

            return p;
        }).ToList();

        // Keep hand-edited entries that no longer come back from the API so we don't lose overrides.
        foreach (var prior in existing.Values)
        {
            if (merged.All(p => !p.Id.Equals(prior.Id, StringComparison.OrdinalIgnoreCase)) &&
                prior.ProcessMatches.Count > 0)
            {
                merged.Add(prior);
            }
        }

        var json = JsonSerializer.Serialize(merged, JsonOptions);
        File.WriteAllText(_profilesPath, json);
        _logger.Info($"Cached {merged.Count} profile(s) to profiles.json");
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load settings.json: {ex.Message}");
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    /// <summary>
    /// Applies cached processMatches overrides onto a freshly fetched set of API profiles.
    /// </summary>
    public IReadOnlyList<SonarProfile> MergeWithCache(IReadOnlyList<SonarProfile> apiProfiles)
    {
        var cached = LoadProfiles().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var profile in apiProfiles)
        {
            if (cached.TryGetValue(profile.Id, out var prior))
            {
                profile.ProcessMatches = prior.ProcessMatches;
            }
        }

        return apiProfiles;
    }
}
