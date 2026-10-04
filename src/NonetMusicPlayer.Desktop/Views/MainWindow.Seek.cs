using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private double _pendingSeek;
    private string? _seekTrackId;
    private void InitializeSeekInteraction()
    {
        // 滑块会处理指针事件，因此也观察已处理事件，确保释放时提交进度。
        PlaybackSlider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(PlaybackSlider).Properties.IsLeftButtonPressed) BeginSeek();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlaybackSlider.AddHandler(PointerReleasedEvent, (_, _) => CommitSeek(), RoutingStrategies.Bubble, handledEventsToo: true);
        PlaybackSlider.PointerCaptureLost += (_, _) => CommitSeek();
        PlaybackSlider.AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) BeginSeek(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        PlaybackSlider.AddHandler(KeyUpEvent, (_, _) => CommitSeek(), RoutingStrategies.Bubble, handledEventsToo: true);
        PlaybackSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && _vm?.IsSeeking == true) _pendingSeek = PlaybackSlider.Value;
        };
        PlaybackSlider.LostFocus += (_, _) => CommitSeek();
    }
    private void BeginSeek()
    {
        if (_vm?.CurrentTrack is null || _vm.IsSeeking) return;
        _seekTrackId = _vm.CurrentTrack.Id; _pendingSeek = PlaybackSlider.Value; _vm.IsSeeking = true;
    }
    private void CommitSeek()
    {
        if (_vm?.IsSeeking != true) return;
        _vm.IsSeeking = false;
        var sameTrack = _seekTrackId == _vm.CurrentTrack?.Id; _seekTrackId = null;
        if (sameTrack) _vm.Seek(_pendingSeek);
    }
}
