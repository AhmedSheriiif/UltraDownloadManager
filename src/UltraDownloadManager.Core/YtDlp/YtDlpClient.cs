using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraDownloadManager.Core.YtDlp;

/// <summary>
/// Thin process wrapper around the bundled yt-dlp.exe. yt-dlp does its own fragment/segment
/// downloading (it already handles HLS/DASH, retries, and concurrency), so unlike aria2-backed
/// downloads this client supervises the whole download rather than handing URLs to an engine.
/// </summary>
public sealed class YtDlpClient
{
    private const string ProgressMarker = "UDM_PROGRESS:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly string _ytDlpPath;
    private readonly string _ffmpegDirectory;

    public YtDlpClient(string ytDlpPath, string ffmpegDirectory)
    {
        _ytDlpPath = ytDlpPath;
        _ffmpegDirectory = ffmpegDirectory;
    }

    public async Task<MediaInfo> GetInfoAsync(string url, CancellationToken ct = default)
    {
        var psi = BuildProcessStartInfo();
        psi.ArgumentList.Add("--dump-json");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--no-warnings");
        psi.ArgumentList.Add("--skip-download");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(url);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start yt-dlp.exe.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(BuildFriendlyError(stderr));

        var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? throw new InvalidOperationException("yt-dlp returned no data for this URL.");

        return JsonSerializer.Deserialize<MediaInfo>(firstLine, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse yt-dlp's output.");
    }

    /// <summary>
    /// Cheap probe (no per-video metadata) listing a playlist's entries. A URL that resolves to a
    /// single video returns exactly one entry, so callers can use this to decide whether to show
    /// playlist-selection UI at all.
    /// </summary>
    public async Task<IReadOnlyList<PlaylistEntry>> GetPlaylistEntriesAsync(string url, CancellationToken ct = default)
    {
        var psi = BuildProcessStartInfo();
        psi.ArgumentList.Add("--flat-playlist");
        psi.ArgumentList.Add("--dump-json");
        psi.ArgumentList.Add("--no-warnings");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(url);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start yt-dlp.exe.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(BuildFriendlyError(stderr));

        var entries = new List<PlaylistEntry>();
        var index = 1;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(line);
            var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
            entries.Add(new PlaylistEntry { Index = index, Title = title ?? $"Item {index}" });
            index++;
        }

        return entries;
    }

    /// <summary>Runs the download and returns the final file's absolute path once yt-dlp reports it
    /// (after any merge/extraction/move step), or null if that line was never seen.</summary>
    public async Task<string?> DownloadAsync(
        string url,
        VideoQuality quality,
        string outputDirectory,
        IProgress<YtDlpProgress>? progress,
        YtDlpDownloadOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new YtDlpDownloadOptions();
        Directory.CreateDirectory(outputDirectory);

        var psi = BuildProcessStartInfo();
        if (options.PlaylistItemIndex is { } playlistIndex)
        {
            psi.ArgumentList.Add("--playlist-items");
            psi.ArgumentList.Add(playlistIndex.ToString());
        }
        else
        {
            psi.ArgumentList.Add("--no-playlist");
        }
        psi.ArgumentList.Add("--windows-filenames");
        psi.ArgumentList.Add("--newline");
        psi.ArgumentList.Add("--retries");
        psi.ArgumentList.Add("10");
        psi.ArgumentList.Add("--fragment-retries");
        psi.ArgumentList.Add("10");
        // Fetch multiple DASH/HLS fragments in parallel instead of yt-dlp's serial default (1) -
        // this is the yt-dlp equivalent of aria2's multi-connection splitting. 4 is a reasonable
        // default: meaningfully faster without being aggressive enough to trip site-side throttling.
        psi.ArgumentList.Add("--concurrent-fragments");
        psi.ArgumentList.Add("4");
        psi.ArgumentList.Add("--progress-template");
        psi.ArgumentList.Add($"download:{ProgressMarker}%(progress)j");
        // Prints the final file's absolute path (after merge/extraction/move) as its own plain
        // line, so "Open Folder" can point at the real output file instead of a mid-download
        // fragment name or just the destination directory.
        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("after_move:filepath");
        psi.ArgumentList.Add("--ffmpeg-location");
        psi.ArgumentList.Add(_ffmpegDirectory);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(Path.Combine(outputDirectory, "%(title)s [%(id)s].%(ext)s"));

        if (options.DownloadSubtitles)
        {
            psi.ArgumentList.Add("--write-subs");
            psi.ArgumentList.Add("--write-auto-subs");
            psi.ArgumentList.Add("--sub-langs");
            psi.ArgumentList.Add("en.*,ar.*");
            psi.ArgumentList.Add("--embed-subs");
        }

        if (options.EmbedThumbnail)
            psi.ArgumentList.Add("--embed-thumbnail");

        if (quality == VideoQuality.AudioOnlyMp3)
        {
            psi.ArgumentList.Add("-x");
            psi.ArgumentList.Add("--audio-format");
            psi.ArgumentList.Add("mp3");
        }
        else
        {
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(FormatSelector(quality));
            psi.ArgumentList.Add("--merge-output-format");
            psi.ArgumentList.Add("mp4");
        }

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(url);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start yt-dlp.exe.");

        var stderrBuilder = new StringBuilder();
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) is not null)
                stderrBuilder.AppendLine(line);
        });

        using var registration = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best effort */ }
        });

        string? outLine;
        string? finalPath = null;
        while ((outLine = await process.StandardOutput.ReadLineAsync()) is not null)
        {
            if (!outLine.StartsWith(ProgressMarker, StringComparison.Ordinal))
            {
                // the only other lines yt-dlp prints to stdout here are our --print after_move:filepath ones
                if (!string.IsNullOrWhiteSpace(outLine)) finalPath = outLine.Trim();
                continue;
            }

            var json = outLine[ProgressMarker.Length..];
            try
            {
                var update = JsonSerializer.Deserialize<YtDlpProgress>(json, JsonOptions);
                if (update is not null) progress?.Report(update);
            }
            catch (JsonException)
            {
                // a malformed/partial progress line is not fatal, just skip it
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await stderrTask;

        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(BuildFriendlyError(stderrBuilder.ToString()));

        return finalPath;
    }

    private ProcessStartInfo BuildProcessStartInfo() => new()
    {
        FileName = _ytDlpPath,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };

    private static string BuildFriendlyError(string stderr)
    {
        var lastErrorLine = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase));

        return lastErrorLine?.Trim() ?? (string.IsNullOrWhiteSpace(stderr) ? "yt-dlp failed for an unknown reason." : stderr.Trim());
    }

    private static string FormatSelector(VideoQuality quality) => quality switch
    {
        VideoQuality.Best => "bestvideo*+bestaudio/best",
        VideoQuality.Quality1080p => "bestvideo[height<=1080]+bestaudio/best[height<=1080]",
        VideoQuality.Quality720p => "bestvideo[height<=720]+bestaudio/best[height<=720]",
        VideoQuality.Quality480p => "bestvideo[height<=480]+bestaudio/best[height<=480]",
        _ => "bestvideo*+bestaudio/best"
    };
}
