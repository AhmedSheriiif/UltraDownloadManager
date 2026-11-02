using CommunityToolkit.Mvvm.ComponentModel;
using UltraDownloadManager.Core.Models;
using UltraDownloadManager.Core.Util;
using UltraDownloadManager.Core.YtDlp;

namespace UltraDownloadManager.App.ViewModels;

public enum DownloadSource
{
    Aria2,
    YtDlp
}

/// <summary>
/// Unified row for the download queue. Aria2-backed rows are refreshed wholesale from polling
/// (<see cref="UpdateFromAria2"/>); yt-dlp-backed rows are pushed incrementally from its process's
/// progress stream, since yt-dlp has no RPC/poll surface - it just supervises its own download.
/// </summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    public string Id { get; }
    public DownloadSource Source { get; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private DownloadStatus _status;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _speedText = "--";

    [ObservableProperty]
    private string _etaText = "--";

    [ObservableProperty]
    private string _connectionsText = "--";

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Resolved output path once known - used by "Open Folder".</summary>
    [ObservableProperty]
    private string? _filePath;

    public DownloadItem? Aria2Model { get; private set; }

    public string? Gid => Aria2Model?.Gid;

    public bool CanPause => Source == DownloadSource.Aria2 && Status is DownloadStatus.Active or DownloadStatus.Waiting;

    public bool CanResume => Source == DownloadSource.Aria2 && Status is DownloadStatus.Paused;

    public bool CanCancel => Source == DownloadSource.YtDlp && Status is DownloadStatus.Active or DownloadStatus.Waiting;

    /// <summary>Reordering only makes sense for items still waiting their turn in aria2's queue.</summary>
    public bool CanReorder => Source == DownloadSource.Aria2 && Status == DownloadStatus.Waiting;

    public bool CanSetSpeedLimit => Source == DownloadSource.Aria2 && Status is DownloadStatus.Active or DownloadStatus.Waiting or DownloadStatus.Paused;

    private DownloadItemViewModel(DownloadSource source, string id)
    {
        Source = source;
        Id = id;
    }

    public static DownloadItemViewModel FromAria2(DownloadItem item)
    {
        var vm = new DownloadItemViewModel(DownloadSource.Aria2, item.Gid);
        vm.UpdateFromAria2(item);
        return vm;
    }

    public static DownloadItemViewModel ForYtDlp(string id, string title, string outputDirectory)
    {
        var vm = new DownloadItemViewModel(DownloadSource.YtDlp, id)
        {
            Name = title,
            Status = DownloadStatus.Active,
            StatusText = "Downloading (video)",
            FilePath = outputDirectory
        };
        return vm;
    }

    public void UpdateFromAria2(DownloadItem item)
    {
        Aria2Model = item;
        Name = item.DisplayName;
        Status = item.Status;
        StatusText = Aria2StatusText(item.Status);
        ProgressPercent = Math.Clamp(item.Progress * 100.0, 0, 100);
        SizeText = item.TotalLength > 0
            ? $"{Formatting.Bytes(item.CompletedLength)} / {Formatting.Bytes(item.TotalLength)}"
            : Formatting.Bytes(item.CompletedLength);
        SpeedText = item.Status == DownloadStatus.Active ? Formatting.Speed(item.DownloadSpeed) : "--";
        EtaText = Formatting.Eta(item.Eta);
        ConnectionsText = item.Status == DownloadStatus.Active ? item.Connections.ToString() : "--";
        ErrorMessage = item.ErrorMessage;
        FilePath = item.Files.Count > 0 ? item.Files[0].Path : null;

        OnPropertyChanged(nameof(Gid));
        NotifyActionsChanged();
    }

    public void UpdateFromYtDlpProgress(YtDlpProgress progress)
    {
        Status = DownloadStatus.Active;
        StatusText = "Downloading (video)";
        ProgressPercent = progress.Progress01 * 100.0;
        SizeText = progress.EffectiveTotalBytes is { } total
            ? $"{Formatting.Bytes(progress.DownloadedBytes ?? 0)} / {Formatting.Bytes(total)}"
            : Formatting.Bytes(progress.DownloadedBytes ?? 0);
        SpeedText = progress.SpeedBytesPerSecond is { } speed ? Formatting.Speed((long)speed) : "--";
        EtaText = progress.EtaSeconds is { } eta ? Formatting.Eta(TimeSpan.FromSeconds(eta)) : "--";
        if (!string.IsNullOrEmpty(progress.Filename)) FilePath = progress.Filename;

        NotifyActionsChanged();
    }

    public void MarkYtDlpComplete(string? finalPath = null)
    {
        Status = DownloadStatus.Complete;
        StatusText = "Complete";
        ProgressPercent = 100;
        SpeedText = "--";
        EtaText = "--";
        if (!string.IsNullOrEmpty(finalPath)) FilePath = finalPath;
        NotifyActionsChanged();
    }

    public void MarkYtDlpCancelled()
    {
        Status = DownloadStatus.Removed;
        StatusText = "Cancelled";
        SpeedText = "--";
        EtaText = "--";
        NotifyActionsChanged();
    }

    public void MarkYtDlpError(string message)
    {
        Status = DownloadStatus.Error;
        StatusText = "Error";
        ErrorMessage = message;
        SpeedText = "--";
        EtaText = "--";
        NotifyActionsChanged();
    }

    private void NotifyActionsChanged()
    {
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(CanSetSpeedLimit));
    }

    private static string Aria2StatusText(DownloadStatus status) => status switch
    {
        DownloadStatus.Active => "Downloading",
        DownloadStatus.Waiting => "Queued",
        DownloadStatus.Paused => "Paused",
        DownloadStatus.Complete => "Complete",
        DownloadStatus.Error => "Error",
        DownloadStatus.Removed => "Removed",
        _ => "Unknown"
    };
}
