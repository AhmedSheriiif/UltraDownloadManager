using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using UltraDownloadManager.App.Util;
using UltraDownloadManager.App.ViewModels;
using UltraDownloadManager.App.Views;
using UltraDownloadManager.Core.Aria2;
using UltraDownloadManager.Core.History;
using UltraDownloadManager.Core.Settings;
using UltraDownloadManager.Core.YtDlp;

namespace UltraDownloadManager.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private Aria2ProcessManager? _processManager;
    private CommandPipeServer? _commandPipeServer;

    public Aria2Client? Client { get; private set; }
    public YtDlpClient YtDlpClient { get; private set; } = null!;
    public HistoryService HistoryService { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = new();
    public SettingsService SettingsService { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "UltraDownloadManager-SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Ultra Download Manager is already running.", "Ultra Download Manager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Settings = SettingsService.Load();
        SettingsService.Save(Settings); // persist a freshly-generated RpcSecret on first run

        BrowserIntegration.EnsureRegistered(SettingsService);

        var aria2cPath = Path.Combine(AppContext.BaseDirectory, "aria2", "aria2c.exe");
        _processManager = new Aria2ProcessManager(Settings.RpcPort, Settings.RpcSecret);

        try
        {
            await _processManager.StartAsync(
                aria2cPath,
                Settings.DownloadDirectory,
                SettingsService.SessionFilePath,
                SettingsService.LogFilePath,
                Settings.MaxConcurrentDownloads,
                Settings.MaxConnectionsPerServer);
        }
        catch (Exception ex)
        {
            await _processManager.DisposeAsync();
            MessageBox.Show(
                $"Ultra Download Manager could not start its download engine (aria2):\n\n{ex.Message}\n\n" +
                "Check the log file for details:\n" + SettingsService.LogFilePath,
                "Startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
            Client = new Aria2Client(_processManager.RpcUrl, _processManager.RpcSecret);
            YtDlpClient = new YtDlpClient(
                Path.Combine(AppContext.BaseDirectory, "ytdlp", "yt-dlp.exe"),
                Path.Combine(AppContext.BaseDirectory, "ytdlp"));
            HistoryService = new HistoryService(SettingsService.HistoryDbPath);
            var mainViewModel = new MainViewModel(Client, YtDlpClient, HistoryService, Settings);
            var mainWindow = new MainWindow(mainViewModel);
            MainWindow = mainWindow;
            mainWindow.Show();
            mainViewModel.StartPolling();

            _commandPipeServer = new CommandPipeServer(mainViewModel);
            _commandPipeServer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to show the main window:\n\n{ex}", "Startup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            SettingsService.Save(Settings);
            _commandPipeServer?.Dispose();
            Client?.Dispose();
            if (_processManager is not null)
                await _processManager.DisposeAsync();
        }
        catch
        {
            // best effort cleanup on shutdown
        }
        finally
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
