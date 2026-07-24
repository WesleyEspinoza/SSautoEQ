namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Append-only logger. Writes timestamped lines to logs/app.log.
/// </summary>
public sealed class AppLogger
{
    private readonly string _logPath;
    private readonly object _sync = new();

    public AppLogger(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var logsDir = Path.Combine(root, "logs");
        Directory.CreateDirectory(logsDir);
        _logPath = Path.Combine(logsDir, "app.log");
    }

    public string LogPath => _logPath;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? ex = null)
    {
        if (ex is null)
        {
            Write("ERROR", message);
            return;
        }

        Write("ERROR", $"{message}{Environment.NewLine}{ex}");
    }

    /// <summary>
    /// Writes several related lines as one timestamped block, e.g. the detected process
    /// and the profile it mapped to. Easier to scan in the log than one long line.
    /// </summary>
    public void Block(params string[] lines)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var text = string.Join(Environment.NewLine, lines.Select(l => $"[{stamp}]{Environment.NewLine}{l}"));
        AppendRaw(text + Environment.NewLine);
    }

    private void Write(string level, string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        AppendRaw($"[{stamp}] [{level}] {message}{Environment.NewLine}");
    }

    private void AppendRaw(string text)
    {
        lock (_sync)
        {
            File.AppendAllText(_logPath, text);
        }
    }
}
