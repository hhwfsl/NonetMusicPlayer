using System.Net;
using System.Net.Http.Headers;

namespace NonetMusicPlayer.Core.Streaming;
/// <summary>仅访问带会话令牌的本地插件地址，按需读取 Range，不接受任意服务器直连。</summary>
public sealed class LoopbackRangeStream : Stream
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly Uri _uri;
    private readonly long _length;
    private readonly CancellationTokenSource _lifetime;
    private long _position;
    private readonly byte[] _buffer = new byte[65536];
    private int _bufferCount;
    private long _bufferStart;
    private HttpResponseMessage? _response;
    private Stream? _content;
    private long _responsePosition, _responseEnd;
    private int _disposed;
    public static void Validate(Uri uri)
    {
        if (uri.Scheme != "http" || !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || !(ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback)) || uri.UserInfo.Length != 0)
            throw new InvalidDataException("插件只能返回 127.0.0.1 / ::1 的本地 HTTP 播放地址。禁止服务器直连和重定向。");
        var token = uri.Query.Split('&').FirstOrDefault(x => x.TrimStart('?').StartsWith("token=", StringComparison.Ordinal));
        if (token is null || !System.Text.RegularExpressions.Regex.IsMatch(Uri.UnescapeDataString(token[(token.IndexOf('=') + 1)..]), "^[0-9a-fA-F]{32,}$")) throw new InvalidDataException("播放地址缺少至少 128 位的会话令牌（32 位十六进制）。");
    }
    public LoopbackRangeStream(Uri uri) : this(uri, null) { }
    public LoopbackRangeStream(Uri uri, CancellationToken token) : this(uri, null, token) { }
    /// <summary>局域网曲库使用已配对、固定证书且带认证的客户端；不放宽插件 URL 验证。</summary>
    internal LoopbackRangeStream(Uri uri, HttpClient? pairedClient) : this(uri, pairedClient, CancellationToken.None) { }
    internal LoopbackRangeStream(Uri uri, HttpClient? pairedClient, CancellationToken token)
    {
        if (pairedClient is null) Validate(uri);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _http = pairedClient ?? new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        _ownsClient = pairedClient is null; _uri = uri;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Head, uri); using var response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
            _length = response.Content.Headers.ContentLength ?? throw new InvalidDataException("插件流必须提供 Content-Length 与 Range 支持。");
            if (_length <= 0) throw new InvalidDataException("插件返回空音频。");
        }
        catch { if (_ownsClient) _http.Dispose(); _lifetime.Dispose(); throw; }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> output)
    {
        if (_position >= Length || output.Length == 0) return 0;
        _lifetime.Token.ThrowIfCancellationRequested();
        if (_position < _bufferStart || _position >= _bufferStart + _bufferCount)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            if (_content is null || _position != _responsePosition || _position > _responseEnd)
            {
                CloseResponse(); _responseEnd = Math.Min(Length - 1, _position + 1048575);
                using var request = new HttpRequestMessage(HttpMethod.Get, _uri); request.Headers.Range = new RangeHeaderValue(_position, _responseEnd);
                _response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (_response.StatusCode != HttpStatusCode.PartialContent || _response.Content.Headers.ContentRange?.From != _position || _response.Content.Headers.ContentRange?.To != _responseEnd || _response.Content.Headers.ContentRange?.Length != Length) throw new InvalidDataException("插件未正确响应 HTTP Range（需要 206 和 Content-Range）。");
                if (_response.Content.Headers.ContentLength != _responseEnd - _position + 1) throw new InvalidDataException("插件流长度不一致。");
                _content = _response.Content.ReadAsStream(timeout.Token); _responsePosition = _position;
            }
            // 保持最多 1 MiB 的顺序 Range，但只等待当前小块；首包不再等整段下载完才交给解码器。
            _bufferCount = _content.ReadAsync(_buffer.AsMemory(0, (int)Math.Min(_buffer.Length, _responseEnd - _position + 1)), timeout.Token).AsTask().GetAwaiter().GetResult();
            if (_bufferCount == 0) throw new EndOfStreamException("网络音频响应提前结束。");
            _bufferStart = _position;
            _responsePosition += _bufferCount;
        }
        var count = Math.Min(output.Length, _bufferCount - (int)(_position - _bufferStart)); _buffer.AsSpan((int)(_position - _bufferStart), count).CopyTo(output); _position += count; return count;
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
    /// <summary>切歌/退出先打断后台读取，解码器释放后再真正关闭 Stream。</summary>
    public void CancelPendingReads() { if (!_lifetime.IsCancellationRequested) _lifetime.Cancel(); }
    private void CloseResponse() { _content?.Dispose(); _content = null; _response?.Dispose(); _response = null; }
    protected override void Dispose(bool disposing) { if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0) { CancelPendingReads(); CloseResponse(); if (_ownsClient) _http.Dispose(); _lifetime.Dispose(); } base.Dispose(disposing); }
}
