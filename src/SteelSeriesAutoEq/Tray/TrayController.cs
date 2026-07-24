using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using SteelSeriesAutoEq.Services;

namespace SteelSeriesAutoEq.Tray;

/// <summary>
/// Owns the tray icon and its context menu, and mirrors the service's state into the menu
/// labels and tooltip. Also opens and reuses the single status window.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly TaskbarIcon _tray;
    private readonly AutoSwitchService _service;
    private readonly AppLogger _logger;
    private readonly MenuItem _gameItem;
    private readonly MenuItem _profileItem;
    private readonly MenuItem _statusItem;
    private readonly MenuItem _endpointItem;
    private readonly MenuItem _autoSwitchItem;
    private SettingsWindow? _settingsWindow;

    public TrayController(AutoSwitchService service, AppLogger logger)
    {
        _service = service;
        _logger = logger;

        _gameItem = DisabledItem("Current Game: —");
        _profileItem = DisabledItem("Current Profile: —");
        _statusItem = DisabledItem("Status: Starting...");
        _endpointItem = DisabledItem("API: —");
        _autoSwitchItem = new MenuItem
        {
            Header = "Enable Auto Switching",
            IsCheckable = true,
            IsChecked = true
        };
        _autoSwitchItem.Click += (_, _) =>
            _service.SetAutoSwitchEnabled(_autoSwitchItem.IsChecked);

        var refreshItem = new MenuItem { Header = "Refresh Profiles" };
        refreshItem.Click += async (_, _) =>
        {
            refreshItem.IsEnabled = false;
            try
            {
                await _service.RefreshProfilesAsync();
                UpdateMenu();
            }
            finally
            {
                refreshItem.IsEnabled = true;
            }
        };

        var openItem = new MenuItem { Header = "Open" };
        openItem.Click += (_, _) => OpenSettings();

        var openLogItem = new MenuItem { Header = "Open Log" };
        openLogItem.Click += (_, _) => OpenLog();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(DisabledItem("SteelSeries Auto EQ"));
        menu.Items.Add(new Separator());
        menu.Items.Add(_gameItem);
        menu.Items.Add(_profileItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(_endpointItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(openItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(_autoSwitchItem);
        menu.Items.Add(openLogItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon
        {
            ToolTipText = "SteelSeries Auto EQ — double-click to open",
            IconSource = AppIcons.CreateImageSource(),
            Icon = AppIcons.CreateIcon(),
            ContextMenu = menu,
            Visibility = Visibility.Visible,
            MenuActivation = PopupActivationMode.RightClick,
            DoubleClickCommand = new RelayCommand(OpenSettings)
        };

        _service.StateChanged += () =>
        {
            Application.Current.Dispatcher.Invoke(UpdateMenu);
        };

        UpdateMenu();
    }

    public void ShowStartupUi()
    {
        OpenSettings();
        ShowAppBalloon(
            "Running in the system tray. Double-click the icon anytime to reopen this window.");
    }

    public void BringToFront()
    {
        OpenSettings();
        ShowAppBalloon("Already running — brought existing window to front.");
    }

    public void Dispose()
    {
        _tray.Dispose();
        _settingsWindow?.Close();
    }

    private void ShowAppBalloon(string message)
    {
        if (_tray.Icon is not null)
        {
            _tray.ShowBalloonTip("SteelSeries Auto EQ", message, _tray.Icon, largeIcon: true);
        }
        else
        {
            _tray.ShowBalloonTip("SteelSeries Auto EQ", message, BalloonIcon.None);
        }
    }

    private void UpdateMenu()
    {
        _gameItem.Header = $"Current Game:{Environment.NewLine}{_service.CurrentGame}";
        _profileItem.Header = $"Current Profile:{Environment.NewLine}{_service.CurrentProfile}";
        _statusItem.Header = $"Status:{Environment.NewLine}{_service.Status}";
        _endpointItem.Header = $"API:{Environment.NewLine}{_service.ApiEndpoint}";
        _autoSwitchItem.IsChecked = _service.AutoSwitchEnabled;
        _tray.ToolTipText =
            $"SteelSeries Auto EQ{Environment.NewLine}" +
            $"{_service.Status}{Environment.NewLine}" +
            $"{_service.CurrentGame} → {_service.CurrentProfile}";
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            _settingsWindow.WindowState = WindowState.Normal;
            return;
        }

        _settingsWindow = new SettingsWindow(_service);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OpenLog()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _logger.LogPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open log:{Environment.NewLine}{ex.Message}",
                "SteelSeries Auto EQ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static MenuItem DisabledItem(string header) =>
        new() { Header = header, IsEnabled = false };
}

internal sealed class RelayCommand(Action execute) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
