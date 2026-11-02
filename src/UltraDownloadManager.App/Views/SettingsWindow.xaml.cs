using System.Windows;
using Microsoft.Win32;
using UltraDownloadManager.App.Util;
using UltraDownloadManager.Core.Settings;

namespace UltraDownloadManager.App.Views;

public partial class SettingsWindow : Window
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "UltraDownloadManager";

    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        _settings = settings;

        DirectoryTextBox.Text = settings.DownloadDirectory;
        MaxConcurrentTextBox.Text = settings.MaxConcurrentDownloads.ToString();
        MaxConnectionsTextBox.Text = settings.MaxConnectionsPerServer.ToString();
        SpeedLimitTextBox.Text = settings.GlobalSpeedLimitKb.ToString();
        MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTrayOnClose;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        AutoCategorizeCheckBox.IsChecked = settings.AutoCategorize;
        WarnOnDuplicateCheckBox.IsChecked = settings.WarnOnDuplicateDownload;
        ShowNotificationsCheckBox.IsChecked = settings.ShowCompletionNotifications;
        SchedulingEnabledCheckBox.IsChecked = settings.SchedulingEnabled;
        ScheduleStartTextBox.Text = settings.ScheduleStartHour.ToString();
        ScheduleEndTextBox.Text = settings.ScheduleEndHour.ToString();
        ScheduleRow.IsEnabled = settings.SchedulingEnabled;
    }

    private void SchedulingEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ScheduleRow.IsEnabled = SchedulingEnabledCheckBox.IsChecked == true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = DirectoryTextBox.Text,
            Title = "Choose default download folder"
        };

        if (dialog.ShowDialog(this) == true)
            DirectoryTextBox.Text = dialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxConcurrentTextBox.Text, out var maxConcurrent) || maxConcurrent < 1)
        {
            ShowValidationError("Max concurrent downloads must be a positive number.");
            return;
        }

        if (!int.TryParse(MaxConnectionsTextBox.Text, out var maxConnections) || maxConnections < 1)
        {
            ShowValidationError("Connections per download must be a positive number.");
            return;
        }

        if (!int.TryParse(SpeedLimitTextBox.Text, out var speedLimit) || speedLimit < 0)
        {
            ShowValidationError("Speed limit must be zero or a positive number.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DirectoryTextBox.Text))
        {
            ShowValidationError("Choose a default download folder.");
            return;
        }

        var schedulingEnabled = SchedulingEnabledCheckBox.IsChecked == true;
        if (schedulingEnabled &&
            (!int.TryParse(ScheduleStartTextBox.Text, out var startHour) || startHour is < 0 or > 23 ||
             !int.TryParse(ScheduleEndTextBox.Text, out var endHour) || endHour is < 0 or > 23))
        {
            ShowValidationError("Schedule hours must be between 0 and 23.");
            return;
        }

        _settings.DownloadDirectory = DirectoryTextBox.Text.Trim();
        _settings.MaxConcurrentDownloads = maxConcurrent;
        _settings.MaxConnectionsPerServer = maxConnections;
        _settings.GlobalSpeedLimitKb = speedLimit;
        _settings.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.AutoCategorize = AutoCategorizeCheckBox.IsChecked == true;
        _settings.WarnOnDuplicateDownload = WarnOnDuplicateCheckBox.IsChecked == true;
        _settings.ShowCompletionNotifications = ShowNotificationsCheckBox.IsChecked == true;
        _settings.SchedulingEnabled = schedulingEnabled;
        if (schedulingEnabled)
        {
            _settings.ScheduleStartHour = int.Parse(ScheduleStartTextBox.Text);
            _settings.ScheduleEndHour = int.Parse(ScheduleEndTextBox.Text);
        }

        ApplyStartWithWindows(_settings.StartWithWindows);

        DialogResult = true;
    }

    private static void ApplyStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch
        {
            // non-fatal: user can still toggle this later
        }
    }

    private void ShowValidationError(string message) =>
        MessageBox.Show(this, message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
