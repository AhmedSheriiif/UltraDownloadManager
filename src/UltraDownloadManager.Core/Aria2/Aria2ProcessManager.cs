using System.Diagnostics;

namespace UltraDownloadManager.Core.Aria2;

/// <summary>
/// Owns the lifecycle of the bundled aria2c.exe process: starts it with RPC enabled,
/// waits for the RPC endpoint to come alive, and shuts it down gracefully on dispose.
/// </summary>
public sealed class Aria2ProcessManager : IAsyncDisposable
{
    private Process? _process;

    public int RpcPort { get; }
    public string RpcSecret { get; }
    public string RpcUrl => $"http://127.0.0.1:{RpcPort}/jsonrpc";

    public Aria2ProcessManager(int rpcPort, string? rpcSecret = null)
    {
        RpcPort = rpcPort;
        RpcSecret = rpcSecret ?? Guid.NewGuid().ToString("N");
    }

    public async Task StartAsync(
        string aria2cPath,
        string downloadDirectory,
        string sessionFilePath,
        string logFilePath,
        int maxConcurrentDownloads,
        int maxConnectionsPerServer,
        CancellationToken ct = default)
    {
        if (_process is { HasExited: false }) return;

        if (!File.Exists(aria2cPath))
            throw new FileNotFoundException("Bundled aria2c.exe was not found.", aria2cPath);

        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(sessionFilePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        if (!File.Exists(sessionFilePath)) File.WriteAllText(sessionFilePath, string.Empty);

        var psi = new ProcessStartInfo
        {
            FileName = aria2cPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(aria2cPath),
        };

        psi.ArgumentList.Add("--enable-rpc=true");
        psi.ArgumentList.Add($"--rpc-listen-port={RpcPort}");
        psi.ArgumentList.Add($"--rpc-secret={RpcSecret}");
        psi.ArgumentList.Add("--rpc-listen-all=false");
        psi.ArgumentList.Add("--continue=true");
        psi.ArgumentList.Add($"--dir={downloadDirectory}");
        psi.ArgumentList.Add($"--max-concurrent-downloads={maxConcurrentDownloads}");
        psi.ArgumentList.Add($"--max-connection-per-server={maxConnectionsPerServer}");
        psi.ArgumentList.Add("--split=16");
        psi.ArgumentList.Add("--min-split-size=1M");
        psi.ArgumentList.Add($"--save-session={sessionFilePath}");
        psi.ArgumentList.Add("--save-session-interval=30");
        psi.ArgumentList.Add($"--input-file={sessionFilePath}");
        psi.ArgumentList.Add("--file-allocation=none");
        psi.ArgumentList.Add("--allow-overwrite=true");
        psi.ArgumentList.Add("--auto-file-renaming=true");
        // Many networks advertise IPv6 that doesn't actually route; falling back silently like
        // browsers do would need Happy Eyeballs, which aria2 doesn't implement, so disable it.
        psi.ArgumentList.Add("--disable-ipv6=true");
        psi.ArgumentList.Add($"--log={logFilePath}");
        psi.ArgumentList.Add("--log-level=warn");
        psi.ArgumentList.Add("--quiet=true");

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start aria2c.exe.");

        await WaitForRpcReadyAsync(ct);
    }

    private async Task WaitForRpcReadyAsync(CancellationToken ct)
    {
        using var client = new Aria2Client(RpcUrl, RpcSecret);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        Exception? last = null;

        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
                throw new InvalidOperationException($"aria2c exited immediately with code {_process.ExitCode}. Check the aria2 log file.");

            try
            {
                await client.GetVersionAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(200, ct);
            }
        }

        throw new InvalidOperationException(
            $"aria2c RPC endpoint did not become ready in time. Last error: {last}", last);
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            using var client = new Aria2Client(RpcUrl, RpcSecret);
            await client.ShutdownAsync();
            if (!_process.WaitForExit(3000))
                _process.Kill(true);
        }
        catch
        {
            try { _process.Kill(true); } catch { /* best effort shutdown */ }
        }
    }
}
