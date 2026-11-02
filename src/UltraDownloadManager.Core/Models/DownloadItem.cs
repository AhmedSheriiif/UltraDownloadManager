using System.Text.Json.Serialization;

namespace UltraDownloadManager.Core.Models;

/// <summary>
/// Maps 1:1 to the object aria2's tellStatus/tellActive/tellWaiting/tellStopped return.
/// Numeric fields arrive as JSON strings on the wire; the caller must deserialize with
/// JsonNumberHandling.AllowReadingFromString for these to populate.
/// </summary>
public sealed class DownloadItem
{
    [JsonPropertyName("gid")]
    public string Gid { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string StatusRaw { get; set; } = string.Empty;

    [JsonIgnore]
    public DownloadStatus Status => DownloadStatusExtensions.Parse(StatusRaw);

    [JsonPropertyName("totalLength")]
    public long TotalLength { get; set; }

    [JsonPropertyName("completedLength")]
    public long CompletedLength { get; set; }

    [JsonPropertyName("uploadLength")]
    public long UploadLength { get; set; }

    [JsonPropertyName("downloadSpeed")]
    public long DownloadSpeed { get; set; }

    [JsonPropertyName("uploadSpeed")]
    public long UploadSpeed { get; set; }

    [JsonPropertyName("connections")]
    public int Connections { get; set; }

    [JsonPropertyName("numSeeders")]
    public int NumSeeders { get; set; }

    [JsonPropertyName("dir")]
    public string Dir { get; set; } = string.Empty;

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("files")]
    public List<DownloadFile> Files { get; set; } = new();

    [JsonPropertyName("bittorrent")]
    public BittorrentInfo? Bittorrent { get; set; }

    [JsonIgnore]
    public double Progress => TotalLength <= 0 ? 0 : (double)CompletedLength / TotalLength;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (Bittorrent?.Info?.Name is { Length: > 0 } torrentName)
                return torrentName;

            var first = Files.FirstOrDefault();
            if (first is null || string.IsNullOrEmpty(first.Path))
                return Gid;

            return Path.GetFileName(first.Path) is { Length: > 0 } name ? name : first.Path;
        }
    }

    [JsonIgnore]
    public TimeSpan? Eta
    {
        get
        {
            if (DownloadSpeed <= 0 || Status != DownloadStatus.Active) return null;
            var remaining = TotalLength - CompletedLength;
            if (remaining <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(remaining / (double)DownloadSpeed);
        }
    }
}

public sealed class DownloadFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("length")]
    public long Length { get; set; }

    [JsonPropertyName("completedLength")]
    public long CompletedLength { get; set; }

    [JsonPropertyName("selected")]
    public string SelectedRaw { get; set; } = "true";

    [JsonIgnore]
    public bool Selected => SelectedRaw == "true";

    [JsonPropertyName("uris")]
    public List<FileUriInfo> Uris { get; set; } = new();

    [JsonIgnore]
    public string? Uri => Uris.FirstOrDefault(u => u.Status == "used")?.Value ?? Uris.FirstOrDefault()?.Value;
}

public sealed class FileUriInfo
{
    [JsonPropertyName("uri")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class BittorrentInfo
{
    [JsonPropertyName("info")]
    public BittorrentMeta? Info { get; set; }
}

public sealed class BittorrentMeta
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
