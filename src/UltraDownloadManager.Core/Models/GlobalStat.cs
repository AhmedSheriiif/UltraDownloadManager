using System.Text.Json.Serialization;

namespace UltraDownloadManager.Core.Models;

public sealed class GlobalStat
{
    [JsonPropertyName("downloadSpeed")]
    public long DownloadSpeed { get; set; }

    [JsonPropertyName("uploadSpeed")]
    public long UploadSpeed { get; set; }

    [JsonPropertyName("numActive")]
    public int NumActive { get; set; }

    [JsonPropertyName("numWaiting")]
    public int NumWaiting { get; set; }

    [JsonPropertyName("numStopped")]
    public int NumStopped { get; set; }
}
