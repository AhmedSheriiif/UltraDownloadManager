namespace UltraDownloadManager.Core.History;

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string Category { get; set; } = "Other";
    public long SizeBytes { get; set; }
    public string Source { get; set; } = "aria2"; // "aria2" or "ytdlp"
    public string Status { get; set; } = "active"; // active, complete, error, cancelled
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
