using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>有限动画共用当前呈现值；每次改向使用独立时钟，旧绑定释放不会回写新动画。</summary>
public sealed class RetargetableMotion : IDisposable
{
    private CancellationTokenSource? _stop;
    private FrameClock? _clock;
    public double Value { get; private set; }
    public bool IsRunning => _stop is not null;
    public event Action<double>? Changed;
    public Task MoveAsync(double target, int durationMs = 300)
    {
        Dispatcher.UIThread.VerifyAccess(); Cancel();
        var from = Value;
        if (durationMs <= 0 || !InterfaceMotion.IsEnabled || Math.Abs(target - from) < .001)
        { Value = target; Changed?.Invoke(Value); return Task.CompletedTask; }
        var stop = new CancellationTokenSource(); _stop = stop;
        var clock = new FrameClock(); _clock = clock;
        clock.Changed = progress => { Value = from + (target - from) * progress; Changed?.Invoke(Value); };
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(Math.Max(60, durationMs * Math.Abs(target - from))),
            Easing = new QuadraticEaseOut(), Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(FrameClock.ProgressProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(FrameClock.ProgressProperty, 1d) } }
            } };
        return Run();
        async Task Run()
        {
            try { await animation.RunAsync(clock, stop.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { AppLog.Warning("UI", "浮层动画已交接到目标状态", error); }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    clock.Changed = null;
                    if (ReferenceEquals(_stop, stop))
                    { _stop = null; _clock = null; Value = target; Changed?.Invoke(Value); }
                });
                stop.Dispose();
            }
        }
    }
    public void Cancel()
    {
        Dispatcher.UIThread.VerifyAccess();
        var stop = _stop; _stop = null;
        if (_clock is not null) _clock.Changed = null;
        _clock = null; stop?.Cancel();
    }
    public void Dispose() { Cancel(); Changed = null; }
    private sealed class FrameClock : Animatable
    {
        public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<FrameClock, double>("Progress", 1);
        public Action<double>? Changed;
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        { base.OnPropertyChanged(change); if (change.Property == ProgressProperty) Changed?.Invoke(change.GetNewValue<double>()); }
    }
}
