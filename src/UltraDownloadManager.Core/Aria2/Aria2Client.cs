using System.Text.Json;
using System.Text.Json.Serialization;
using UltraDownloadManager.Core.Models;

namespace UltraDownloadManager.Core.Aria2;

/// <summary>
/// Thin JSON-RPC 2.0 client for aria2's RPC interface (https://aria2.github.io/manual/en/html/aria2c.html#rpc-interface).
/// One instance talks to one aria2c process over HTTP.
/// </summary>
public sealed class Aria2Client : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _rpcUri;
    private readonly string _secret;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public Aria2Client(string rpcUrl, string secret)
    {
        _secret = secret;
        _rpcUri = new Uri(rpcUrl);
        _http = new HttpClient();
    }

    private string Token => $"token:{_secret}";

    private async Task<JsonElement> CallAsync(string method, params object?[] parameters)
    {
        var callParams = new List<object?> { Token };
        callParams.AddRange(parameters.Where(p => p is not null));

        var rpcRequest = new JsonRpcRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Method = method,
            Params = callParams
        };

        var json = JsonSerializer.Serialize(rpcRequest, _jsonOptions);

        // aria2's built-in HTTP server doesn't handle the "Expect: 100-continue" handshake
        // HttpClient sends by default, which makes it return a bare 500 - so send without it.
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _rpcUri)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.ExpectContinue = false;

        using var response = await _http.SendAsync(httpRequest);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"aria2 RPC returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var envelope = await JsonSerializer.DeserializeAsync<JsonRpcResponse>(stream, _jsonOptions)
            ?? throw new Aria2RpcException(-1, "Empty response from aria2 RPC.");

        if (envelope.Error is { } error)
            throw new Aria2RpcException(error.Code, error.Message);

        return envelope.Result;
    }

    public async Task<string> AddUriAsync(IEnumerable<string> uris, IDictionary<string, string>? options = null)
    {
        var result = await CallAsync("aria2.addUri", uris.ToArray(), options ?? new Dictionary<string, string>());
        return result.GetString() ?? string.Empty;
    }

    public async Task<string> AddTorrentAsync(string base64Torrent, IDictionary<string, string>? options = null)
    {
        var result = await CallAsync("aria2.addTorrent", base64Torrent, Array.Empty<string>(), options ?? new Dictionary<string, string>());
        return result.GetString() ?? string.Empty;
    }

    public Task PauseAsync(string gid, bool force = false) => CallAsync(force ? "aria2.forcePause" : "aria2.pause", gid);

    public Task PauseAllAsync(bool force = false) => CallAsync(force ? "aria2.forcePauseAll" : "aria2.pauseAll");

    public Task UnpauseAsync(string gid) => CallAsync("aria2.unpause", gid);

    public Task UnpauseAllAsync() => CallAsync("aria2.unpauseAll");

    public Task RemoveAsync(string gid, bool force = false) => CallAsync(force ? "aria2.forceRemove" : "aria2.remove", gid);

    /// <summary>how: "POS_SET" (absolute from front), "POS_CUR" (relative), or "POS_END" (relative to end).</summary>
    public Task ChangePositionAsync(string gid, int pos, string how) => CallAsync("aria2.changePosition", gid, pos, how);

    public async Task<DownloadItem> TellStatusAsync(string gid, string[]? keys = null)
    {
        var result = await CallAsync("aria2.tellStatus", gid, (object?)keys);
        return result.Deserialize<DownloadItem>(_jsonOptions)!;
    }

    public async Task<List<DownloadItem>> TellActiveAsync(string[]? keys = null)
    {
        var result = await CallAsync("aria2.tellActive", (object?)keys);
        return result.Deserialize<List<DownloadItem>>(_jsonOptions) ?? new();
    }

    public async Task<List<DownloadItem>> TellWaitingAsync(int offset, int num, string[]? keys = null)
    {
        var result = await CallAsync("aria2.tellWaiting", offset, num, (object?)keys);
        return result.Deserialize<List<DownloadItem>>(_jsonOptions) ?? new();
    }

    public async Task<List<DownloadItem>> TellStoppedAsync(int offset, int num, string[]? keys = null)
    {
        var result = await CallAsync("aria2.tellStopped", offset, num, (object?)keys);
        return result.Deserialize<List<DownloadItem>>(_jsonOptions) ?? new();
    }

    public async Task<GlobalStat> GetGlobalStatAsync()
    {
        var result = await CallAsync("aria2.getGlobalStat");
        return result.Deserialize<GlobalStat>(_jsonOptions) ?? new();
    }

    public Task ChangeGlobalOptionAsync(IDictionary<string, string> options) => CallAsync("aria2.changeGlobalOption", options);

    public Task ChangeOptionAsync(string gid, IDictionary<string, string> options) => CallAsync("aria2.changeOption", gid, options);

    public Task PurgeDownloadResultAsync() => CallAsync("aria2.purgeDownloadResult");

    public Task RemoveDownloadResultAsync(string gid) => CallAsync("aria2.removeDownloadResult", gid);

    public async Task<string> GetVersionAsync()
    {
        var result = await CallAsync("aria2.getVersion");
        return result.GetProperty("version").GetString() ?? "unknown";
    }

    public Task ShutdownAsync(bool force = false) => CallAsync(force ? "aria2.forceShutdown" : "aria2.shutdown");

    public void Dispose() => _http.Dispose();

    private sealed class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; init; } = "2.0";

        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("method")]
        public string Method { get; init; } = string.Empty;

        [JsonPropertyName("params")]
        public List<object?> Params { get; init; } = new();
    }

    private sealed class JsonRpcResponse
    {
        [JsonPropertyName("result")]
        public JsonElement Result { get; init; }

        [JsonPropertyName("error")]
        public JsonRpcError? Error { get; init; }
    }

    private sealed class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; init; }

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }
}
