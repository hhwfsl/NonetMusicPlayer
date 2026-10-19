using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Core.Network;

/// <summary>局域网只传音乐元数据，不包含本地路径、设置、历史或播放控制。</summary>
public sealed record LanSong(string Id, string Title, string Artist, string Album, long DurationMs, bool Artwork, bool Lyrics);
public sealed record LanPlaylist(string Id, string Name, string Description, string[] TrackIds);
[method: JsonConstructor]
public sealed record LanCatalog(string DeviceId, string Name, string Revision, LanSong[] Tracks, LanPlaylist[] Playlists, int Total, string DeviceKind = "unknown")
{
    // 新增可选类型字段，同时保留旧程序集的构造与解构签名。
    public LanCatalog(string deviceId, string name, string revision, LanSong[] tracks, LanPlaylist[] playlists, int total)
        : this(deviceId, name, revision, tracks, playlists, total, "unknown") { }
    public void Deconstruct(out string deviceId, out string name, out string revision, out LanSong[] tracks, out LanPlaylist[] playlists, out int total)
    { deviceId = DeviceId; name = Name; revision = Revision; tracks = Tracks; playlists = Playlists; total = Total; }
}
/// <summary>配对审批只交付设备标识和网络地址，不将邀请密钥或会话令牌暴露给界面、插件或日志。</summary>
public sealed record LanConnectionRequest(string DeviceId, string Name, string Address)
{ public int WaitSeconds { get; init; } = 30; public bool Duplex { get; init; } }
public sealed record LanInfo(string DeviceId, string Name, string Revision, int Protocol = 1);
public sealed record LanPairRequest(string Key, string DeviceId, string Name) { public bool Duplex { get; init; } }
public sealed record LanPairResult(string DeviceId, string Name, string Token, int Protocol = 1) { public bool Duplex { get; init; } }
/// <summary>反向凭据只在接收方批准后经固定证书的连接发送，不放入邀请或通知。</summary>
public sealed record LanDuplexGrant(int Port, string Fingerprint, string Token);
public sealed record LanReverseAccess(string DeviceId, string Name, Uri Origin, string Fingerprint, string Token);
public sealed record LanFile(Stream Content, long Length, string ContentType) : IDisposable { public void Dispose() => Content.Dispose(); }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LanCatalog))]
[JsonSerializable(typeof(LanInfo))]
[JsonSerializable(typeof(LanPairRequest))]
[JsonSerializable(typeof(LanPairResult))]
[JsonSerializable(typeof(LanDuplexGrant))]
public partial class LanJson : JsonSerializerContext;

public static class LanAddress
{
    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        return b.Length == 4 ? b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 : ip.IsIPv6LinkLocal || (b[0] & 0xfe) == 0xfc;
    }
    /// <summary>邀请含短期配对密钥和证书指纹，禁止写日志或导出到备份。</summary>
    public static (Uri Origin, string Fingerprint, string Key) ParseInvitation(string text)
    {
        var uri = new Uri(text.Trim());
        if (uri.Scheme != "nonet" || uri.Host != "connect") throw new InvalidDataException("Invalid Nonet invitation.");
        var q = uri.Query.TrimStart('?').Split('&').Select(v => v.Split('=', 2)).Where(v => v.Length == 2).ToDictionary(v => v[0], v => Uri.UnescapeDataString(v[1]));
        var host = q.GetValueOrDefault("host", "");
        if (!IPAddress.TryParse(host, out var ip) || !IsPrivate(ip) || !int.TryParse(q.GetValueOrDefault("port"), out var port) || port is < 1024 or > 65535) throw new InvalidDataException("Only local network addresses are accepted.");
        var fingerprint = q.GetValueOrDefault("cert", "").ToLowerInvariant(); var key = q.GetValueOrDefault("key", "");
        if (!IsHex(fingerprint, 64) || !IsHex(key, 64)) throw new InvalidDataException("Incomplete Nonet invitation.");
        return (new UriBuilder("https", ip.ToString(), port).Uri, fingerprint, key);
    }
    public static bool IsHex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
    public static string RandomKey() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
