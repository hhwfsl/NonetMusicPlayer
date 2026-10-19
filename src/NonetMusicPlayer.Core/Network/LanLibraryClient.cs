using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using NonetMusicPlayer.Core.Streaming;

namespace NonetMusicPlayer.Core.Network;

/// <summary>只信任邀请中指纹的 HTTPS 客户端；凭据仅驻留内存，不跟随重定向。</summary>
public sealed class LanLibraryClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClientHandler _handler;
    private readonly CancellationTokenSource _lifetime = new();
    private static readonly ConcurrentDictionary<string, LanLibraryClient> Active = new(StringComparer.Ordinal);
    public string DeviceId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public LanCatalog Catalog { get; private set; } = new("", "", "", [], [], 0);
    public event EventHandler? Changed;
    public event EventHandler? Disconnected;
    public bool Connected => !_lifetime.IsCancellationRequested;
    public bool DuplexSupported { get; private set; }
    private LanLibraryClient(Uri origin, string fingerprint)
    {
        var handler = _handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        handler.ServerCertificateCustomValidationCallback = (message, cert, _, _) => message.RequestUri?.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority) && cert is not null && Convert.ToHexStringLower(SHA256.HashData(cert.RawData)) == fingerprint;
        _http = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(20) };
    }
    public static async Task<LanLibraryClient> ConnectAsync(string invitation, string ownId, string ownName, CancellationToken token = default)
        => await ConnectDuplexAsync(invitation, ownId, ownName, false, token);
    public static async Task<LanLibraryClient> ConnectDuplexAsync(string invitation, string ownId, string ownName, bool duplex, CancellationToken token = default)
    {
        var (origin, fingerprint, key) = LanAddress.ParseInvitation(invitation); var client = new LanLibraryClient(origin, fingerprint);
        try
        {
            // 已知长度请求避免 Chunked；服务端只接受有界 Content-Length，防止无限请求体。
            using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new LanPairRequest(key, ownId, ownName) { Duplex = duplex }, LanJson.Default.LanPairRequest));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            // 配对的人工确认超时与日常媒体请求分离；HttpClient 启动请求后不能修改 Timeout。
            using var pairing = new HttpClient(client._handler, false) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(135) };
            using var response = await pairing.PostAsync("v1/pair", content, token); response.EnsureSuccessStatusCode();
            var pair = JsonSerializer.Deserialize(await ReadLimited(response, 4096, token), LanJson.Default.LanPairResult) ?? throw new InvalidDataException();
            if (pair.Protocol != 1 || !LanAddress.IsHex(pair.DeviceId, 32) || !LanAddress.IsHex(pair.Token, 64) || pair.DeviceId == ownId) throw new InvalidDataException("Invalid peer identity.");
            client.DeviceId = pair.DeviceId; client.Name = pair.Name[..Math.Min(pair.Name.Length, 100)]; client._http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.Token);
            client.DuplexSupported = pair.Duplex;
            await client.RefreshAsync(token);
            if (Active.TryGetValue(pair.DeviceId, out var old)) old.Dispose(); Active[pair.DeviceId] = client;
            _ = client.WatchAsync(); return client;
        }
        catch { client.Dispose(); throw; }
    }
    public static async Task<LanLibraryClient> FromApprovedAccessAsync(LanReverseAccess access, CancellationToken token = default)
    {
        if (!IPAddress.TryParse(access.Origin.Host, out var ip) || !LanAddress.IsPrivate(ip) || access.Origin.Scheme != "https" || !LanAddress.IsHex(access.DeviceId, 32) || !LanAddress.IsHex(access.Fingerprint, 64) || !LanAddress.IsHex(access.Token, 64)) throw new InvalidDataException("Invalid approved access");
        var client = new LanLibraryClient(access.Origin, access.Fingerprint) { DeviceId = access.DeviceId, Name = access.Name, DuplexSupported = true };
        client._http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
        try { await client.RefreshAsync(token); if (Active.TryGetValue(client.DeviceId, out var old)) old.Dispose(); Active[client.DeviceId] = client; _ = client.WatchAsync(); return client; }
        catch { client.Dispose(); throw; }
    }
    public async Task EstablishReverseAsync(LanDuplexGrant grant, CancellationToken token = default)
    {
        if (!DuplexSupported) throw new InvalidDataException("Peer does not support bidirectional pairing");
        using var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(grant, LanJson.Default.LanDuplexGrant)); content.Headers.ContentType = new("application/json");
        using var response = await _http.PostAsync("v1/duplex", content, token); response.EnsureSuccessStatusCode();
    }
    public async Task NotifyDisconnectAsync()
    {
        if (!Connected) return;
        try { using var stop = new CancellationTokenSource(1000); using var response = await _http.PostAsync("v1/disconnect", new ByteArrayContent([]), stop.Token); }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    public async Task RefreshAsync(CancellationToken token = default)
    {
        var tracks = new List<LanSong>(); LanCatalog? first = null;
        do
        {
            using var response = await _http.GetAsync($"v1/catalog?offset={tracks.Count}", HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
            var page = JsonSerializer.Deserialize(await ReadLimited(response, 4 * 1024 * 1024, token), LanJson.Default.LanCatalog) ?? throw new InvalidDataException();
            first ??= page;
            if (page.DeviceId != DeviceId || page.Revision != first.Revision || page.Total is < 0 or > 20000 || page.Tracks.Length > 200 || page.Playlists.Length > 2000) throw new InvalidDataException("Peer catalog changed or exceeded its limit.");
            if (page.Tracks.Length == 0 && tracks.Count < page.Total) throw new InvalidDataException();
            tracks.AddRange(page.Tracks);
        } while (tracks.Count < first.Total);
        if (tracks.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != tracks.Count || tracks.Any(t => t.Id.Length is 0 or > 256 || t.Title.Length > 2000)) throw new InvalidDataException();
        Catalog = first with { Tracks = tracks.ToArray() }; Changed?.Invoke(this, EventArgs.Empty);
    }
    private async Task WatchAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(5000, _lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(4000);
                using var response = await _http.GetAsync("v1/info", HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
                var info = JsonSerializer.Deserialize(await ReadLimited(response, 8192, timeout.Token), LanJson.Default.LanInfo);
                if (info?.DeviceId != DeviceId || info.Protocol != 1) throw new InvalidDataException();
                if (info.Revision != Catalog.Revision) await RefreshAsync(timeout.Token);
            }
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or OperationCanceledException or ObjectDisposedException) { Dispose(); }
    }
    public string TrackUri(string id) => $"nonet-lan://{DeviceId}/{Uri.EscapeDataString(id)}";
    public static Stream OpenAudio(Uri uri)
    {
        if (uri.Scheme != "nonet-lan" || !Active.TryGetValue(uri.Host, out var peer) || !peer.Connected) throw new IOException("LAN device is disconnected.");
        var id = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')); if (!peer.Catalog.Tracks.Any(t => t.Id == id)) throw new IOException("LAN track is unavailable.");
        return new LoopbackRangeStream(new Uri(peer._http.BaseAddress!, "v1/audio/" + Uri.EscapeDataString(id)), peer._http);
    }
    public async Task<byte[]> ReadAssetAsync(string kind, string id, CancellationToken token = default)
    {
        if (kind is not ("artwork" or "lyrics") || !Catalog.Tracks.Any(t => t.Id == id)) throw new InvalidDataException();
        using var response = await _http.GetAsync($"v1/{kind}/{Uri.EscapeDataString(id)}", HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        return await ReadLimited(response, kind == "lyrics" ? 2 * 1024 * 1024 : 5 * 1024 * 1024, token);
    }
    private static async Task<byte[]> ReadLimited(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException();
        await using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); var buffer = new byte[16384];
        int count; while ((count = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + count > limit) throw new InvalidDataException(); output.Write(buffer, 0, count); } return output.ToArray();
    }
    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested) return; _lifetime.Cancel();
        Active.TryRemove(new KeyValuePair<string, LanLibraryClient>(DeviceId, this)); _http.Dispose(); Disconnected?.Invoke(this, EventArgs.Empty);
    }
}
