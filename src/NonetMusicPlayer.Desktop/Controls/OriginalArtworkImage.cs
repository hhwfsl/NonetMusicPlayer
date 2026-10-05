using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>详情封面按原始像素解码；列表仍使用有界缩略图缓存，离开页面及时释放大图。</summary>
public sealed class OriginalArtworkImage : Image
{
    private readonly string? _path;
    private Bitmap? _bitmap;
    private int _generation;
    public OriginalArtworkImage(string? path, IImage? preview)
    {
        _path = path; Source = preview; Stretch = Stretch.UniformToFill;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        AttachedToVisualTree += async (_, _) =>
        {
            var generation = ++_generation;
            if (string.IsNullOrWhiteSpace(_path)) return;
            try
            {
                var decoded = await Task.Run(() =>
                {
                    // 复用图片大小与像素限制，但这里不能用 320px 缓存作为最终详情图。
                    var file = new FileInfo(_path);
                    if (!file.Exists || file.Length > CoverCropService.MaximumFileBytes) throw new InvalidDataException("Original cover too large.");
                    using (var probe = File.OpenRead(_path))
                    using (var codec = SkiaSharp.SKCodec.Create(probe))
                        if (codec is null || (long)codec.Info.Width * codec.Info.Height > CoverCropService.MaximumPixels) throw new InvalidDataException("Original cover pixel limit exceeded.");
                    using var input = File.OpenRead(_path); return new Bitmap(input);
                });
                if (generation != _generation || TopLevel.GetTopLevel(this) is null) { decoded.Dispose(); return; }
                _bitmap?.Dispose(); _bitmap = decoded; Source = decoded;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
            { AppLog.Warning("Artwork", "Unable to decode original playlist cover", error); }
        };
        DetachedFromVisualTree += (_, _) => { ++_generation; Source = null; _bitmap?.Dispose(); _bitmap = null; };
    }
}
