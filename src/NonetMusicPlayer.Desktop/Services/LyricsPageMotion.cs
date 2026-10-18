using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>歌词页从播放栏方向展开/收回。只为单次切换保存有界快照，不冻结播放时钟或保留旧页面。</summary>
public sealed class LyricsPageMotion : IDisposable
{
    public const int MaximumSnapshotPixels = 2_000_000;
    private Action? _cancel;
    public bool IsRunning { get; private set; }
    public static CroppedBitmap? Capture(Control source, Rect region)
    {
        if (!InterfaceMotion.IsEnabled || source.Bounds.Width < 1 || source.Bounds.Height < 1 || region.Width < 1 || region.Height < 1) return null;
        try
        {
            var scale = Math.Min(TopLevel.GetTopLevel(source)?.RenderScaling ?? 1, Math.Sqrt(MaximumSnapshotPixels / (source.Bounds.Width * source.Bounds.Height)));
            var size = new PixelSize(Math.Max(1, (int)(source.Bounds.Width * scale)), Math.Max(1, (int)(source.Bounds.Height * scale)));
            var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            try
            {
                bitmap.Render(source);
                var x = Math.Clamp((int)(region.X * scale), 0, size.Width - 1);
                var y = Math.Clamp((int)(region.Y * scale), 0, size.Height - 1);
                return new CroppedBitmap(bitmap, new PixelRect(x, y, Math.Clamp((int)(region.Width * scale), 1, size.Width - x), Math.Clamp((int)(region.Height * scale), 1, size.Height - y)));
            }
            catch { bitmap.Dispose(); throw; }
        }
        catch (Exception error) { AppLog.Warning("UI", "Lyrics transition snapshot skipped", error); return null; }
    }
    public void Begin(Grid layer, ContentControl host, CroppedBitmap snapshot, Rect region, Vector layerOffset, bool opening, bool playerTop)
    {
        Cancel();
        if (!InterfaceMotion.IsEnabled) { snapshot.Dispose(); return; }
        var token = new CancellationTokenSource();
        var image = new Image { Source = snapshot, Stretch = Stretch.Fill, IsHitTestVisible = false };
        var overlay = new Border { Name = "LyricsTransitionOverlay", Child = image, ClipToBounds = true,
            IsHitTestVisible = false, Margin = new Thickness(region.X - layerOffset.X, region.Y - layerOffset.Y, 0, 0),
            Width = region.Width, Height = region.Height, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, ZIndex = 30 };
        Grid.SetRowSpan(overlay, 2);
        var distance = region.Height * (playerTop ? -1 : 1);
        // 基础值使用最终位置，动画解绑到资源释放之间不会闪回起点。
        var transform = new TranslateTransform(0, opening ? 0 : distance);
        IDisposable? opacityLease = null, inputLease = null;
        image.RenderTransform = transform;
        var completed = false;
        void Finish()
        {
            if (completed) return; completed = true;
            opacityLease?.Dispose(); inputLease?.Dispose(); layer.Children.Remove(overlay); image.Source = null; snapshot.Dispose();
            IsRunning = false; _cancel = null;
        }
        try
        {
            layer.Children.Add(overlay);
            if (opening)
            {
                // 真正的歌词页继续接收时钟和定位更新，只暂时隐藏绘制，避免停用视图造成定位丢失。
                opacityLease = host.Bind(Visual.OpacityProperty, new ConstantValue(0d), BindingPriority.Animation);
                inputLease = host.Bind(Avalonia.Input.InputElement.IsHitTestVisibleProperty, new ConstantValue(false), BindingPriority.Animation);
            }
            IsRunning = true;
            _cancel = () => { token.Cancel(); Finish(); };
            _ = Animate();
        }
        catch { Finish(); token.Dispose(); throw; }
        async Task Animate()
        {
            var animation = new Animation { Duration = TimeSpan.FromMilliseconds(opening ? 240 : 200), Easing = new CubicEaseOut(), Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, opening ? distance : 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, opening ? 0d : distance) } }
            } };
            // Avalonia 的 TransformAnimator 以 Visual 为目标，再定位其 TranslateTransform。
            try { await animation.RunAsync(image, token.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { AppLog.Warning("UI", "Lyrics transition skipped", error); }
            finally
            {
                if (Dispatcher.UIThread.CheckAccess()) Finish(); else await Dispatcher.UIThread.InvokeAsync(Finish);
                token.Dispose();
            }
        }
    }
    public void Cancel() => _cancel?.Invoke();
    public void Dispose() => Cancel();
    private sealed class ConstantValue(object value) : IObservable<object>
    {
        public IDisposable Subscribe(IObserver<object> observer) { observer.OnNext(value); return new ElementLayoutService.ActionLease(() => { }); }
    }
}
