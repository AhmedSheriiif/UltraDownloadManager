namespace UltraDownloadManager.Core.YtDlp;

/// <summary>One item in a playlist, from a --flat-playlist probe (cheap: no per-video metadata fetch).</summary>
public sealed class PlaylistEntry
{
    /// <summary>1-based position in the playlist - yt-dlp's --playlist-items selects by this.</summary>
    public int Index { get; set; }

    public string Title { get; set; } = string.Empty;
}
