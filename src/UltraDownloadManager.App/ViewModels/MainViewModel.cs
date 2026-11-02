using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using UltraDownloadManager.Core.Aria2;
using UltraDownloadManager.Core.Categorization;
using UltraDownloadManager.Core.History;
using UltraDownloadManager.Core.Models;
using UltraDownloadManager.Core.Scheduling;
using UltraDownloadManager.Core.Settings;
using UltraDownloadManager.Core.Util;
using UltraDownloadManager.Core.YtDlp;

namespace UltraDownloadManager.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Aria2Client _client;
    private readonly YtDlpClient _ytDlpClient;
    private readonly HistoryService _historyService;
    private readonly DispatcherTimer _pollTimer;
    private readonly Dictionary<string, CancellationTokenSource> _ytDlpCancellations = new();
    private readonly HashSet<string> _historyFinalized = new();
    private bool _isPolling;
    private bool? _wasWithinScheduleWindow;

    public AppSettings Settings { get; }
    public HistoryService History => _historyService;

    /// <summary>Fired once, on the UI thread, the moment a download (either engine) finishes successfully.</summary>
    public event Action<DownloadItemViewModel>? DownloadCompleted;

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenContainingFolderCommand))]
    private DownloadItemViewModel? _selectedDownload;

    [ObservableProperty]
    private string _globalDownloadSpeedText = "0 B/s";

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private int _queuedCount;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainViewModel(Aria2Client client, YtDlpClient ytDlpClient, HistoryService historyService, AppSettings settings)
    {
        _client = client;
        _ytDlpClient = ytDlpClient;
        _historyService = historyService;
        Settings = settings;

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += async (_, _) => await PollAsync();
    }

    public void StartPolling() => _pollTimer.Start();

    public void StopPolling() => _pollTimer.Stop();

    private async Task PollAsync()
    {
        if (_isPolling) return;
        _isPolling = true;
        try
        {
            await ApplyScheduleAsync();

            var active = await _client.TellActiveAsync();
            var waiting = await _client.TellWaitingAsync(0, 1000);
            var stopped = await _client.TellStoppedAsync(0, 1000);
            var stat = await _client.GetGlobalStatAsync();

            var all = active.Concat(waiting).Concat(stopped).ToList();
            MergeDownloads(all);

            GlobalDownloadSpeedText = Formatting.Speed(stat.DownloadSpeed);
            ActiveCount = stat.NumActive;
            QueuedCount = stat.NumWaiting;
            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            StatusMessage = $"aria2 connection error: {ex.Message}";
        }
        finally
        {
            _isPolling = false;
        }
    }

    /// <summary>
    /// Pauses/resumes everything on transitions in/out of the configured download time window.
    /// Only acts on transitions (not every tick) so it doesn't fight manual pause/resume clicks
    /// mid-window.
    /// </summary>
    private async Task ApplyScheduleAsync()
    {
        var isWithinWindow = !Settings.SchedulingEnabled
            || ScheduleWindow.IsWithinWindow(Settings.ScheduleStartHour, Settings.ScheduleEndHour, DateTime.Now);

        if (_wasWithinScheduleWindow == isWithinWindow) return;
        var isFirstCheck = _wasWithinScheduleWindow is null;
        _wasWithinScheduleWindow = isWithinWindow;
        if (isFirstCheck) return; // don't force a pause/resume state on startup, only on later transitions

        try
        {
            if (isWithinWindow)
            {
                await _client.UnpauseAllAsync();
                StatusMessage = "Schedule window opened - downloads resumed.";
            }
            else
            {
                await _client.PauseAllAsync();
                StatusMessage = "Outside the scheduled download window - downloads paused.";
            }
        }
        catch
        {
            // best effort - next tick retries since state didn't actually flip
        }
    }

    /// <summary>
    /// Maintains two invariants in <see cref="Downloads"/>: yt-dlp rows (inserted at index 0
    /// elsewhere) always stay at the front, and aria2 rows form a contiguous tail block kept in
    /// the exact order aria2 itself reports (tellActive+tellWaiting+tellStopped) - that order is
    /// what makes "move up/down" (aria2.changePosition) visible, and what makes the queue reflect
    /// real aria2 priority rather than just insertion order.
    /// Uses <see cref="ObservableCollection{T}.Move"/> rather than remove+re-add so a poll tick
    /// where nothing actually reordered doesn't disturb the list's selection or flicker.
    /// </summary>
    private void MergeDownloads(List<DownloadItem> latest)
    {
        var byGid = latest.ToDictionary(d => d.Gid);

        for (var i = Downloads.Count - 1; i >= 0; i--)
        {
            var vm = Downloads[i];
            if (vm.Source != DownloadSource.Aria2) continue; // yt-dlp rows aren't driven by aria2 polling
            if (vm.Gid is null || !byGid.ContainsKey(vm.Gid))
                Downloads.RemoveAt(i);
        }

        var vmByGid = Downloads
            .Where(d => d.Source == DownloadSource.Aria2 && d.Gid is not null)
            .ToDictionary(d => d.Gid!);

        foreach (var item in latest)
        {
            if (vmByGid.TryGetValue(item.Gid, out var vm))
            {
                vm.UpdateFromAria2(item);
            }
            else
            {
                vm = InsertAria2Row(item);
                vmByGid[item.Gid] = vm;
            }

            RecordHistoryIfTerminal(vm, item);
        }

        var tailStart = Downloads.Count - latest.Count;
        for (var targetPos = 0; targetPos < latest.Count; targetPos++)
        {
            var vm = vmByGid[latest[targetPos].Gid];
            var currentPos = Downloads.IndexOf(vm);
            var desiredPos = tailStart + targetPos;
            if (currentPos != desiredPos)
                Downloads.Move(currentPos, desiredPos);
        }
    }

    private DownloadItemViewModel InsertAria2Row(DownloadItem item)
    {
        var vm = DownloadItemViewModel.FromAria2(item);
        Downloads.Add(vm); // appended to the aria2 tail block; MergeDownloads fixes exact order right after

        var url = item.Files.Count > 0 ? item.Files[0].Uri : null;
        _historyService.Upsert(new HistoryEntry
        {
            Id = item.Gid,
            Url = url ?? string.Empty,
            FileName = item.DisplayName,
            FilePath = item.Files.Count > 0 ? item.Files[0].Path : null,
            Category = CategoryRouter.CategoryForFileName(item.DisplayName),
            Source = "aria2",
            Status = "active"
        });

        return vm;
    }

    private void RecordHistoryIfTerminal(DownloadItemViewModel vm, DownloadItem item)
    {
        if (vm.Status is not (DownloadStatus.Complete or DownloadStatus.Error))
            return;
        if (!_historyFinalized.Add(vm.Id))
            return;

        _historyService.Upsert(new HistoryEntry
        {
            Id = item.Gid,
            Url = item.Files.Count > 0 ? item.Files[0].Uri ?? string.Empty : string.Empty,
            FileName = item.DisplayName,
            FilePath = item.Files.Count > 0 ? item.Files[0].Path : null,
            Category = CategoryRouter.CategoryForFileName(item.DisplayName),
            SizeBytes = item.TotalLength,
            Source = "aria2",
            Status = vm.Status == DownloadStatus.Complete ? "complete" : "error",
            CompletedAtUtc = DateTime.UtcNow
        });

        if (vm.Status == DownloadStatus.Complete)
            DownloadCompleted?.Invoke(vm);
    }

    public async Task AddUrlsAsync(IEnumerable<string> urls, string? customDirectory, bool startPaused)
    {
        foreach (var url in urls.Select(u => u.Trim()).Where(u => u.Length > 0))
        {
            var directory = customDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                var category = CategoryRouter.CategoryForUrl(url);
                directory = CategoryRouter.ResolveDirectory(Settings.DownloadDirectory, category, Settings.AutoCategorize);
            }

            var options = new Dictionary<string, string> { ["dir"] = directory };
            if (startPaused) options["pause"] = "true";

            try
            {
                await _client.AddUriAsync(new[] { url }, options);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to add {url}: {ex.Message}";
            }
        }

        await PollAsync();
    }

    /// <summary>Looks up a prior successful download of this exact URL, for duplicate warnings.</summary>
    public HistoryEntry? FindCompletedDuplicate(string url) =>
        Settings.WarnOnDuplicateDownload ? _historyService.FindCompletedByUrl(url) : null;

    /// <summary>Adds a download confirmed via the browser-extension pipe, carrying its referrer/cookies along.</summary>
    public async Task AddBrowserDownloadAsync(string url, string? referrer, string? cookie, string directory)
    {
        var options = new Dictionary<string, string> { ["dir"] = directory };
        if (!string.IsNullOrWhiteSpace(referrer)) options["referer"] = referrer;
        if (!string.IsNullOrWhiteSpace(cookie)) options["header"] = $"Cookie: {cookie}";

        try
        {
            await _client.AddUriAsync(new[] { url }, options);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to add {url}: {ex.Message}";
        }

        await PollAsync();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseSelectedAsync(DownloadItemViewModel? item)
    {
        if ((item ?? SelectedDownload)?.Gid is not { } gid) return;
        await _client.PauseAsync(gid);
        await PollAsync();
    }

    private bool CanPause(DownloadItemViewModel? item) => (item ?? SelectedDownload)?.CanPause == true;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeSelectedAsync(DownloadItemViewModel? item)
    {
        if ((item ?? SelectedDownload)?.Gid is not { } gid) return;
        await _client.UnpauseAsync(gid);
        await PollAsync();
    }

    private bool CanResume(DownloadItemViewModel? item) => (item ?? SelectedDownload)?.CanResume == true;

    [RelayCommand]
    private async Task RemoveSelectedAsync(DownloadItemViewModel? item)
    {
        var vm = item ?? SelectedDownload;
        if (vm is null) return;

        if (vm.Status == DownloadStatus.Complete && !string.IsNullOrEmpty(vm.FilePath) && File.Exists(vm.FilePath))
        {
            var result = MessageBox.Show(
                $"Remove \"{vm.Name}\" from the list?\n\nYes = remove from list and delete the file\nNo = remove from list only",
                "Remove download", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes)
            {
                try { File.Delete(vm.FilePath); }
                catch (Exception ex) { StatusMessage = $"Could not delete file: {ex.Message}"; }
            }
        }

        if (vm.Source == DownloadSource.YtDlp)
        {
            if (_ytDlpCancellations.TryGetValue(vm.Id, out var cts))
                cts.Cancel();
            Downloads.Remove(vm);
            _historyService.Remove(vm.Id);
            return;
        }

        var gid = vm.Gid!;
        try
        {
            if (vm.Status is DownloadStatus.Complete or DownloadStatus.Error or DownloadStatus.Removed)
                await _client.RemoveDownloadResultAsync(gid);
            else
                await _client.RemoveAsync(gid, force: true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to remove: {ex.Message}";
        }

        Downloads.Remove(vm);
        _historyService.Remove(gid);
    }

    [RelayCommand]
    private void OpenContainingFolder(DownloadItemViewModel? item)
    {
        var path = (item ?? SelectedDownload)?.FilePath;
        if (string.IsNullOrEmpty(path)) return;

        // Always open the PARENT folder with the item highlighted - whether the item is a file or
        // (for a yt-dlp row whose final filename wasn't captured) a directory - rather than entering
        // it. If nothing at this path exists any more, don't hand Explorer a dead argument: it
        // silently falls back to its own default location instead of erroring, which looks like a
        // random unrelated folder opened instead of a visible failure.
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            StatusMessage = $"Can't find \"{path}\" - it may have been moved or deleted.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // best effort, ignore if Explorer can't be launched
        }
    }

    [RelayCommand]
    private async Task MoveUpAsync(DownloadItemViewModel? item)
    {
        var vm = item ?? SelectedDownload;
        if (vm?.Gid is not { } gid || !vm.CanReorder) return;
        try { await _client.ChangePositionAsync(gid, -1, "POS_CUR"); }
        catch (Exception ex) { StatusMessage = $"Could not reorder: {ex.Message}"; }
        await PollAsync();
    }

    [RelayCommand]
    private async Task MoveDownAsync(DownloadItemViewModel? item)
    {
        var vm = item ?? SelectedDownload;
        if (vm?.Gid is not { } gid || !vm.CanReorder) return;
        try { await _client.ChangePositionAsync(gid, 1, "POS_CUR"); }
        catch (Exception ex) { StatusMessage = $"Could not reorder: {ex.Message}"; }
        await PollAsync();
    }

    /// <summary>0 means remove the per-download limit (unlimited again).</summary>
    public async Task SetSpeedLimitAsync(DownloadItemViewModel item, int kilobytesPerSecond)
    {
        if (item.Gid is not { } gid) return;
        var bytes = kilobytesPerSecond <= 0 ? "0" : (kilobytesPerSecond * 1024).ToString();
        try
        {
            await _client.ChangeOptionAsync(gid, new Dictionary<string, string> { ["max-download-limit"] = bytes });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not set speed limit: {ex.Message}";
        }
    }

    /// <summary>
    /// Starts a yt-dlp-backed download. Unlike aria2 downloads this isn't added via an engine
    /// RPC call - yt-dlp supervises its own process end to end, so we track it ourselves and
    /// push progress into the row directly instead of polling for it.
    /// </summary>
    public void StartYtDlpDownload(MediaInfo info, VideoQuality quality, string baseDirectory, YtDlpDownloadOptions? options = null)
    {
        var category = quality == VideoQuality.AudioOnlyMp3 ? "Music" : "Videos";
        var outputDirectory = CategoryRouter.ResolveDirectory(baseDirectory, category, Settings.AutoCategorize);

        var id = Guid.NewGuid().ToString("N");
        var vm = DownloadItemViewModel.ForYtDlp(id, info.Title, outputDirectory);
        Downloads.Insert(0, vm);

        var cts = new CancellationTokenSource();
        _ytDlpCancellations[id] = cts;

        var progress = new Progress<YtDlpProgress>(vm.UpdateFromYtDlpProgress);
        var url = info.WebpageUrl ?? string.Empty;

        _historyService.Upsert(new HistoryEntry
        {
            Id = id,
            Url = url,
            FileName = info.Title,
            FilePath = outputDirectory,
            Category = category,
            Source = "ytdlp",
            Status = "active"
        });

        _ = RunYtDlpDownloadAsync(vm, url, quality, outputDirectory, category, progress, cts, options);
    }

    /// <summary>
    /// Queues one yt-dlp download per selected playlist entry, each as its own queue row/process -
    /// yt-dlp's --playlist-items picks the entry out of the original playlist URL, so no per-video
    /// URL reconstruction is needed (robust across every site yt-dlp supports, not just YouTube).
    /// </summary>
    public void StartYtDlpPlaylistDownload(
        string playlistUrl, IEnumerable<PlaylistEntry> selectedEntries, VideoQuality quality,
        string baseDirectory, YtDlpDownloadOptions? baseOptions = null)
    {
        var category = quality == VideoQuality.AudioOnlyMp3 ? "Music" : "Videos";
        var outputDirectory = CategoryRouter.ResolveDirectory(baseDirectory, category, Settings.AutoCategorize);

        foreach (var entry in selectedEntries)
        {
            var id = Guid.NewGuid().ToString("N");
            var vm = DownloadItemViewModel.ForYtDlp(id, entry.Title, outputDirectory);
            Downloads.Insert(0, vm);

            var cts = new CancellationTokenSource();
            _ytDlpCancellations[id] = cts;
            var progress = new Progress<YtDlpProgress>(vm.UpdateFromYtDlpProgress);

            var options = new YtDlpDownloadOptions
            {
                PlaylistItemIndex = entry.Index,
                DownloadSubtitles = baseOptions?.DownloadSubtitles ?? false,
                EmbedThumbnail = baseOptions?.EmbedThumbnail ?? false
            };

            _historyService.Upsert(new HistoryEntry
            {
                Id = id,
                Url = playlistUrl,
                FileName = entry.Title,
                FilePath = outputDirectory,
                Category = category,
                Source = "ytdlp",
                Status = "active"
            });

            _ = RunYtDlpDownloadAsync(vm, playlistUrl, quality, outputDirectory, category, progress, cts, options);
        }
    }

    private async Task RunYtDlpDownloadAsync(
        DownloadItemViewModel vm, string url, VideoQuality quality, string outputDirectory, string category,
        IProgress<YtDlpProgress> progress, CancellationTokenSource cts, YtDlpDownloadOptions? options = null)
    {
        try
        {
            var finalPath = await _ytDlpClient.DownloadAsync(url, quality, outputDirectory, progress, options, cts.Token);
            vm.MarkYtDlpComplete(finalPath);
            _historyService.Upsert(new HistoryEntry
            {
                Id = vm.Id,
                Url = url,
                FileName = vm.Name,
                FilePath = vm.FilePath,
                Category = category,
                Source = "ytdlp",
                Status = "complete",
                CompletedAtUtc = DateTime.UtcNow
            });
            DownloadCompleted?.Invoke(vm);
        }
        catch (OperationCanceledException)
        {
            vm.MarkYtDlpCancelled();
            _historyService.Remove(vm.Id);
        }
        catch (Exception ex)
        {
            vm.MarkYtDlpError(ex.Message);
            _historyService.Upsert(new HistoryEntry
            {
                Id = vm.Id,
                Url = url,
                FileName = vm.Name,
                Category = category,
                Source = "ytdlp",
                Status = "error",
                CompletedAtUtc = DateTime.UtcNow
            });
        }
        finally
        {
            _ytDlpCancellations.Remove(vm.Id);
            cts.Dispose();
        }
    }

    [RelayCommand]
    private async Task PauseAllAsync()
    {
        await _client.PauseAllAsync();
        await PollAsync();
    }

    [RelayCommand]
    private async Task ResumeAllAsync()
    {
        await _client.UnpauseAllAsync();
        await PollAsync();
    }

    /// <summary>
    /// Pushes the current settings to the running aria2 instance so most changes take effect
    /// immediately without restarting the app. Directory/connection changes only affect
    /// downloads added afterward; speed limit and concurrency apply instantly.
    /// </summary>
    public async Task ApplySettingsAsync()
    {
        var limitBytes = Settings.GlobalSpeedLimitKb <= 0 ? "0" : (Settings.GlobalSpeedLimitKb * 1024).ToString();

        try
        {
            await _client.ChangeGlobalOptionAsync(new Dictionary<string, string>
            {
                ["max-overall-download-limit"] = limitBytes,
                ["max-concurrent-downloads"] = Settings.MaxConcurrentDownloads.ToString()
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not apply speed/concurrency settings live: {ex.Message}";
        }

        try
        {
            await _client.ChangeGlobalOptionAsync(new Dictionary<string, string>
            {
                ["dir"] = Settings.DownloadDirectory,
                ["max-connection-per-server"] = Settings.MaxConnectionsPerServer.ToString()
            });
        }
        catch
        {
            // these only take effect for new downloads on some aria2 builds; non-fatal if rejected
        }
    }

    public ValueTask DisposeAsync()
    {
        _pollTimer.Stop();
        foreach (var cts in _ytDlpCancellations.Values)
            cts.Cancel();
        return ValueTask.CompletedTask;
    }
}
