using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>仅播放有限时长的微动画，不参与歌词、进度和长列表布局，不创建后台定时器。</summary>
public sealed class InterfaceMotion : IDisposable
{
    private CancellationTokenSource? _running;
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
            button.AddHandler(InputElement.PointerPressedEvent, (_, _) => { transform.ScaleX = .94; transform.ScaleY = .94; }, RoutingStrategies.Tunnel);
            button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { transform.ScaleX = 1; transform.ScaleY = 1; }, RoutingStrategies.Tunnel, true);
            button.PointerCaptureLost += (_, _) => { transform.ScaleX = 1; transform.ScaleY = 1; };
            button.PointerExited += (_, _) => { transform.ScaleX = 1; transform.ScaleY = 1; };
        }
    }
    public static bool IsEnabled => Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION") != "1"
        && (!OperatingSystem.IsWindows() || !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled);
    public void Enter(Control control)
    {
        Cancel();
        if (!IsEnabled) return;
        _running = new(); _ = Run(control, _running.Token);
    }
    private static async Task Run(Control control, CancellationToken cancellationToken)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, .84) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
            }
        };
        try { await animation.RunAsync(control, cancellationToken); }
        catch (OperationCanceledException) { }
        // 动画失败不影响导航；不写入本地 Opacity，以免覆盖插件或主题绑定。
        catch (Exception error) { AppLog.Warning("UI", "Page animation skipped", error); }
    }
    public void Cancel() { _running?.Cancel(); _running?.Dispose(); _running = null; }
    public void Dispose() => Cancel();
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
