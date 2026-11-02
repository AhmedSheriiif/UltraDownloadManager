using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using UltraDownloadManager.App.Util;
using UltraDownloadManager.App.ViewModels;
using UltraDownloadManager.App.Views;

namespace UltraDownloadManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TaskbarIcon _trayIcon;
    private bool _isExiting;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        DarkTitleBar.Apply(this);
        _trayIcon = BuildTrayIcon();

        _viewModel.DownloadCompleted += OnDownloadCompleted;
    }

    private void OnDownloadCompleted(ViewModels.DownloadItemViewModel item)
    {
        if (!_viewModel.Settings.ShowCompletionNotifications) return;
        _trayIcon.ShowBalloonTip("Download complete", item.Name, BalloonIcon.Info);
    }

    private TaskbarIcon BuildTrayIcon()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = "Ultra Download Manager",
            IconSource = LoadEmbeddedIcon()
        };

        icon.TrayMouseDoubleClick += (_, _) => ShowFromTray();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open" };
        openItem.Click += (_, _) => ShowFromTray();

        var pauseAllItem = new System.Windows.Controls.MenuItem { Header = "Pause All" };
        pauseAllItem.Click += async (_, _) => await _viewModel.PauseAllCommand.ExecuteAsync(null);

        var resumeAllItem = new System.Windows.Controls.MenuItem { Header = "Resume All" };
        resumeAllItem.Click += async (_, _) => await _viewModel.ResumeAllCommand.ExecuteAsync(null);

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            _isExiting = true;
            System.Windows.Application.Current.Shutdown();
        };

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(pauseAllItem);
        menu.Items.Add(resumeAllItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);
        icon.ContextMenu = menu;

        return icon;
    }

    private static BitmapFrame LoadEmbeddedIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var stream = info!.Stream;
        return BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.Settings.MinimizeToTrayOnClose)
            Hide();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting && _viewModel.Settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _trayIcon.Dispose();
    }

    private async void AddDownload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDownloadWindow(_viewModel.Settings.DownloadDirectory) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var urlsToAdd = new List<string>();
        foreach (var url in dialog.Urls)
        {
            if (ConfirmNotDuplicate(url))
                urlsToAdd.Add(url);
        }

        if (urlsToAdd.Count > 0)
            await _viewModel.AddUrlsAsync(urlsToAdd, dialog.CustomDirectory, dialog.StartPaused);
    }

    private void GrabVideo_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var dialog = new AddVideoWindow(app.YtDlpClient) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (dialog.IsPlaylist)
        {
            _viewModel.StartYtDlpPlaylistDownload(
                dialog.PlaylistUrl, dialog.SelectedEntries, dialog.SelectedQuality,
                _viewModel.Settings.DownloadDirectory, dialog.DownloadOptions);
            return;
        }

        if (dialog.Info is null) return;
        if (ConfirmNotDuplicate(dialog.Info.WebpageUrl ?? string.Empty))
            _viewModel.StartYtDlpDownload(dialog.Info, dialog.SelectedQuality, _viewModel.Settings.DownloadDirectory, dialog.DownloadOptions);
    }

    /// <summary>Returns true if the caller should proceed - either no prior download exists, or the user confirmed a re-download.</summary>
    private bool ConfirmNotDuplicate(string url)
    {
        var duplicate = _viewModel.FindCompletedDuplicate(url);
        if (duplicate is null) return true;

        var when = duplicate.CompletedAtUtc?.ToLocalTime().ToString("g") ?? "earlier";
        var result = MessageBox.Show(this,
            $"You already downloaded this on {when}:\n{duplicate.FilePath}\n\nDownload it again?",
            "Already downloaded", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var dialog = new HistoryWindow(app.HistoryService) { Owner = this };
        dialog.ShowDialog();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var app = (App)System.Windows.Application.Current;
            app.SettingsService.Save(_viewModel.Settings);
            await _viewModel.ApplySettingsAsync();
        }
    }

    private void DownloadsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.OpenContainingFolderCommand.CanExecute(null))
            _viewModel.OpenContainingFolderCommand.Execute(null);
    }

    private async void SpeedLimit_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ViewModels.DownloadItemViewModel item) return;

        var dialog = new SpeedLimitWindow(item.Name, currentLimitKb: 0) { Owner = this };
        if (dialog.ShowDialog() == true)
            await _viewModel.SetSpeedLimitAsync(item, dialog.KilobytesPerSecond);
    }
}
