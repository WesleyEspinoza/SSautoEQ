namespace SteelSeriesAutoEq.Models;

/// <summary>
/// How confident a match is. Higher values win when more than one profile could apply; the
/// order reflects how specific each kind of match is.
/// </summary>
public enum MatchPriority
{
    None = 0,
    Fuzzy = 1,
    PartialProfileName = 2,
    ExactWindowTitle = 3,
    GameAlias = 4,
    ExactProcessName = 5
}

/// <summary>
/// The profile the matcher picked for an app, plus why it was chosen. The reason is used for
/// logging and to pre-fill the assignment UI with a suggestion.
/// </summary>
public sealed class MatchResult
{
    public required SonarProfile Profile { get; init; }
    public required MatchPriority Priority { get; init; }
    public required string Reason { get; init; }

    /// <summary>Tie-breaker within a priority level; 1.0 for exact matches.</summary>
    public double Score { get; init; } = 1.0;
}
