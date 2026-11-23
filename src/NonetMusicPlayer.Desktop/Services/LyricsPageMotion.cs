using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>歌词页从播放栏方向展开/收回。只为单次切换保存有界快照，不冻结播放时钟或保留旧页面。</summary>
public sealed class LyricsPageMotion : IDisposable
{
    public const int MaximumSnapshotPixels = 2_000_000;
    private const double BackdropBlurRadius = 40;
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
    public void Begin(Grid layer, ContentControl host, Bitmap snapshot, Rect region, IReadOnlyList<Rect> protectedAreas,
        IReadOnlyList<Control> workspace, IReadOnlyList<Control> chrome, bool opening, bool playerTop, int duration, Action? settled)
    {
        var start = _interrupted ?? (opening ? 0 : 1);
        Cancel();
        if (!InterfaceMotion.IsEnabled || duration == 0) { snapshot.Dispose(); settled?.Invoke(); return; }
        var generation = ++_generation;
        var full = region;
        var image = new Image { Source = snapshot, Stretch = Stretch.Fill, Width = full.Width, Height = full.Height, IsHitTestVisible = false };
        var transform = new TranslateTransform(); image.RenderTransform = transform;
        var overlay = new Border { Name = "LyricsTransitionOverlay", Width = full.Width, Height = full.Height,
            Margin = new Thickness(full.X, full.Y, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, IsHitTestVisible = false, ClipToBounds = true, Child = image, ZIndex = 1000 };
        // 转场图层单独让出原生标题栏和伸出播放栏的进度滑块。
        foreach (var area in protectedAreas)
        {
            if (!full.Intersects(area)) continue;
            var local = new Rect(new Point(area.X - full.X, area.Y - full.Y), area.Size);
            overlay.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                overlay.Clip ?? new RectangleGeometry(new Rect(full.Size)), new RectangleGeometry(local));
        }
        Grid.SetRowSpan(overlay, 100); Grid.SetColumnSpan(overlay, 100);
        var blur = new BlurEffect { Radius = BackdropBlurRadius * start };
        var protectedVisuals = chrome.ToHashSet<Visual>();
        var protectedParents = chrome.SelectMany(control => control.GetVisualAncestors()).ToHashSet();
        var blurLeases = workspace.SelectMany(BlurTargets)
            .Select(visual => visual.Bind(Visual.EffectProperty, new ConstantValue(blur), BindingPriority.Animation)).ToArray();
        // 方案可把品牌或播放器放进侧栏，只模糊这些控件所在分支以外的子树。
        IEnumerable<Visual> BlurTargets(Visual visual)
        {
            if (protectedVisuals.Contains(visual)) yield break;
            if (!protectedParents.Contains(visual)) { yield return visual; yield break; }
            foreach (var child in visual.GetVisualChildren())
                foreach (var target in BlurTargets(child)) yield return target;
        }
        var backdrop = new List<(double Top, Size Size, RectangleGeometry Reveal, IDisposable Lease)>();
        foreach (var control in workspace)
        {
            if (control.TranslatePoint(default, layer) is not { } origin) continue;
            var reveal = new RectangleGeometry(new Rect(control.Bounds.Size));
            var clip = control.Clip is { } previous ? new CombinedGeometry(GeometryCombineMode.Intersect, previous, reveal) : (Geometry)reveal;
            var lease = control.Bind(Visual.ClipProperty, new ConstantValue(clip), BindingPriority.Animation);
            backdrop.Add((origin.Y, control.Bounds.Size, reveal, lease));
        }
        IDisposable? opacity = null, input = null; var finished = false;
        void Draw(double value)
        {
            // 展开和收回共用同一进度，动画中途反向时模糊半径从当前值接续。
            blur.Radius = BackdropBlurRadius * value;
            transform.Y = (playerTop ? -1 : 1) * full.Height * (1 - value);
            // 只在歌词已覆盖的区域裁掉背景，透明方案也不会透出旧内容。
            var edge = (playerTop ? full.Bottom : full.Top) + transform.Y;
            foreach (var (top, size, reveal, _) in backdrop)
            {
                var cut = Math.Clamp(edge - top, 0, size.Height);
                reveal.Rect = playerTop ? new Rect(0, cut, size.Width, size.Height - cut) : new Rect(0, 0, size.Width, cut);
            }
        }
        void Finish()
        {
            if (finished) return; finished = true; _slide.Changed -= Draw;
            // 同一个 UI 事务内移除快照、还原裁剪并显示最终页面。
            opacity?.Dispose(); input?.Dispose();
            foreach (var item in backdrop) item.Lease.Dispose();
            foreach (var lease in blurLeases) lease.Dispose();
            layer.Children.Remove(overlay); image.Source = null; snapshot.Dispose();
            if (_generation == generation) _cancel = null;
            settled?.Invoke();
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
            try { await _slide.MoveAsync(opening ? 1 : 0, duration); }
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
