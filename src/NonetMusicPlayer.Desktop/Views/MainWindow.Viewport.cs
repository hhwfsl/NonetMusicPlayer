using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private int _viewportRequest;
    private bool _windowInactive;

    /// <summary>焦点恢复不代表导航，不应把旧输入控件滚动到视口中。</summary>
    private void InitializeViewportContinuity()
    {
        ScrollViewer.SetBringIntoViewOnFocusChange(this, false);
        Activated += (_, _) =>
        {
            _windowInactive = false;
            // 立即采样真实播放状态；普通页保持现有视口，歌词页使用当前时钟重新居中。
            _vm?.RefreshPlaybackState();
            if (FullLyricsHost.Content is LyricsView lyrics) lyrics.RestorePlaybackFocus();
        };
        Deactivated += (_, _) => { _windowInactive = true; _locateTimer.Stop(); ++_locateRequest; };
        AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (_, _) => ++_viewportRequest, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, _) => ++_viewportRequest, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        // 显式导航仍按原规范定位或置顶；激活事件不修改普通页面视口。
    }
}
