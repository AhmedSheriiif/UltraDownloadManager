namespace UltraDownloadManager.Core.YtDlp;

public enum VideoQuality
{
    Best,
    Quality1080p,
    Quality720p,
    Quality480p,
    AudioOnlyMp3
}

public static class VideoQualityExtensions
{
    public static string DisplayName(this VideoQuality quality) => quality switch
    {
        VideoQuality.Best => "Best available (video + audio)",
        VideoQuality.Quality1080p => "1080p",
        VideoQuality.Quality720p => "720p",
        VideoQuality.Quality480p => "480p",
        VideoQuality.AudioOnlyMp3 => "Audio only (MP3)",
        _ => quality.ToString()
    };
}
