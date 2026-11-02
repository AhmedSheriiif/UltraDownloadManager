using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using UltraDownloadManager.App.ViewModels;
using UltraDownloadManager.App.Views;
using UltraDownloadManager.Core.YtDlp;

namespace UltraDownloadManager.App.Util;

/// <summary>
/// Local, current-user-only named pipe the native messaging host uses to hand yt-dlp-recognized
/// URLs to the running app. aria2-backed downloads go straight through its RPC port, but yt-dlp
/// downloads are supervised in-process by <see cref="MainViewModel"/>, so there's no RPC surface
/// for an outside process to add one to - this pipe is that missing surface.
/// </summary>
public sealed class CommandPipeServer : IDisposable
{
    public const string PipeName = "UltraDownloadManager.Commands";

    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _cts = new();

    public CommandPipeServer(MainViewModel viewModel) => _viewModel = viewModel;

    public void Start() => _ = RunAsync(_cts.Token);

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server, leaveOpen: true);
                using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (line is not null)
                    await writer.WriteLineAsync(await HandleAsync(line));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // a single bad/dropped connection shouldn't take down the server loop
            }
        }
    }

    private async Task<string> HandleAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var actionEl) ? actionEl.GetString() : null;

            if (action == "addVideo")
            {
                var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? url : url;
                var qualityName = root.TryGetProperty("quality", out var q) ? q.GetString() : null;
                var quality = Enum.TryParse<VideoQuality>(qualityName, out var parsed) ? parsed : VideoQuality.Best;

                var info = new MediaInfo { Title = title, WebpageUrl = url };

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    _viewModel.StartYtDlpDownload(info, quality, _viewModel.Settings.DownloadDirectory));

                return "{\"success\":true}";
            }

            if (action == "addDownload")
            {
                var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                    return "{\"success\":false,\"message\":\"Missing url.\"}";

                var referrer = root.TryGetProperty("referrer", out var r) ? r.GetString() : null;
                var cookie = root.TryGetProperty("cookie", out var c) ? c.GetString() : null;

                var accepted = false;
                var chosenDirectory = _viewModel.Settings.DownloadDirectory;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new ConfirmDownloadWindow(url!, _viewModel.Settings.DownloadDirectory);
                    accepted = dialog.ShowDialog() == true;
                    if (accepted) chosenDirectory = dialog.ChosenDirectory;
                });

                if (!accepted)
                    return "{\"success\":false,\"message\":\"Cancelled by user.\"}";

                await _viewModel.AddBrowserDownloadAsync(url!, referrer, cookie, chosenDirectory);
                return "{\"success\":true}";
            }

            return "{\"success\":false,\"message\":\"Unknown action.\"}";
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { success = false, message = ex.Message });
        }
    }

    public void Dispose() => _cts.Cancel();
}
