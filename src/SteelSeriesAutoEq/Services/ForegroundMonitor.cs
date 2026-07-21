using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SteelSeriesAutoEq.Models;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Reports which window currently has focus, using two complementary mechanisms:
///
///  - A WinEvent hook (EVENT_SYSTEM_FOREGROUND) for instant reaction when the user switches
///    between applications.
///  - A one-second safety poll that catches anything the hook does not report. The important
///    case is switching tabs inside a single window: the foreground window never changes, only
///    its title does, so the hook stays silent. The poll notices the new title and reports it.
///
/// Both paths funnel through <see cref="RaiseIfChanged"/>, which de-duplicates so a change is
/// only reported once regardless of which mechanism saw it first.
/// </summary>
public sealed class ForegroundMonitor : IDisposable
{
    private const int MaxTitleLength = 512;
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private WinEventDelegate? _callback;
    private IntPtr _hook = IntPtr.Zero;
    private Timer? _pollTimer;

    // Cheap change detection so the poll only does the expensive process lookup when needed.
    private IntPtr _lastRawHwnd = IntPtr.Zero;
    private string _lastRawTitle = string.Empty;
    private string? _lastKey;

    public event Action<ForegroundAppInfo>? ForegroundChanged;

    public void Start()
    {
        if (_hook == IntPtr.Zero)
        {
            // Keep a rooted delegate so the native callback isn't garbage collected.
            _callback = OnWinEvent;
            _hook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WineventOutofcontext | WineventSkipownprocess);
        }

        _pollTimer ??= new Timer(_ => CheckForeground(), null, PollInterval, PollInterval);

        // Report whatever is focused right now so we don't wait for the first change.
        CheckForeground();
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        _callback = null;
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    public ForegroundAppInfo? GetForegroundApp()
    {
        var hwnd = GetForegroundWindow();
        return hwnd == IntPtr.Zero ? null : FromHwnd(hwnd, ReadTitle(hwnd));
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        // The hook tells us "something changed"; re-read the current foreground either way.
        CheckForeground();
    }

    private void CheckForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var title = ReadTitle(hwnd);

            // Skip the process lookup when the window and title are unchanged since last time.
            lock (_sync)
            {
                if (hwnd == _lastRawHwnd && string.Equals(title, _lastRawTitle, StringComparison.Ordinal))
                {
                    return;
                }

                _lastRawHwnd = hwnd;
                _lastRawTitle = title;
            }

            RaiseIfChanged(FromHwnd(hwnd, title));
        }
        catch
        {
            // Detection must never throw, especially from the native callback or timer thread.
        }
    }

    private void RaiseIfChanged(ForegroundAppInfo app)
    {
        var key = $"{app.ProcessId}|{app.ExecutableName}|{app.WindowTitle}";

        lock (_sync)
        {
            if (string.Equals(key, _lastKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastKey = key;
        }

        ForegroundChanged?.Invoke(app);
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        var builder = new StringBuilder(MaxTitleLength);
        _ = GetWindowText(hwnd, builder, MaxTitleLength);
        return builder.ToString().Trim();
    }

    private static ForegroundAppInfo FromHwnd(IntPtr hwnd, string title)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return new ForegroundAppInfo
            {
                WindowTitle = title,
                ProcessName = string.Empty,
                ExecutableName = string.Empty,
                ProcessId = 0
            };
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            var executableName = $"{processName}.exe";

            try
            {
                var moduleName = process.MainModule?.ModuleName;
                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    executableName = moduleName;
                    processName = Path.GetFileNameWithoutExtension(moduleName);
                }
            }
            catch
            {
                // MainModule is off-limits for elevated or system processes; the name is enough.
            }

            return new ForegroundAppInfo
            {
                WindowTitle = title,
                ProcessName = processName,
                ExecutableName = executableName,
                ProcessId = (int)processId
            };
        }
        catch
        {
            // Process may have exited between reading the handle and querying it.
            return new ForegroundAppInfo
            {
                WindowTitle = title,
                ProcessName = string.Empty,
                ExecutableName = string.Empty,
                ProcessId = (int)processId
            };
        }
    }

    public void Dispose() => Stop();

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
