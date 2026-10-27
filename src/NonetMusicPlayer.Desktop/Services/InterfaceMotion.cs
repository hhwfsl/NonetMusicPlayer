using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>仅播放有限时长的微动画，不参与歌词、进度和长列表布局，不创建后台定时器。</summary>
public sealed class InterfaceMotion : IDisposable
{
    private Action? _cancel;
    private int _generation;
    public bool IsRunning => _cancel is not null;
    /// <summary>只对三个播放按钮施加渲染缩放，不改变排版尺寸或扩大命中区域。</summary>
    public static void InstallTransport(params Button[] buttons)
    {
        if (!IsEnabled) return;
        foreach (var button in buttons)
        {
            var transform = new ScaleTransform(1, 1) { Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(90) },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(90) }
            } };
            button.RenderTransform = transform; button.RenderTransformOrigin = RelativePoint.Center;
            void Scale(double value) { var target = StyleLayerService.OwnsMotion(button) ? 1 : value; transform.ScaleX = target; transform.ScaleY = target; }
            button.AddHandler(InputElement.PointerPressedEvent, (_, _) => Scale(.94), RoutingStrategies.Tunnel);
            button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Scale(1), RoutingStrategies.Tunnel, true);
            button.PointerCaptureLost += (_, _) => Scale(1);
            button.PointerExited += (_, _) => Scale(1);
        }
    }
    public static bool IsEnabled => Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION") != "1"
        && (!OperatingSystem.IsWindows() || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled);
    public void Enter(Control control)
        => Enter(control, 140);
    public void Enter(Control control, int durationMs)
        => EnterPage(control, new ResolvedPageTransition(true, "fade", Math.Clamp(durationMs, 0, 1000), 0, 0, .84, "cubic-out"));
    /// <summary>单个动画时钟驱动渲染属性，不重新挂接原生页面或改变布局尺寸；取消立即释放覆盖。</summary>
    public void EnterPage(Control control, ResolvedPageTransition profile)
    {
        Cancel();
        if (!IsEnabled || !profile.Enabled || profile.DurationMs <= 0 || profile.Effect == "none") return;
        var generation = ++_generation;
        var stop = new CancellationTokenSource();
        var clock = new PageClock(); var finished = false;
        var opacity = control.Opacity; var originalTransform = control.RenderTransform;
        var translate = new MatrixTransform();
        var opacityChannel = new ValueChannel(opacity); var transformChannel = new ValueChannel(translate);
        IDisposable? opacityLease = null, transformLease = null;
        void Finish()
        {
            if (finished) return; finished = true;
            clock.Changed = null; opacityLease?.Dispose(); transformLease?.Dispose();
            if (_generation == generation) _cancel = null;
        }
        void Draw(double progress)
        {
            if (finished) return;
            opacityChannel.Set(opacity * (profile.StartOpacity + (1 - profile.StartOpacity) * progress));
            translate.Matrix = (originalTransform?.Value ?? Matrix.Identity) * Matrix.CreateTranslation(profile.OffsetX * (1 - progress), profile.OffsetY * (1 - progress));
        }
        try
        {
            Draw(0);
            if (profile.Effect is "fade" or "fade-slide") opacityLease = control.Bind(Visual.OpacityProperty, opacityChannel, BindingPriority.Animation);
            if (profile.Effect is "slide" or "fade-slide") transformLease = control.Bind(Visual.RenderTransformProperty, transformChannel, BindingPriority.Animation);
            clock.Changed = Draw;
            _cancel = () => { stop.Cancel(); Finish(); };
            _ = Run();
        }
        catch (Exception error) { Finish(); stop.Dispose(); AppLog.Warning("UI", "Page animation skipped", error); }
        async Task Run()
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(Math.Clamp(profile.DurationMs, 0, 1000)),
                Easing = profile.Easing switch { "linear" => new LinearEasing(), "parabolic" => new QuadraticEaseOut(), _ => new CubicEaseOut() },
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(PageClock.ProgressProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(PageClock.ProgressProperty, 1d) } }
                }
            };
            try { await animation.RunAsync(clock, stop.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { AppLog.Warning("UI", "Page animation skipped", error); }
            finally { Finish(); stop.Dispose(); }
        }
    }
    public void Cancel() => _cancel?.Invoke();
    public void Dispose() => Cancel();
    private sealed class PageClock : Animatable
    {
        // 动画绑定结束后会露出基础值；基础值必须是完成态，避免清理前短暂回弹到起点。
        public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<PageClock, double>("Progress", 1d);
        public Action<double>? Changed { get; set; }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        { base.OnPropertyChanged(change); if (change.Property == ProgressProperty) Changed?.Invoke(change.GetNewValue<double>()); }
    }
    private sealed class ValueChannel(object value) : IObservable<object>
    {
        private IObserver<object>? _observer;
        public IDisposable Subscribe(IObserver<object> observer) { _observer = observer; observer.OnNext(value); return new ElementLayoutService.ActionLease(() => _observer = null); }
        public void Set(object next) { value = next; _observer?.OnNext(next); }
    }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
