using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// stdout is exclusively JSON-RPC. Never print diagnostics, URLs or authentication values there.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Files = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> KnownTrackIds = new();
    private static readonly TcpListener Listener = new(IPAddress.Loopback, 0);
    private static readonly string Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static JsonElement _configuration;
    private static string _catalogUrl = "", _streamTemplate = "", _authorization = "", _fixtureFolder = "";
    private static int _port;
    private static CancellationTokenSource? _sessionCancellation;
    private static async Task Main()
    {
        Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
        Listener.Start(); _port = ((IPEndPoint)Listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        var server = ServeAsync(cancellation.Token);
        try
        {
            while (await Console.In.ReadLineAsync() is { } line)
            {
                JsonElement id = default;
                try
                {
                    if (line.Length > 1_000_000) throw new InvalidDataException();
                    using var document = JsonDocument.Parse(line); var request = document.RootElement;
                    id = request.GetProperty("id").Clone();
                    if (request.GetProperty("jsonrpc").GetString() != "2.0") throw new InvalidDataException();
                    var parameters = request.GetProperty("params"); var method = request.GetProperty("method").GetString();
                    object result = method switch
                    {
                        "initialize" => Initialize(parameters),
                        "catalog.list" => await CatalogAsync(parameters),
                        "playback.resolve" => Resolve(parameters.GetProperty("trackId").GetString()!),
                        "lifecycle.disable" or "lifecycle.uninstall" or "lifecycle.shutdown" => Cleanup(method),
                        _ => throw new InvalidOperationException("Unknown method")
                    };
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, Json));
                }
                catch { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = -32000, message = "Provider operation failed. Check configuration or server." } }, Json)); }
            }
        }
        finally { cancellation.Cancel(); Listener.Stop(); Http.Dispose(); try { await server; } catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { } }
    }
    /// <summary>清理钩子必须幂等。uninstall 可在尚未 initialize 的新进程中被调用。</summary>
    private static object Cleanup(string method)
    {
        _sessionCancellation?.Cancel(); Listener.Stop(); Http.CancelPendingRequests(); Files.Clear(); KnownTrackIds.Clear();
        // 示例仅记录生命周期名称，不记录凭据、歌曲 URL 或用户输入。
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "lifecycle.events.jsonl"), JsonSerializer.Serialize(new { method, utc = DateTimeOffset.UtcNow }, Json) + Environment.NewLine);
        return new { released = true };
    }
    private static object Initialize(JsonElement parameters)
    {
        using var document = JsonDocument.Parse(parameters.GetProperty("configuration").GetString() ?? "{}"); _configuration = document.RootElement.Clone();
        string Config(string key) => _configuration.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
        _fixtureFolder = Config("fixtureFolder"); _catalogUrl = Config("catalogUrl"); _streamTemplate = Config("streamUrlTemplate"); _authorization = Config("authorization");
        if (_fixtureFolder.Length == 0)
        {
            ValidateServerUrl(_catalogUrl); ValidateServerUrl(_streamTemplate.Replace("{trackId}", "test"));
            if (!_streamTemplate.Contains("{trackId}")) throw new InvalidDataException("streamUrlTemplate requires {trackId}");
        }
        return new { contractVersion = 1, capabilities = new[] { "catalog", "loopback-http" } };
    }
    private static void ValidateServerUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0) throw new InvalidDataException("HTTP(S) URL required");
        if (uri.Scheme == "http" && !(IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip))) throw new InvalidDataException("Use HTTPS for a remote server");
    }
    private static async Task<object> CatalogAsync(JsonElement parameters)
    {
        var cursor = parameters.TryGetProperty("cursor", out var value) ? value.GetString() ?? "" : "";
        if (_fixtureFolder.Length > 0)
        {
            Files.Clear(); KnownTrackIds.Clear();
            var extensions = new HashSet<string>([".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus"], StringComparer.OrdinalIgnoreCase);
            var files = Directory.EnumerateFiles(_fixtureFolder).Where(f => extensions.Contains(Path.GetExtension(f))).Take(1000);
            var tracks = files.Select(file =>
            {
                var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file)))).Substring(0, 24);
                Files[id] = Path.GetFullPath(file); KnownTrackIds[id] = true;
                return new { id, title = Path.GetFileNameWithoutExtension(file), artist = "示例音源", album = "Contract 测试", durationSeconds = 0, format = Path.GetExtension(file).TrimStart('.') };
            }).ToArray();
            return new { tracks };
        }
        var url = _catalogUrl + (cursor.Length > 0 ? (_catalogUrl.Contains('?') ? "&" : "?") + "cursor=" + Uri.EscapeDataString(cursor) : "");
        using var request = ServerRequest(HttpMethod.Get, url); using var response = await Http.SendAsync(request); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 8_000_000) throw new InvalidDataException("Catalog page too large");
        var bytes = await response.Content.ReadAsByteArrayAsync(); if (bytes.Length > 8_000_000) throw new InvalidDataException();
        using var document = JsonDocument.Parse(bytes); var catalog = document.RootElement.Clone();
        var entries = catalog.GetProperty("tracks"); if (entries.GetArrayLength() > 1000) throw new InvalidDataException("Maximum page size 1000");
        foreach (var track in entries.EnumerateArray()) KnownTrackIds[track.GetProperty("id").GetString()!] = true;
        // This is the adapter boundary: change this mapping for your own server's response shape.
        return catalog;
    }
    private static object Resolve(string id)
    {
        if (!KnownTrackIds.ContainsKey(id)) throw new InvalidDataException("Unknown track");
        return new { kind = "loopback-http", url = $"http://127.0.0.1:{_port}/audio?token={Token}&track={Uri.EscapeDataString(id)}" };
    }
    private static HttpRequestMessage ServerRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        if (_authorization.Length > 0) request.Headers.TryAddWithoutValidation("Authorization", _authorization);
        return request;
    }
    private static async Task ServeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await Listener.AcceptTcpClientAsync(token); }
            catch (Exception e) when (e is OperationCanceledException or SocketException) { break; }
            _ = HandleAsync(client, token);
        }
    }
    private static async Task HandleAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var start = await reader.ReadLineAsync(timeout.Token); if (start is null || start.Length > 8192) return;
                var parts = start.Split(' '); if (parts.Length != 3 || parts[0] is not ("GET" or "HEAD")) { await SendStatus(stream, 405, timeout.Token); return; }
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var length = start.Length;
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } header)
                {
                    length += header.Length; if (length > 16384) return;
                    var index = header.IndexOf(':'); if (index > 0) headers[header[..index]] = header[(index + 1)..].Trim();
                }
                var uri = new Uri("http://127.0.0.1" + parts[1]);
                var query = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
                if (uri.AbsolutePath != "/audio" || query.GetValueOrDefault("token") != Token) { await SendStatus(stream, 403, timeout.Token); return; }
                var id = query.GetValueOrDefault("track", ""); if (!KnownTrackIds.ContainsKey(id)) { await SendStatus(stream, 404, timeout.Token); return; }
                if (Files.TryGetValue(id, out var file))
                {
                    await ServeFile(stream, parts[0], file, headers.GetValueOrDefault("Range"), timeout.Token); return;
                }
                var url = _streamTemplate.Replace("{trackId}", Uri.EscapeDataString(id));
                using var request = ServerRequest(new HttpMethod(parts[0]), url);
                if (headers.TryGetValue("Range", out var range)) request.Headers.Range = RangeHeaderValue.Parse(range);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable)) { await SendStatus(stream, 502, timeout.Token); return; }
                if (response.Content.Headers.ContentLength is not { } bodyLength) { await SendStatus(stream, 502, timeout.Token); return; }
                var contentRange = response.Content.Headers.ContentRange?.ToString();
                await Headers(stream, (int)response.StatusCode, bodyLength, contentRange, timeout.Token);
                if (parts[0] != "HEAD" && response.IsSuccessStatusCode) { await using var body = await response.Content.ReadAsStreamAsync(timeout.Token); await body.CopyToAsync(stream, timeout.Token); }
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or HttpRequestException or FormatException or ArgumentException) { }
        }
    }
    private static async Task ServeFile(NetworkStream output, string method, string path, string? range, CancellationToken token)
    {
        await using var input = File.OpenRead(path); var size = input.Length; long start = 0, end = size - 1; var status = 200;
        if (range is not null)
        {
            var parsed = RangeHeaderValue.Parse(range);
            if (parsed.Unit != "bytes" || parsed.Ranges.Count != 1) { await Headers(output, 416, 0, $"bytes */{size}", token); return; }
            var item = parsed.Ranges.Single();
            start = item.From ?? Math.Max(0, size - (item.To ?? 0)); end = item.From is null ? size - 1 : Math.Min(size - 1, item.To ?? size - 1);
            if (start < 0 || start >= size || end < start) { await Headers(output, 416, 0, $"bytes */{size}", token); return; }
            status = 206;
        }
        var count = end - start + 1; await Headers(output, status, count, status == 206 ? $"bytes {start}-{end}/{size}" : null, token);
        if (method == "HEAD") return;
        input.Position = start; var buffer = new byte[65536];
        while (count > 0) { var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), token); if (read == 0) break; await output.WriteAsync(buffer.AsMemory(0, read), token); count -= read; }
    }
    private static Task SendStatus(NetworkStream stream, int status, CancellationToken token) => Headers(stream, status, 0, null, token);
    private static async Task Headers(NetworkStream stream, int status, long size, string? contentRange, CancellationToken token)
    {
        var reason = status switch { 200 => "OK", 206 => "Partial Content", 416 => "Range Not Satisfiable", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Bad Gateway" };
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/octet-stream\r\nContent-Length: {size}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n" + (contentRange is null ? "" : "Content-Range: " + contentRange + "\r\n") + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header.Replace("\\r", "\r").Replace("\\n", "\n")), token);
    }
}
