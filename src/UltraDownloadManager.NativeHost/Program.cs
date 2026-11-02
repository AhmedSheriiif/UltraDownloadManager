using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using UltraDownloadManager.Core.YtDlp;

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

var message = ReadMessage(stdin);
if (message is null)
{
    return;
}

var action = message.Value.TryGetProperty("action", out var actionEl) ? actionEl.GetString() : null;

object response = action switch
{
    "addDownload" => await HandleAddDownloadAsync(message.Value),
    "ping" => new { success = true, message = "pong" },
    _ => new { success = false, message = $"Unknown action: {action}" }
};

WriteMessage(stdout, response);
return;

static JsonElement? ReadMessage(Stream input)
{
    var lengthBytes = new byte[4];
    if (!ReadExact(input, lengthBytes)) return null;

    var length = BitConverter.ToInt32(lengthBytes, 0);
    if (length <= 0 || length > 10 * 1024 * 1024) return null;

    var buffer = new byte[length];
    if (!ReadExact(input, buffer)) return null;

    using var doc = JsonDocument.Parse(buffer);
    return doc.RootElement.Clone();
}

static bool ReadExact(Stream stream, byte[] buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = stream.Read(buffer, offset, buffer.Length - offset);
        if (read <= 0) return false;
        offset += read;
    }
    return true;
}

static void WriteMessage(Stream output, object payload)
{
    var json = JsonSerializer.Serialize(payload);
    var bytes = Encoding.UTF8.GetBytes(json);
    output.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
    output.Write(bytes, 0, bytes.Length);
    output.Flush();
}

static async Task<object> HandleAddDownloadAsync(JsonElement root)
{
    var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
    if (string.IsNullOrWhiteSpace(url))
        return new { success = false, message = "Missing url." };

    var referrer = root.TryGetProperty("referrer", out var r) ? r.GetString() : null;
    var cookie = root.TryGetProperty("cookie", out var c) ? c.GetString() : null;

    // Try yt-dlp first: a plain file download manager can't fetch a YouTube/Twitter/TikTok/etc.
    // page URL directly, so if yt-dlp recognizes this link, route it to the app's video pipeline
    // instead of handing the page URL to aria2, where it would just fail. Skip the (slow-ish)
    // probe entirely for URLs that already look like direct file links, so ordinary downloads
    // (zip, pdf, a CDN-hosted video src, ...) keep going straight to aria2 with no added latency.
    if (!LooksLikeDirectFileLink(url!))
    {
        var ytDlpResult = await TryRouteToYtDlpAsync(url!);
        if (ytDlpResult is not null)
            return ytDlpResult;
    }

    // Hand off to the running app over the command pipe instead of calling aria2 directly, so the
    // app can show its "Download or Cancel / choose a folder" confirmation before anything starts.
    // The pipe round-trip blocks on that confirmation, so a null result here means the pipe itself
    // was unreachable (app not running yet) - not that the user declined - which is the only case
    // that should trigger launching the app and retrying.
    var payload = JsonSerializer.Serialize(new { action = "addDownload", url, referrer, cookie });
    return await SendToAppAsync(payload) ?? new { success = false, message = "Ultra Download Manager isn't responding." };
}

static bool LooksLikeDirectFileLink(string url)
{
    var directFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".exe", ".msi", ".dmg", ".apk", ".iso",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv",
        ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".wma",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".m4v", ".webm",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg"
    };

    try
    {
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        return ext.Length > 0 && directFileExtensions.Contains(ext);
    }
    catch (UriFormatException)
    {
        return false;
    }
}

/// <summary>
/// Returns a response object if yt-dlp recognized the URL and the add-video command was handed
/// off to the app; null if yt-dlp doesn't support this URL, so the caller should fall back to
/// a plain aria2 download instead.
/// </summary>
static async Task<object?> TryRouteToYtDlpAsync(string url)
{
    var ytDlpPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ytdlp", "yt-dlp.exe"));
    if (!File.Exists(ytDlpPath))
        return null;

    MediaInfo info;
    try
    {
        var ytDlpClient = new YtDlpClient(ytDlpPath, Path.GetDirectoryName(ytDlpPath)!);
        using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        info = await ytDlpClient.GetInfoAsync(url, probeCts.Token);
    }
    catch
    {
        return null; // not a yt-dlp-recognized URL (or an ordinary direct file link) - let aria2 handle it
    }

    // yt-dlp's "Generic" extractor matches almost any URL (it scrapes the page for embeddable
    // media), including plain non-video files - it isn't a real "this site is supported" signal.
    // Only treat an actual site-specific extractor match as yt-dlp recognizing the URL.
    if (string.Equals(info.ExtractorKey, "Generic", StringComparison.OrdinalIgnoreCase))
        return null;

    var payload = JsonSerializer.Serialize(new
    {
        action = "addVideo",
        url = info.WebpageUrl ?? url,
        title = info.Title,
        quality = "Best"
    });

    return await SendToAppAsync(payload) ?? new { success = false, message = "Ultra Download Manager isn't responding." };
}

/// <summary>
/// Sends a command to the running app over the named pipe, launching it and retrying (up to 15s)
/// if it isn't reachable yet. Returns the app's own response - which may itself carry
/// success:false, e.g. if the user declined a confirmation - or null only if the app never
/// became reachable at all.
/// </summary>
static async Task<object?> SendToAppAsync(string payload)
{
    var response = await TrySendPipeCommandAsync(payload, TimeSpan.FromSeconds(3));
    if (response is not null)
        return ParseResponse(response);

    TryLaunchApp();
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < deadline)
    {
        await Task.Delay(500);
        response = await TrySendPipeCommandAsync(payload, TimeSpan.FromSeconds(2));
        if (response is not null)
            return ParseResponse(response);
    }

    return null;
}

static object ParseResponse(string response)
{
    try
    {
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.Clone();
    }
    catch (JsonException)
    {
        return new { success = false, message = "Ultra Download Manager sent an unreadable response." };
    }
}

static async Task<string?> TrySendPipeCommandAsync(string payload, TimeSpan timeout)
{
    try
    {
        using var pipe = new NamedPipeClientStream(".", "UltraDownloadManager.Commands", PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = new CancellationTokenSource(timeout);
        await pipe.ConnectAsync((int)timeout.TotalMilliseconds, cts.Token);

        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        await writer.WriteLineAsync(payload);
        return await reader.ReadLineAsync();
    }
    catch
    {
        return null;
    }
}

static void TryLaunchApp()
{
    try
    {
        var appExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "UltraDownloadManager.exe"));
        if (File.Exists(appExe))
            Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true });
    }
    catch
    {
        // best effort - if this fails the caller's reachability retry loop will just time out
    }
}
