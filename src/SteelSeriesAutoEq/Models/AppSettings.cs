namespace SteelSeriesAutoEq.Models;

/// <summary>
/// User-adjustable settings, persisted to settings.json.
/// </summary>
public sealed class AppSettings
{
    public bool AutoSwitchEnabled { get; set; } = true;

    /// <summary>Minimum bigram-similarity score (0-1) before a fuzzy suggestion is offered.</summary>
    public double FuzzyMatchThreshold { get; set; } = 0.72;

    /// <summary>
    /// Game EQ profile id used when the focused process has no explicit assignment.
    /// Null/empty = leave current profile.
    /// </summary>
    public string? DefaultProfileId { get; set; }

    /// <summary>
    /// Explicit executable-name (lowercased, e.g. "cs2.exe") → Sonar config id assignments.
    /// Checked first on focus change; falls back to <see cref="DefaultProfileId"/>.
    /// </summary>
    public Dictionary<string, string> ProcessProfileMap { get; set; } = new();
}
