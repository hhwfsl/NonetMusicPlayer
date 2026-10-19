using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace NonetMusicPlayer.Core.Network;

/// <summary>只读 HTTPS 曲库：固定端点、有界请求、认证与 Range，不提供任意文件路径。</summary>
public sealed class LanLibraryServer : IDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly string _deviceId, _name;
    private readonly Func<LanCatalog> _catalog;
    private readonly Func<string, string, Task<LanFile?>> _open;
    private readonly Func<LanConnectionRequest, CancellationToken, Task<bool>>? _approve;
    private readonly SemaphoreSlim _pairSlots = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _slots = new(12);
    private sealed record Session(string DeviceId, string Name, string Address, DateTimeOffset Seen, bool Duplex);
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    public Func<LanReverseAccess, CancellationToken, Task<bool>>? AcceptReverse { get; set; }
    public Func<int>? ApprovalWaitSeconds { get; set; }
    public event Action<string>? PeerDisconnected;
    /// <summary>仅在用户发起双向连接且对端接受后授权反向读取。</summary>
    public string GrantAccess(string deviceId, string name)
    {
        if (!LanAddress.IsHex(deviceId, 32) || deviceId == _deviceId || _lifetime.IsCancellationRequested) throw new InvalidDataException("Invalid peer");
        if (_sessions.Count >= 16) throw new InvalidOperationException("Session limit reached");
        var token = LanAddress.RandomKey(); _sessions[token] = new(deviceId, name, "", DateTimeOffset.UtcNow, false); return token;
    }
    public void RevokeDevice(string deviceId) { foreach (var entry in _sessions.Where(p => p.Value.DeviceId == deviceId)) _sessions.TryRemove(entry.Key, out _); }
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, int Count)> _attempts = new();
    private TcpListener? _listener;
    private string _pairKey = LanAddress.RandomKey();
    private DateTimeOffset _pairExpires = DateTimeOffset.UtcNow.AddMinutes(5);
    public int Port { get; private set; }
    public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(_certificate.RawData));
    public LanLibraryServer(X509Certificate2 certificate, string deviceId, string name, Func<LanCatalog> catalog, Func<string, string, Task<LanFile?>> open)
        : this(certificate, deviceId, name, catalog, open, null) { }
    public LanLibraryServer(X509Certificate2 certificate, string deviceId, string name, Func<LanCatalog> catalog, Func<string, string, Task<LanFile?>> open,
        Func<LanConnectionRequest, CancellationToken, Task<bool>>? approve = null)
    { _certificate = certificate; _deviceId = deviceId; _name = name; _catalog = catalog; _open = open; _approve = approve; }
    public void Start(int port = 0) { _listener = new TcpListener(IPAddress.Any, port); _listener.Start(12); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _ = AcceptAsync(); }
    public string Invitation(string address) => $"nonet://connect?host={Uri.EscapeDataString(address)}&port={Port}&cert={Fingerprint}&key={_pairKey}";
    public void RenewInvitation() { _pairKey = LanAddress.RandomKey(); _pairExpires = DateTimeOffset.UtcNow.AddMinutes(5); }
    public static X509Certificate2 Certificate(string path)
    {
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12FromFile(path, null);
        using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=Nonet local library", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return X509CertificateLoader.LoadPkcs12FromFile(path, null);
    }
    private async Task AcceptAsync()
    {
        try { while (!_lifetime.IsCancellationRequested) { var client = await _listener!.AcceptTcpClientAsync(_lifetime.Token); if (client.Client.RemoteEndPoint is not IPEndPoint endpoint || !LanAddress.IsPrivate(endpoint.Address) || !_slots.Wait(0)) { client.Dispose(); continue; } _ = ServeAsync(client, endpoint.Address.ToString()); } }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task ServeAsync(TcpClient client, string remote)
    {
        try
        {
            using (client)
            using (var tls = new SslStream(client.GetStream()))
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, deadline.Token);
                var lines = (await ReadHeaders(tls, deadline.Token)).Split("\r\n"); var first = lines[0].Split(' ');
                if (first.Length != 3 || first[0] is not ("GET" or "HEAD" or "POST")) { await Respond(tls, 405, [], "text/plain", false, deadline.Token); return; }
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1).Where(l => l.Contains(':'))) { var split = line.IndexOf(':'); if (!fields.TryAdd(line[..split], line[(split + 1)..].Trim())) throw new InvalidDataException(); }
                var route = first[1].Split('?')[0]; var head = first[0] == "HEAD";
                if (route == "/v1/pair" && first[0] == "POST")
                {
                    // 清理过期会话与计数，避免长期共享耗尽配对名额或累积无界状态。
                    var now = DateTimeOffset.UtcNow;
                    foreach (var item in _sessions.Where(s => s.Value.Seen < now.AddMinutes(-30))) _sessions.TryRemove(item.Key, out _);
                    foreach (var item in _attempts.Where(s => s.Value.At < now.AddMinutes(-1))) _attempts.TryRemove(item.Key, out _);
                    if (_attempts.Count >= 256 && !_attempts.ContainsKey(remote)) { await Respond(tls, 429, [], "text/plain", false, deadline.Token); return; }
                    var attempt = _attempts.AddOrUpdate(remote, (DateTimeOffset.UtcNow, 1), (_, old) => old.At < DateTimeOffset.UtcNow.AddMinutes(-1) ? (DateTimeOffset.UtcNow, 1) : (old.At, old.Count + 1));
                    if (attempt.Count > 6 || !int.TryParse(fields.GetValueOrDefault("Content-Length"), out var length) || length is < 1 or > 2048 || fields.ContainsKey("Transfer-Encoding")) { await Respond(tls, 429, [], "text/plain", false, deadline.Token); return; }
                    var body = new byte[length]; await tls.ReadExactlyAsync(body, deadline.Token); var request = JsonSerializer.Deserialize(body, LanJson.Default.LanPairRequest);
                    if (_sessions.Count >= 16 || request is null || !LanAddress.IsHex(request.DeviceId ?? "", 32) || request.DeviceId == _deviceId ||
                        string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 || request.Name.Any(char.IsControl) ||
                        _pairExpires < DateTimeOffset.UtcNow || !FixedEquals(request.Key ?? "", _pairKey)) { await Respond(tls, 403, [], "text/plain", false, deadline.Token); return; }
                    // 有效邀请不等于读取授权。默认拒绝；等待审批不占满音频连接名额，停止共享会取消审批。
                    if (_approve is null || !_pairSlots.Wait(0)) { await Respond(tls, 403, [], "text/plain", false, deadline.Token); return; }
                    var accepted = false;
                    try
                    {
                        var wait = Math.Clamp(ApprovalWaitSeconds?.Invoke() ?? 30, 10, 120);
                        deadline.CancelAfter(TimeSpan.FromSeconds(wait + 5));
                        using var approval = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); approval.CancelAfter(TimeSpan.FromSeconds(wait));
                        try { accepted = await _approve(new(request.DeviceId!, request.Name, remote) { WaitSeconds = wait, Duplex = request.Duplex }, approval.Token).WaitAsync(approval.Token); }
                        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested) { }
                    }
                    finally { _pairSlots.Release(); }
                    if (!accepted || _lifetime.IsCancellationRequested || !FixedEquals(request.Key!, _pairKey))
                    { await Respond(tls, 403, [], "text/plain", false, deadline.Token); return; }
                    var duplex = request.Duplex && AcceptReverse is not null;
                    var token = LanAddress.RandomKey(); _sessions[token] = new(request.DeviceId!, request.Name, remote, DateTimeOffset.UtcNow, duplex);
                    await Respond(tls, 200, JsonSerializer.SerializeToUtf8Bytes(new LanPairResult(_deviceId, _name, token) { Duplex = duplex }, LanJson.Default.LanPairResult), "application/json", false, deadline.Token); return;
                }
                var auth = fields.GetValueOrDefault("Authorization", ""); var bearer = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..] : "";
                if (!_sessions.TryGetValue(bearer, out var session) || session.Seen < DateTimeOffset.UtcNow.AddMinutes(-30) || session.Address.Length > 0 && session.Address != remote) { _sessions.TryRemove(bearer, out _); await Respond(tls, 401, [], "text/plain", head, deadline.Token); return; }
                var refreshed = session with { Seen = DateTimeOffset.UtcNow }; _sessions.TryUpdate(bearer, refreshed, session); session = refreshed;
                if (route == "/v1/disconnect" && first[0] == "POST")
                { RevokeDevice(session.DeviceId); PeerDisconnected?.Invoke(session.DeviceId); await Respond(tls, 200, [], "application/json", false, deadline.Token); return; }
                if (route == "/v1/duplex" && first[0] == "POST")
                {
                    if (!session.Duplex || AcceptReverse is null || !int.TryParse(fields.GetValueOrDefault("Content-Length"), out var length) || length is < 1 or > 2048 || fields.ContainsKey("Transfer-Encoding"))
                    { await Respond(tls, 403, [], "text/plain", false, deadline.Token); return; }
                    var bytes = new byte[length]; await tls.ReadExactlyAsync(bytes, deadline.Token);
                    var grant = JsonSerializer.Deserialize(bytes, LanJson.Default.LanDuplexGrant);
                    if (grant is null || grant.Port is < 1024 or > 65535 || !LanAddress.IsHex(grant.Fingerprint ?? "", 64) || !LanAddress.IsHex(grant.Token ?? "", 64) || !_sessions.TryUpdate(bearer, session with { Duplex = false }, session))
                    { await Respond(tls, 400, [], "text/plain", false, deadline.Token); return; }
                    // 回连地址只能是实际建立配对的私网来源，不接受请求体里的任意 URL。
                    var accepted = await AcceptReverse(new(session.DeviceId, session.Name, new UriBuilder("https", remote, grant.Port).Uri, grant.Fingerprint!, grant.Token!), deadline.Token);
                    if (!accepted) RevokeDevice(session.DeviceId);
                    await Respond(tls, accepted ? 200 : 409, [], "application/json", false, deadline.Token); return;
                }
                if (first[0] == "POST") { await Respond(tls, 405, [], "text/plain", false, deadline.Token); return; }
                // 凭据和实际来源均已验证；不向插件或日志交付任何认证信息。
                if (route is "/v1/info" or "/v1/catalog") LanLibraryClient.ObserveAuthenticatedPeer(session.DeviceId);
                if (route == "/v1/info") { var c = _catalog(); await Respond(tls, 200, JsonSerializer.SerializeToUtf8Bytes(new LanInfo(_deviceId, _name, c.Revision), LanJson.Default.LanInfo), "application/json", head, deadline.Token); return; }
                if (route == "/v1/catalog")
                {
                    var c = _catalog(); var q = first[1].Split('?', 2).ElementAtOrDefault(1) ?? ""; var offsetValue = q.Split('&').FirstOrDefault(x => x.StartsWith("offset=", StringComparison.Ordinal));
                    var offset = int.TryParse(offsetValue?[7..], out var n) ? Math.Clamp(n, 0, 20000) : 0;
                    await Respond(tls, 200, JsonSerializer.SerializeToUtf8Bytes(c with { Tracks = c.Tracks.Skip(offset).Take(200).ToArray(), Playlists = offset == 0 ? c.Playlists : [] }, LanJson.Default.LanCatalog), "application/json", head, deadline.Token); return;
                }
                var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3 || parts[0] != "v1" || parts[1] is not ("audio" or "artwork" or "lyrics")) { await Respond(tls, 404, [], "text/plain", head, deadline.Token); return; }
                var id = Uri.UnescapeDataString(parts[2]); if (id.Length > 256 || id.Contains('/') || id.Contains('\\')) { await Respond(tls, 400, [], "text/plain", head, deadline.Token); return; }
                using var file = await _open(parts[1], id); if (file is null) { await Respond(tls, 404, [], "text/plain", head, deadline.Token); return; }
                long start = 0, end = file.Length - 1; var partial = fields.TryGetValue("Range", out var range);
                if (partial && !TryRange(range!, file.Length, out start, out end)) { await WriteHeader(tls, 416, 0, "text/plain", $"Content-Range: bytes */{file.Length}\r\n", deadline.Token); return; }
                await WriteHeader(tls, partial ? 206 : 200, file.Length == 0 ? 0 : end - start + 1, file.ContentType, "Accept-Ranges: bytes\r\n" + (partial ? $"Content-Range: bytes {start}-{end}/{file.Length}\r\n" : ""), deadline.Token);
                if (head || file.Length == 0) return; if (start > 0) file.Content.Seek(start, SeekOrigin.Begin);
                // 大文件仅流式复制固定缓冲；停止共享时取消全部连接。
                deadline.CancelAfter(TimeSpan.FromMinutes(30)); var buffer = new byte[65536]; var remaining = end - start + 1;
                while (remaining > 0) { var read = await file.Content.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), deadline.Token); if (read == 0) break; await tls.WriteAsync(buffer.AsMemory(0, read), deadline.Token); remaining -= read; }
            }
        }
        // 单个只读请求失败只关闭该连接，不让文件访问或回调异常成为未观察任务。
        catch (Exception e) when (e is not OutOfMemoryException) { }
        finally { _slots.Release(); }
    }
    public static bool TryRange(string value, long length, out long start, out long end)
    {
        start = 0; end = length - 1; if (!value.StartsWith("bytes=", StringComparison.Ordinal) || value.Contains(',') || length <= 0) return false;
        var parts = value[6..].Split('-'); if (parts.Length != 2) return false;
        if (parts[0].Length == 0) { if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0) return false; start = Math.Max(0, length - suffix); }
        else { if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out start)) return false; if (parts[1].Length > 0 && !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)) return false; end = Math.Min(end, length - 1); }
        return start >= 0 && start < length && end >= start;
    }
    private static bool FixedEquals(string a, string b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    private static async Task<string> ReadHeaders(Stream stream, CancellationToken token)
    {
        var bytes = new List<byte>(); var b = new byte[1];
        while (bytes.Count < 16384) { if (await stream.ReadAsync(b, token) == 0) throw new IOException(); bytes.Add(b[0]); if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10) return Encoding.ASCII.GetString(bytes.ToArray()); }
        throw new InvalidDataException();
    }
    private static async Task WriteHeader(Stream stream, int code, long length, string type, string extra, CancellationToken token) => await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Response\r\nContent-Length: {length}\r\nContent-Type: {type}\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n{extra}\r\n"), token);
    private static async Task Respond(Stream stream, int code, byte[] bytes, string type, bool head, CancellationToken token) { await WriteHeader(stream, code, bytes.Length, type, "", token); if (!head) await stream.WriteAsync(bytes, token); }
    public void Dispose() { _lifetime.Cancel(); _listener?.Stop(); _sessions.Clear(); }
}
