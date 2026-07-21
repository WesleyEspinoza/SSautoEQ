using System.Collections.Concurrent;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Simple append-only logger. Writes timestamped lines to logs/app.log and keeps the most
/// recent lines in memory so the UI can show a tail without re-reading the file.
/// </summary>
public sealed class AppLogger
{
    private const int MaxRecentLines = 200;

    private readonly string _logPath;
    private readonly object _sync = new();
    private readonly ConcurrentQueue<string> _recent = new();

    public event Action<string>? LineWritten;

    public AppLogger(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var logsDir = Path.Combine(root, "logs");
        Directory.CreateDirectory(logsDir);
        _logPath = Path.Combine(logsDir, "app.log");
    }

    public string LogPath => _logPath;

    public IReadOnlyList<string> GetRecentLines(int count = 50)
    {
        return _recent.Reverse().Take(count).Reverse().ToList();
    }

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

        foreach (var line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            _recent.Enqueue(line);
            while (_recent.Count > MaxRecentLines && _recent.TryDequeue(out _))
            {
            }

            LineWritten?.Invoke(line);
        }
    }
}
