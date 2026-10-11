using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>Skia 原生逐帧处理动态 WebP/APNG，不把所有解码帧缓存到内存。</summary>
public sealed class AnimatedRasterDecoder : IDisposable
{
    private readonly Stream _stream;
    private readonly SKCodec _codec;
    private readonly SKBitmap _decoded, _scaled;
    private readonly SKCodecFrameInfo[] _frames;
    private readonly long[] _ends;
    private int _prior = -1;
    private long _cycle = -1;
    public static bool Handles(string path) => Path.GetExtension(path).ToLowerInvariant() is ".webp" or ".png";
    public AnimatedRasterDecoder(string path)
    {
        _stream = File.OpenRead(path);
        try
        {
            _codec = SKCodec.Create(_stream) ?? throw new InvalidDataException(L10n.T("Background.UnsupportedMedia"));
            if (_codec.FrameCount is < 2 or > 10000 || _codec.Info.Width <= 0 || _codec.Info.Height <= 0 || (long)_codec.Info.Width * _codec.Info.Height > 16 * 1024 * 1024) throw new InvalidDataException(L10n.T("Background.UnsupportedMedia"));
            _frames = _codec.FrameInfo; _ends = new long[_frames.Length];
            for (var i = 0; i < _frames.Length; i++) _ends[i] = (i == 0 ? 0 : _ends[i - 1]) + Math.Max(10, _frames[i].Duration);
            var scale = Math.Min(1, Math.Min(1280d / _codec.Info.Width, 720d / _codec.Info.Height));
            var size = _codec.GetScaledDimensions((float)scale);
            _decoded = new(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            _scaled = new(new SKImageInfo(Math.Max(1, (int)(_codec.Info.Width * scale)), Math.Max(1, (int)(_codec.Info.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Unpremul));
        }
        catch { _scaled?.Dispose(); _decoded?.Dispose(); _codec?.Dispose(); _stream.Dispose(); throw; }
    }
    public VideoFrame ReadFrame(long elapsedMilliseconds)
    {
        var cycle = elapsedMilliseconds / _ends[^1]; var time = elapsedMilliseconds % _ends[^1];
        var index = Array.FindIndex(_ends, end => end > time);
        if (_cycle != cycle || index < _prior) _prior = -1; _cycle = cycle;
        if (index != _prior)
        {
            var options = new SKCodecOptions(index) { PriorFrame = _prior >= _frames[index].RequiredFrame && _prior < index && _prior >= 0 && _frames[_prior].DisposalMethod.ToString() != "RestorePrevious" ? _prior : -1 };
            if (_codec.GetPixels(_decoded.Info, _decoded.GetPixels(), _decoded.RowBytes, options) != SKCodecResult.Success) throw new InvalidDataException(L10n.T("Background.UnsupportedMedia"));
            _prior = index;
            if (!_decoded.ScalePixels(_scaled, new SKSamplingOptions(SKFilterMode.Linear))) throw new InvalidDataException(L10n.T("Background.UnsupportedMedia"));
        }
        var count = _scaled.Width * _scaled.Height * 4; var pixels = ArrayPool<byte>.Shared.Rent(count);
        Marshal.Copy(_scaled.GetPixels(), pixels, 0, count); return new(_scaled.Width, _scaled.Height, pixels);
    }
    public void Dispose() { _scaled.Dispose(); _decoded.Dispose(); _codec.Dispose(); _stream.Dispose(); }
}
