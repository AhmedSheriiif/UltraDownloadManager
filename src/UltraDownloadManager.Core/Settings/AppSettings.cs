namespace UltraDownloadManager.Core.Settings;

public sealed class AppSettings
{
    public string DownloadDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "UltraDownloadManager");

    public int MaxConcurrentDownloads { get; set; } = 5;

    public int MaxConnectionsPerServer { get; set; } = 16;

    /// <summary>0 means unlimited.</summary>
    public int GlobalSpeedLimitKb { get; set; } = 0;

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public int RpcPort { get; set; } = 6800;

    /// <summary>
    /// Shared secret for aria2's RPC interface. Persisted (not regenerated per run) so that
    /// other local, trusted processes - namely the browser native messaging host - can reach
    /// the same aria2 instance the main app started.
    /// </summary>
    public string RpcSecret { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Routes completed downloads into Videos/Music/Documents/... subfolders by file type.</summary>
    public bool AutoCategorize { get; set; } = false;

    public bool SchedulingEnabled { get; set; } = false;

    /// <summary>Local hour (0-23) downloads are allowed to run from. Equal to ScheduleEndHour = always allowed.</summary>
    public int ScheduleStartHour { get; set; } = 22;

    /// <summary>Local hour (0-23) downloads are allowed to run until. Wraps past midnight if less than the start hour.</summary>
    public int ScheduleEndHour { get; set; } = 7;

    public bool WarnOnDuplicateDownload { get; set; } = true;

    public bool ShowCompletionNotifications { get; set; } = true;
}
