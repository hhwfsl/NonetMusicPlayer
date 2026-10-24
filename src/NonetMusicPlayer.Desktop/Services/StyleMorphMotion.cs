using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Styling;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>有界快照变形动画。矩形、弧线路径与圆角共用同一时钟，不采用先缩放再归位的转场。</summary>
public sealed class StyleMorphMotion : IDisposable
{
    private Action? _cancel;
    public bool IsRunning => _cancel is not null;
    public static (Rect Bounds, double Radius) Sample(Rect from, Rect to, double startRadius, double endRadius, double arc, double progress)
    {
        var t = Math.Clamp(progress, 0, 1); var p = StyleLayerService.Ease(t);
        static double L(double a, double b, double t) => a + (b - a) * t;
        var bounds = new Rect(L(from.X, to.X, p), L(from.Y, to.Y, p) - 4 * arc * t * (1 - t), Math.Max(1, L(from.Width, to.Width, p)), Math.Max(1, L(from.Height, to.Height, p)));
        return (bounds, Math.Clamp(L(startRadius, endRadius, p), 0, Math.Min(bounds.Width, bounds.Height) / 2));
    }
    public Task PreviewAsync(Grid layer, Control source, Rect from, Rect to, ResolvedStyleMorph profile, CancellationToken token = default)
    {
        var bitmap = LyricsPageMotion.Capture(source, new(source.Bounds.Size));
        return bitmap is null ? Task.CompletedTask : RunAsync(layer, source, bitmap, from, to, profile, profile.CornerRadius, profile.CornerRadius, token);
    }
    public Task RunAsync(Grid layer, Control hidden, Bitmap snapshot, Rect from, Rect to, ResolvedStyleMorph profile, double startRadius, double endRadius, CancellationToken token = default)
    {
        Cancel();
        if (!InterfaceMotion.IsEnabled || !profile.Enabled || profile.DurationMs == 0 || token.IsCancellationRequested) { snapshot.Dispose(); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var image = new Image { Name = "NonetMorphImage", Source = snapshot, Stretch = Stretch.Fill };
        var geometry = new RectangleGeometry();
        var contentOpacity = new AnimatedOpacity();
        var overlay = new MorphOverlay { Name = "NonetMorphOverlay", Child = image, IsHitTestVisible = false, Clip = geometry,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ZIndex = 1000 };
        Grid.SetRowSpan(overlay, 100); Grid.SetColumnSpan(overlay, 100);
        var animationStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        IDisposable? hide = null, input = null; var registration = default(CancellationTokenRegistration); var finished = false;
        void Finish()
        {
            if (finished) return; finished = true; registration.Dispose();
            hide?.Dispose(); input?.Dispose(); layer.Children.Remove(overlay); image.Source = null; snapshot.Dispose();
            _cancel = null; completion.TrySetResult();
        }
        void Draw(double progress)
        {
            var sample = Sample(from, to, startRadius, endRadius, profile.Arc, progress);
            overlay.Width = sample.Bounds.Width; overlay.Height = sample.Bounds.Height;
            overlay.Margin = new(sample.Bounds.X, sample.Bounds.Y, 0, 0);
            overlay.CornerRadius = new(sample.Radius);
            geometry.Rect = new(sample.Bounds.Size); geometry.RadiusX = geometry.RadiusY = sample.Radius;
            // 末段交接到真实布局，避免快照中的文字被放大后突然跳回正常字号。
            var blend = Math.Clamp((progress - .65) / .35, 0, 1);
            contentOpacity.Set(blend); overlay.Opacity = 1 - blend;
        }
        try
        {
            Draw(0); layer.Children.Add(overlay);
            hide = hidden.Bind(Visual.OpacityProperty, contentOpacity, BindingPriority.Animation);
            input = hidden.Bind(Avalonia.Input.InputElement.IsHitTestVisibleProperty, new Constant(false), BindingPriority.Animation);
            overlay.Changed = Draw;
            _cancel = () => { animationStop.Cancel(); Finish(); };
            registration = token.Register(() => Dispatcher.UIThread.Post(Finish));
            _ = AnimateAsync();
        }
        catch { Finish(); throw; }
        return completion.Task;
        async Task AnimateAsync()
        {
            var animation = new Animation { Duration = TimeSpan.FromMilliseconds(profile.DurationMs), Easing = new LinearEasing(), Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(MorphOverlay.ProgressProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(MorphOverlay.ProgressProperty, 1d) } }
            } };
            try { await animation.RunAsync(overlay, animationStop.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { AppLog.Warning("Styles", "Morph animation skipped", error); }
            finally { overlay.Changed = null; if (Dispatcher.UIThread.CheckAccess()) Finish(); else await Dispatcher.UIThread.InvokeAsync(Finish); animationStop.Dispose(); }
        }
    }
    private sealed class MorphOverlay : Border
    {
        public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<MorphOverlay, double>("Progress", 1);
        public Action<double>? Changed { get; set; }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        { base.OnPropertyChanged(change); if (change.Property == ProgressProperty) Changed?.Invoke(change.GetNewValue<double>()); }
    }
    private sealed class Constant(object value) : IObservable<object>
    {
        public IDisposable Subscribe(IObserver<object> observer) { observer.OnNext(value); return new ElementLayoutService.ActionLease(() => { }); }
    }
    private sealed class AnimatedOpacity : IObservable<object>
    {
        private IObserver<object>? _observer;
        private double _value;
        public IDisposable Subscribe(IObserver<object> observer) { _observer = observer; observer.OnNext(_value); return new ElementLayoutService.ActionLease(() => _observer = null); }
        public void Set(double value) { _value = value; _observer?.OnNext(value); }
    }
    public void Cancel() => _cancel?.Invoke();
    public void Dispose() => Cancel();
}
