namespace UltraDownloadManager.Core.Models;

public enum DownloadStatus
{
    Unknown,
    Active,
    Waiting,
    Paused,
    Error,
    Complete,
    Removed
}

public static class DownloadStatusExtensions
{
    public static DownloadStatus Parse(string? aria2Status) => aria2Status switch
    {
        "active" => DownloadStatus.Active,
        "waiting" => DownloadStatus.Waiting,
        "paused" => DownloadStatus.Paused,
        "error" => DownloadStatus.Error,
        "complete" => DownloadStatus.Complete,
        "removed" => DownloadStatus.Removed,
        _ => DownloadStatus.Unknown
    };
}
