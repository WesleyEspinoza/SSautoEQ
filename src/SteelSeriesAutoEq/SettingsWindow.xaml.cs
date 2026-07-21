using System.Windows;
using SteelSeriesAutoEq.Models;
using SteelSeriesAutoEq.Services;

namespace SteelSeriesAutoEq;

/// <summary>
/// The status and configuration window. Reads everything it displays from the service and
/// re-reads on <see cref="AutoSwitchService.StateChanged"/>, so it always reflects live state.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AutoSwitchService _service;
    private readonly AppLogger _logger;
    private string? _focusExecutable;

    public SettingsWindow(AutoSwitchService service, AppLogger logger)
    {
        InitializeComponent();
        _service = service;
        _logger = logger;
        LoadFromService();
        _service.StateChanged += OnStateChanged;
        Closed += (_, _) => _service.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged()
    {
        Dispatcher.Invoke(LoadFromService);
    }

    private void LoadFromService()
    {
        var settings = _service.GetSettings();
        AutoSwitchCheck.IsChecked = settings.AutoSwitchEnabled;
        ActiveProfileText.Text = string.IsNullOrWhiteSpace(_service.CurrentProfile)
            ? "—"
            : _service.CurrentProfile;

        var defaultName = _service.ResolveDefaultProfile()?.Name ?? "(none)";
        StatusText.Text =
            $"{_service.Status}  |  API: {_service.ApiEndpoint}{Environment.NewLine}" +
            $"Default: {defaultName}  |  Profiles: {_service.Profiles.Count}";

        // Default profile combo
        var defaultItems = new List<ProfileChoice>
        {
            new("(none — leave current)", null)
        };
        defaultItems.AddRange(_service.Profiles
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ProfileChoice(p.Name, p.Id)));

        DefaultProfileCombo.ItemsSource = defaultItems;
        DefaultProfileCombo.SelectedItem = defaultItems.FirstOrDefault(c =>
            string.Equals(c.Id, settings.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            ?? defaultItems[0];

        LoadFocusPanel();
        LoadAssignments();
    }

    private void LoadFocusPanel()
    {
        var app = _service.LastForegroundApp;
        _focusExecutable = app?.ExecutableName;

        var assignItems = new List<ProfileChoice>
        {
            new("(use default)", null)
        };
        assignItems.AddRange(_service.Profiles
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ProfileChoice(p.Name, p.Id)));
        AssignProfileCombo.ItemsSource = assignItems;

        if (app is null || string.IsNullOrWhiteSpace(app.ExecutableName))
        {
            FocusProcessText.Text = "(no game/app detected yet)";
            FocusWindowText.Text = string.Empty;
            AssignProfileCombo.SelectedItem = assignItems[0];
            AssignProfileCombo.IsEnabled = false;
            AssignButton.IsEnabled = false;
            return;
        }

        AssignProfileCombo.IsEnabled = true;
        AssignButton.IsEnabled = true;
        FocusProcessText.Text = app.ExecutableName;
        FocusWindowText.Text = string.IsNullOrWhiteSpace(app.WindowTitle)
            ? string.Empty
            : $"“{app.WindowTitle}”";

        // Pre-select: existing assignment → suggestion → (use default)
        var assignedId = _service.GetAssignedProfileId(app.ExecutableName);
        if (!string.IsNullOrWhiteSpace(assignedId))
        {
            AssignProfileCombo.SelectedItem = assignItems.FirstOrDefault(c =>
                string.Equals(c.Id, assignedId, StringComparison.OrdinalIgnoreCase)) ?? assignItems[0];
        }
        else
        {
            var suggestion = _service.SuggestProfile(app);
            AssignProfileCombo.SelectedItem = suggestion is null
                ? assignItems[0]
                : assignItems.FirstOrDefault(c =>
                    string.Equals(c.Id, suggestion.Id, StringComparison.OrdinalIgnoreCase)) ?? assignItems[0];
        }
    }

    private void LoadAssignments()
    {
        AssignmentsList.ItemsSource = _service.GetAssignments()
            .Select(a => new AssignmentRow(a.Executable, a.ProfileName, a.ConfigId))
            .ToList();
    }

    private async void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_focusExecutable))
        {
            return;
        }

        var choice = AssignProfileCombo.SelectedItem as ProfileChoice;
        await _service.AssignProfileToProcessAsync(_focusExecutable, choice?.Id);
        LoadFromService();
    }

    private async void RemoveAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (AssignmentsList.SelectedItem is not AssignmentRow row)
        {
            MessageBox.Show("Select an assignment to remove.",
                "SteelSeries Auto EQ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _service.AssignProfileToProcessAsync(row.Executable, null);
        LoadFromService();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _service.RefreshProfilesAsync();
        LoadFromService();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selectedDefault = DefaultProfileCombo.SelectedItem as ProfileChoice;
        var current = _service.GetSettings();

        var settings = new AppSettings
        {
            AutoSwitchEnabled = AutoSwitchCheck.IsChecked == true,
            PollIntervalMs = current.PollIntervalMs,
            FuzzyMatchThreshold = current.FuzzyMatchThreshold,
            DefaultProfileId = string.IsNullOrWhiteSpace(selectedDefault?.Id)
                ? null
                : selectedDefault.Id,
            ProcessProfileMap = current.ProcessProfileMap
        };

        _service.UpdateSettings(settings);
        _service.SetAutoSwitchEnabled(settings.AutoSwitchEnabled);
        LoadFromService();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class ProfileChoice(string name, string? id)
    {
        public string Name { get; } = name;
        public string? Id { get; } = id;
    }

    private sealed class AssignmentRow(string executable, string profileName, string configId)
    {
        public string Executable { get; } = executable;
        public string ProfileName { get; } = profileName;
        public string ConfigId { get; } = configId;
    }
}
