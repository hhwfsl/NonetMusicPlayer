using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>歌词页从播放栏方向展开/收回。只为单次切换保存有界快照，不冻结播放时钟或保留旧页面。</summary>
public sealed class LyricsPageMotion : IDisposable
{
    public const int MaximumSnapshotPixels = 2_000_000;
    private readonly RetargetableMotion _slide = new();
    private Action? _cancel;
    private double? _interrupted;
    private int _generation;
    public bool IsRunning => _cancel is not null;
    public static Bitmap? Capture(Control source, Rect region, double? renderScaling = null)
    {
        if (!InterfaceMotion.IsEnabled || source.Bounds.Width < 1 || source.Bounds.Height < 1 || region.Width < 1 || region.Height < 1) return null;
        try
        {
            var scale = Math.Min(renderScaling ?? TopLevel.GetTopLevel(source)?.RenderScaling ?? 1, Math.Sqrt(MaximumSnapshotPixels / (source.Bounds.Width * source.Bounds.Height)));
            var size = new PixelSize(Math.Max(1, (int)(source.Bounds.Width * scale)), Math.Max(1, (int)(source.Bounds.Height * scale)));
            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bitmap.Render(source);
            var x = Math.Clamp((int)Math.Round(region.X * scale), 0, size.Width - 1);
            var y = Math.Clamp((int)Math.Round(region.Y * scale), 0, size.Height - 1);
            var pixels = new PixelRect(x, y, Math.Clamp((int)Math.Round(region.Width * scale), 1, size.Width - x), Math.Clamp((int)Math.Round(region.Height * scale), 1, size.Height - y));
            // 裁剪使用物理像素，输出统一为 96 DPI。避免 CroppedBitmap 将非 96 DPI 源矩形
            // 再按逻辑单位传入绘制后端，造成高 DPI 或降采样快照出现先放大的画面跳变。
            var cropped = new WriteableBitmap(pixels.Size, new Vector(96, 96), bitmap.Format ?? PixelFormat.Bgra8888, bitmap.AlphaFormat ?? AlphaFormat.Premul);
            try
            {
                using var buffer = cropped.Lock();
                bitmap.CopyPixels(pixels, buffer.Address, checked(buffer.RowBytes * buffer.Size.Height), buffer.RowBytes);
                return cropped;
            }
            catch { cropped.Dispose(); throw; }
        }
        catch (Exception error) { AppLog.Warning("UI", "Lyrics transition snapshot skipped", error); return null; }
    }
    public void Begin(Grid layer, ContentControl host, Bitmap snapshot, Rect region, Vector layerOffset, bool opening, bool playerTop)
        => Begin(layer, host, snapshot, region, layerOffset, opening, playerTop, null);
    public void Begin(Grid layer, ContentControl host, Bitmap snapshot, Rect region, Vector layerOffset, bool opening, bool playerTop, InterfaceMotionProfile? profile)
        => Begin(layer, host, snapshot, region, layerOffset, opening, playerTop, profile, null, default);
    public void Begin(Grid layer, ContentControl host, Bitmap snapshot, Rect region, Vector layerOffset, bool opening, bool playerTop, InterfaceMotionProfile? profile, Bitmap? previousPage, Size previousPageSize, Rect? playerRegion = null)
    {
        var start = _interrupted ?? (opening ? 0 : 1);
        _interrupted = null; Cancel();
        if (!InterfaceMotion.IsEnabled || profile?.LyricsDurationMs == 0) { snapshot.Dispose(); previousPage?.Dispose(); return; }
        var generation = ++_generation;
        var player = playerRegion ?? new Rect(region.X, playerTop ? region.Y - 96 : region.Bottom, region.Width, 96);
        // 只在播放栏朝向内容区的边缘裁剪；整张歌词图保持原尺寸，仅沿 Y 轴平移。
        var top = playerTop ? Math.Max(region.Y, player.Bottom) : region.Y;
        var bottom = playerTop ? region.Bottom : Math.Min(region.Bottom, player.Y);
        var full = new Rect(region.X - layerOffset.X, top - layerOffset.Y, region.Width, Math.Max(1, bottom - top));
        var image = new Image { Source = snapshot, Stretch = Stretch.Fill, Width = full.Width, Height = full.Height, IsHitTestVisible = false };
        var transform = new TranslateTransform(); image.RenderTransform = transform;
        var overlay = new Border { Name = "LyricsTransitionOverlay", Width = full.Width, Height = full.Height,
            Margin = new Thickness(full.X, full.Y, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, IsHitTestVisible = false, ClipToBounds = true, Child = image, ZIndex = 1000 };
        Grid.SetRowSpan(overlay, 100); Grid.SetColumnSpan(overlay, 100);
        Border? backdrop = null;
        if (opening && previousPage is not null)
        {
            backdrop = new Border { Name = "LyricsTransitionBackdrop", Child = new Image { Source = previousPage, Stretch = Stretch.Fill },
                Width = previousPageSize.Width, Height = previousPageSize.Height, Margin = new Thickness(-layerOffset.X, -layerOffset.Y, 0, 0),
                HorizontalAlignment = overlay.HorizontalAlignment, VerticalAlignment = overlay.VerticalAlignment, IsHitTestVisible = false, ZIndex = 999 };
            Grid.SetRowSpan(backdrop, 100); Grid.SetColumnSpan(backdrop, 100); layer.Children.Add(backdrop);
        }
        IDisposable? opacity = null, input = null; var finished = false;
        void Draw(double value) => transform.Y = (playerTop ? -1 : 1) * full.Height * (1 - value);
        void Finish()
        {
            if (finished) return; finished = true; _slide.Changed -= Draw;
            // 真实歌词、旧页底图和快照在同一个 UI 事务中交接，不透出旧页的一帧。
            opacity?.Dispose(); input?.Dispose();
            if (backdrop is not null) { layer.Children.Remove(backdrop); if (backdrop.Child is Image old) old.Source = null; }
            layer.Children.Remove(overlay); image.Source = null; snapshot.Dispose(); previousPage?.Dispose();
            if (_generation == generation) _cancel = null;
        }
        _slide.MoveAsync(start, 0).GetAwaiter().GetResult(); Draw(start); layer.Children.Add(overlay);
        if (opening)
        {
            opacity = host.Bind(Visual.OpacityProperty, new ConstantValue(0d), BindingPriority.Animation);
            input = host.Bind(Avalonia.Input.InputElement.IsHitTestVisibleProperty, new ConstantValue(false), BindingPriority.Animation);
        }
        _slide.Changed += Draw;
        _cancel = () => { _slide.Cancel(); Finish(); };
        _ = Run();
        async Task Run()
        {
            try { await _slide.MoveAsync(opening ? 1 : 0, profile?.LyricsDurationMs ?? 300); }
            catch (Exception error) { AppLog.Warning("UI", "歌词垂直转场已结束", error); }
            finally { await Dispatcher.UIThread.InvokeAsync(Finish); }
        }
    }
    /// <summary>快速反向切换只保存垂直进度，不截取已经移动的图像再二次缩放。</summary>
    public void Interrupt() { var value = IsRunning ? _slide.Value : (double?)null; Cancel(); _interrupted = value; }
    public void Cancel() { _interrupted = null; var cancel = _cancel; _cancel = null; cancel?.Invoke(); }
    public void Dispose() { Cancel(); _slide.Dispose(); }
    private sealed class ConstantValue(object value) : IObservable<object>
    {
        public IDisposable Subscribe(IObserver<object> observer) { observer.OnNext(value); return new ElementLayoutService.ActionLease(() => { }); }
    }
}
