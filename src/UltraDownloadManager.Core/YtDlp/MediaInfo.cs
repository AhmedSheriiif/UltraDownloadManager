using System.Text.Json.Serialization;

namespace UltraDownloadManager.Core.YtDlp;

/// <summary>Subset of yt-dlp's --dump-json output that the UI actually needs.</summary>
public sealed class MediaInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("uploader")]
    public string? Uploader { get; set; }

    [JsonPropertyName("webpage_url")]
    public string? WebpageUrl { get; set; }

    [JsonPropertyName("extractor_key")]
    public string? ExtractorKey { get; set; }

    [JsonIgnore]
    public string DurationText
    {
        get
        {
            if (DurationSeconds is not { } seconds) return "--";
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes:00}:{t.Seconds:00}";
        }
    }
}
