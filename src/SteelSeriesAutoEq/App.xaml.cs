using System.Runtime.InteropServices;
using System.Windows;
using SteelSeriesAutoEq.Services;
using SteelSeriesAutoEq.Tray;

namespace SteelSeriesAutoEq;

/// <summary>
/// Application entry point. Enforces a single instance, wires up the services, and starts the
/// tray UI. All long-lived objects are created here and disposed on exit.
/// </summary>
public partial class App : Application
{
    // Windows toast attribution (the small icon + name in the notification header) keys off this.
    private const string AppUserModelId = "SteelSeriesAutoEq.Tray";

    private SingleInstanceGuard? _singleInstance;
    private AppLogger? _logger;
    private AutoSwitchService? _service;
    private TrayController? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Must run before any windows or tray balloons so toasts pick up our exe icon.
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        base.OnStartup(e);

        // If another copy is already running, hand off to it and quit quietly.
        _singleInstance = SingleInstanceGuard.TryAcquireOrSignal();
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "logs"));

        _logger = new AppLogger(AppContext.BaseDirectory);
        _logger.Info("SteelSeries Auto EQ starting...");

        var cache = new ProfileCacheService(_logger, AppContext.BaseDirectory);
        var settings = cache.LoadSettings();
        var discovery = new ApiDiscoveryService(_logger);
        var api = new SonarApiClient(_logger);
        var foreground = new ForegroundMonitor();
        var launcher = new SteelSeriesLauncher(_logger);

        _service = new AutoSwitchService(_logger, discovery, api, foreground, cache, launcher, settings);
        _tray = new TrayController(_service, _logger);

        // A second launch signals us instead of starting a new process; bring our window forward.
        _singleInstance.ActivateRequested += () =>
        {
            Dispatcher.Invoke(() => _tray?.BringToFront());
        };
        _singleInstance.StartListening();

        try
        {
            await _service.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Startup failed.", ex);
        }

        _tray.ShowStartupUi();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Shutting down...");
        _tray?.Dispose();
        _service?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}
