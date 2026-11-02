namespace UltraDownloadManager.Core.YtDlp;

public sealed class YtDlpDownloadOptions
{
    /// <summary>1-based playlist position to grab (via --playlist-items). Null downloads a single video (--no-playlist).</summary>
    public int? PlaylistItemIndex { get; set; }

    public bool DownloadSubtitles { get; set; }

    public bool EmbedThumbnail { get; set; }
}
