using System.Net;
using System.Net.Http.Headers;

namespace NonetMusicPlayer.Core.Streaming;
/// <summary>仅访问带会话令牌的本地插件地址，按需读取 Range，不接受任意服务器直连。</summary>
public sealed class LoopbackRangeStream : Stream
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Uri _uri;
    private readonly long _length;
    private long _position;
    private byte[] _buffer = [];
    private long _bufferStart;
    public static void Validate(Uri uri)
    {
        if (uri.Scheme != "http" || !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || !(ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback)) || uri.UserInfo.Length != 0)
            throw new InvalidDataException("插件只能返回 127.0.0.1 / ::1 的本地 HTTP 播放地址。禁止服务器直连和重定向。");
        var token = uri.Query.Split('&').FirstOrDefault(x => x.TrimStart('?').StartsWith("token=", StringComparison.Ordinal));
        if (token is null || !System.Text.RegularExpressions.Regex.IsMatch(Uri.UnescapeDataString(token[(token.IndexOf('=') + 1)..]), "^[0-9a-fA-F]{32,}$")) throw new InvalidDataException("播放地址缺少至少 128 位的会话令牌（32 位十六进制）。");
    }
    public LoopbackRangeStream(Uri uri)
    {
        Validate(uri); _uri = uri;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri); using var response = _http.Send(request); response.EnsureSuccessStatusCode();
            _length = response.Content.Headers.ContentLength ?? throw new InvalidDataException("插件流必须提供 Content-Length 与 Range 支持。");
            if (_length <= 0) throw new InvalidDataException("插件返回空音频。");
        }
        catch { _http.Dispose(); throw; }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> output)
    {
        if (_position >= Length || output.Length == 0) return 0;
        if (_position < _bufferStart || _position >= _bufferStart + _buffer.Length)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _uri); var end = Math.Min(Length - 1, _position + 262143);
            request.Headers.Range = new RangeHeaderValue(_position, end);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != _position || response.Content.Headers.ContentRange?.Length != Length) throw new InvalidDataException("插件未正确响应 HTTP Range（需要 206 和 Content-Range）。");
            if (response.Content.Headers.ContentLength != end - _position + 1) throw new InvalidDataException("插件流长度不一致。");
            _buffer = new byte[(int)(end - _position + 1)];
            using var content = response.Content.ReadAsStream(timeout.Token); content.ReadExactlyAsync(_buffer, timeout.Token).AsTask().GetAwaiter().GetResult();
            _bufferStart = _position;
        }
        var count = Math.Min(output.Length, _buffer.Length - (int)(_position - _bufferStart)); _buffer.AsSpan((int)(_position - _bufferStart), count).CopyTo(output); _position += count; return count;
    }
    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, SeekOrigin.End => Length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _position; set { if (value < 0 || value > Length) throw new IOException("定位超出音频范围。"); _position = value; } }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
}
