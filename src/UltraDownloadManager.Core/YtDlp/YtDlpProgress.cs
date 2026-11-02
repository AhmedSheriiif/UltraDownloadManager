using System.Text.Json.Serialization;

namespace UltraDownloadManager.Core.YtDlp;

/// <summary>
/// Mirrors the fields yt-dlp exposes under its internal "progress" dict
/// (https://github.com/yt-dlp/yt-dlp --progress-template), as dumped via
/// our custom --progress-template line (see YtDlpClient).
/// </summary>
public sealed class YtDlpProgress
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("downloaded_bytes")]
    public long? DownloadedBytes { get; set; }

    [JsonPropertyName("total_bytes")]
    public long? TotalBytes { get; set; }

    [JsonPropertyName("total_bytes_estimate")]
    public double? TotalBytesEstimate { get; set; }

    [JsonPropertyName("speed")]
    public double? SpeedBytesPerSecond { get; set; }

    [JsonPropertyName("eta")]
    public double? EtaSeconds { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonIgnore]
    public long? EffectiveTotalBytes => TotalBytes ?? (long?)TotalBytesEstimate;

    [JsonIgnore]
    public double Progress01 => EffectiveTotalBytes is > 0 && DownloadedBytes is { } d
        ? Math.Clamp(d / (double)EffectiveTotalBytes.Value, 0, 1)
        : 0;
}
