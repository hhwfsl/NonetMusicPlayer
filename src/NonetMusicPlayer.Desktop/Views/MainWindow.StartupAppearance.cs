using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private double ViewportWidth => Bounds.Width > 0 ? Bounds.Width : double.IsFinite(Width) ? Width : 1120;
    private double ViewportHeight => Bounds.Height > 0 ? Bounds.Height : double.IsFinite(Height) ? Height : 760;
    /// <summary>隐藏时准备实际尺寸、模板、样式和图片首帧，避免 Loaded 才应用风格造成闪变。</summary>
    public void PrepareInitialAppearance()
    {
        if (IsVisible || Content is not Control content) return;
        ApplyTemplate();
        var size = new Size(Math.Max(1, ViewportWidth), Math.Max(1, ViewportHeight));
        for (var pass = 0; pass < 2; pass++)
        {
            content.Measure(size); content.Arrange(new Rect(size));
            SuspendElementExtensions(); ApplyAppearance(); RefreshElementExtensions();
            content.Measure(size); content.Arrange(new Rect(size));
            StyleLayers.Refresh();
        }
        // 离屏绘制预热图片与模板，像素上限固定；不创建可见窗口，也不依赖是否启用动画。
        var scale = Math.Min(1, Math.Sqrt(LyricsPageMotion.MaximumSnapshotPixels / (size.Width * size.Height)));
        using var frame = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale))), new Vector(96 * scale, 96 * scale));
        frame.Render(content);
    }
}
