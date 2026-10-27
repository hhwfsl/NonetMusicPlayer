using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>详情封面按原始像素解码；列表仍使用有界缩略图缓存，离开页面及时释放大图。</summary>
public sealed class OriginalArtworkImage : Image
{
    private readonly string? _path;
    private readonly IImage? _preview;
    private Bitmap? _bitmap;
    private int _generation;
    private bool _loading;
    private bool _suspended;
    internal long DecodedBytes => _bitmap is null ? 0 : 4L * _bitmap.PixelSize.Width * _bitmap.PixelSize.Height;
    public OriginalArtworkImage(string? path, IImage? preview)
    {
        _path = path; _preview = preview; Source = preview; Stretch = Stretch.UniformToFill;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        AttachedToVisualTree += async (_, _) => await LoadOriginalAsync();
        DetachedFromVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (this.IsAttachedToVisualTree()) return;
            ++_generation; _loading = false; Source = null; _bitmap?.Dispose(); _bitmap = null;
        }, DispatcherPriority.Background);
    }
    /// <summary>隐藏时仅释放解码像素；保留控件及有界预览，恢复时无需导航或改变布局。</summary>
    internal void SuspendDecodedPixels()
    {
        _suspended = true; ++_generation; _loading = false;
        Source = _preview; _bitmap?.Dispose(); _bitmap = null;
    }
    internal void ResumeDecodedPixels() { _suspended = false; _ = LoadOriginalAsync(); }
    private async Task LoadOriginalAsync()
    {
        if (_suspended || TopLevel.GetTopLevel(this) is not Window { IsVisible: true } window || window.WindowState == WindowState.Minimized) return;
        // 方案同步重挂接时复用原图和进行中的解码，不清空封面或重复读取大图。
        if (_bitmap is not null || _loading || string.IsNullOrWhiteSpace(_path)) return;
        var generation = ++_generation;
        _loading = true;
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
        finally { if (generation == _generation) _loading = false; }
    }
}
