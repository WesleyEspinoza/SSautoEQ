namespace SteelSeriesAutoEq.Models;

/// <summary>
/// A snapshot of the window that currently has focus: what we could read about the owning
/// process at the moment the foreground changed.
/// </summary>
public sealed class ForegroundAppInfo
{
    public required string WindowTitle { get; init; }
    public required string ProcessName { get; init; }
    public required string ExecutableName { get; init; }
    public int ProcessId { get; init; }

    /// <summary>Window title when we have one, otherwise the executable name.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(WindowTitle) ? ExecutableName : WindowTitle;
}
