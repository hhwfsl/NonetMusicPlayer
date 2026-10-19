using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using NonetMusicPlayer.Core.Streaming;
using NonetMusicPlayer.Core.Diagnostics;

namespace NonetMusicPlayer.Core.Network;

/// <summary>只信任邀请中指纹的 HTTPS 客户端；凭据仅驻留内存，不跟随重定向。</summary>
public sealed class LanLibraryClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClientHandler _handler;
    private readonly CancellationTokenSource _lifetime = new();
    private static readonly ConcurrentDictionary<string, LanLibraryClient> Active = new(StringComparer.Ordinal);
    private long _lastContact = Environment.TickCount64;
    /// <summary>只由同进程服务端在校验会话及来源后调用；双向通信任一方向正常都证明设备仍在线。</summary>
    internal static void ObserveAuthenticatedPeer(string deviceId)
    { if (Active.TryGetValue(deviceId, out var peer) && peer.Connected) Interlocked.Exchange(ref peer._lastContact, Environment.TickCount64); }
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
            // 每页独立限制响应头与正文的时间；大曲库不共用心跳期限，也不允许正文无限挂起。
            using var pageTimeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); pageTimeout.CancelAfter(20_000);
            using var response = await _http.GetAsync($"v1/catalog?offset={tracks.Count}", HttpCompletionOption.ResponseHeadersRead, pageTimeout.Token); response.EnsureSuccessStatusCode();
            var page = JsonSerializer.Deserialize(await ReadLimited(response, 4 * 1024 * 1024, pageTimeout.Token), LanJson.Default.LanCatalog) ?? throw new InvalidDataException();
            first ??= page;
            if (page.DeviceId != DeviceId || page.Revision != first.Revision || page.Total is < 0 or > 20000 || page.Tracks.Length > 200 || page.Playlists.Length > 2000) throw new InvalidDataException("Peer catalog changed or exceeded its limit.");
            if (page.Tracks.Length == 0 && tracks.Count < page.Total) throw new InvalidDataException();
            Interlocked.Exchange(ref _lastContact, Environment.TickCount64);
            tracks.AddRange(page.Tracks);
        } while (tracks.Count < first.Total);
        if (tracks.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != tracks.Count || tracks.Any(t => t.Id.Length is 0 or > 256 || t.Title.Length > 2000)) throw new InvalidDataException();
        Catalog = first with { Tracks = tracks.ToArray() }; Changed?.Invoke(this, EventArgs.Empty);
    }
    private async Task WatchAsync()
    {
        var failures = 0;
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(failures == 0 ? 5000 : 2000, _lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(4000);
                using var response = await _http.GetAsync("v1/info", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                { AppLog.Info("LAN", "Paired session was revoked by peer"); Dispose(); return; }
                response.EnsureSuccessStatusCode();
                var info = JsonSerializer.Deserialize(await ReadLimited(response, 8192, timeout.Token), LanJson.Default.LanInfo);
                if (info?.DeviceId != DeviceId || info.Protocol != 1) { Dispose(); return; }
                Interlocked.Exchange(ref _lastContact, Environment.TickCount64); failures = 0;
                // 目录分页不共用心跳的四秒期限，避免较大曲库在后台刷新时被误断开。
                if (info.Revision != Catalog.Revision) await RefreshAsync(_lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception e) when (e is HttpRequestException or IOException or JsonException or OperationCanceledException or ObjectDisposedException)
            {
                if (_lifetime.IsCancellationRequested) return;
                if (++failures == 1) AppLog.Warning("LAN", "Peer probe delayed; retaining the paired session and retrying");
                // 不以一次单向探测超时覆盖反向已认证活动；确实双向失联才结束会话。
                if (failures >= 3 && Environment.TickCount64 - Interlocked.Read(ref _lastContact) >= 30_000)
                { AppLog.Warning("LAN", "Peer connection ended after sustained bidirectional inactivity"); Dispose(); return; }
            }
        }
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
