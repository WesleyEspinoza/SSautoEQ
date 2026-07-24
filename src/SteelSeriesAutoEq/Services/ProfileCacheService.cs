using System.Text.Json;
using SteelSeriesAutoEq.Models;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Reads and writes on-disk state (settings.json and profiles.json). The profile cache
/// shows something immediately at startup and preserves manual per-profile overrides.
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

    public IReadOnlyList<SonarProfile> LoadProfiles()
    {
        if (!File.Exists(_profilesPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_profilesPath);
            return JsonSerializer.Deserialize<List<SonarProfile>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load profiles.json: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Merges API profiles with any cached processMatches overrides, writes profiles.json,
    /// and returns the merged list.
    /// </summary>
    public IReadOnlyList<SonarProfile> MergeAndSave(IEnumerable<SonarProfile> apiProfiles)
    {
        var existing = LoadProfiles().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        var merged = apiProfiles.Select(p =>
        {
            if (existing.TryGetValue(p.Id, out var prior) && prior.ProcessMatches.Count > 0)
            {
                p.ProcessMatches = prior.ProcessMatches;
            }

            return p;
        }).ToList();

        // Keep hand-edited entries that no longer come back from the API.
        foreach (var prior in existing.Values)
        {
            if (prior.ProcessMatches.Count == 0)
            {
                continue;
            }

            if (merged.All(p => !p.Id.Equals(prior.Id, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(prior);
            }
        }

        File.WriteAllText(_profilesPath, JsonSerializer.Serialize(merged, JsonOptions));
        _logger.Info($"Cached {merged.Count} profile(s) to profiles.json");
        return merged;
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
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
