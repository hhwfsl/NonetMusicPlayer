using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly ObservableCollection<TrackItem> _queueRows = [];
    private IPointer? _queueDragPointer;
    private IPointer? _queuePendingPointer;
    private readonly DispatcherTimer _queueHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    private TrackItem? _queueDragTrack;
    private Point _queueDragStart;
    private TrackReorderPreview? _queueReorder;
    private bool _suppressQueueTap;

    private void InitializeQueueDrag()
    {
        _queueHoldTimer.Tick += (_, _) =>
        {
            _queueHoldTimer.Stop();
            if (!_queueOpen || _queuePendingPointer is not { } pointer || _queueDragTrack is null) return;
            _queuePendingPointer = null; _queueDragPointer = pointer; _suppressQueueTap = true;
            pointer.Capture(_queueList);
            _queueReorder = new TrackReorderPreview(_queueList!, _queueRows, [_queueDragTrack.Id], _queueDragStart);
            _queueReorder.Preview(_queueDragStart);
        };
        _queueList!.AddHandler(PointerPressedEvent, (_, e) =>
        {
            _suppressQueueTap = false;
            if (!e.GetCurrentPoint(_queueList).Properties.IsLeftButtonPressed) return;
            var ancestors = Ancestors(e.Source).OfType<Control>().ToArray();
            if (ancestors.OfType<ListBoxItem>().FirstOrDefault()?.DataContext is not TrackItem track) return;
            CancelQueueDrag(); _queueDragTrack = track; _queueDragStart = e.GetPosition(_queueList);
            // 去掉独立手柄后，触摸/笔长按整行拾起；提前移动仍交给原生列表滚动。
            if (e.Pointer.Type != PointerType.Mouse) { _queuePendingPointer = e.Pointer; _queueHoldTimer.Start(); return; }
            _suppressQueueTap = true; _queueDragPointer = e.Pointer;
            e.Pointer.Capture(_queueList); e.Handled = true;
        }, RoutingStrategies.Tunnel, true);
        _queueList.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_queuePendingPointer == e.Pointer && DistanceFromQueuePress(e.GetPosition(_queueList)) >= 7)
            { _queuePendingPointer = null; _queueDragTrack = null; _queueHoldTimer.Stop(); return; }
            if (_queueDragPointer != e.Pointer || _queueDragTrack is null) return;
            var point = e.GetPosition(_queueList);
            if (_queueReorder is null && DistanceFromQueuePress(point) < 7) return;
            _queueReorder ??= new TrackReorderPreview(_queueList, _queueRows, [_queueDragTrack.Id], _queueDragStart);
            _queueReorder.Preview(point); e.Handled = true;
        }, RoutingStrategies.Tunnel, true);
        _queueList.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_queuePendingPointer == e.Pointer) { _queuePendingPointer = null; _queueDragTrack = null; _queueHoldTimer.Stop(); return; }
            if (_queueDragPointer != e.Pointer || e.GetCurrentPoint(_queueList).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(_queueList); var preview = _queueReorder; var track = _queueDragTrack;
            var inside = new Rect(_queueList.Bounds.Size).Contains(point);
            var expected = preview?.OriginalIds; var ordered = _queueRows.Select(t => t.Id).ToArray(); var valid = preview?.IsValid == true;
            CancelQueueDrag(); e.Handled = true;
            if (preview is not null) { if (inside && valid) _vm?.ReorderPlaybackQueue(expected!, ordered); RefreshQueue(); }
            else if (inside && track is not null && DistanceFromQueuePress(point) < 7) PlayQueueSelection(track);
        }, RoutingStrategies.Tunnel, true);
        _queueList.PointerCaptureLost += (_, e) => { if (_queueDragPointer == e.Pointer && e.Pointer.Captured != _queueList) CancelQueueDrag(); };
    }
    private void CancelQueueDrag()
    {
        _queueHoldTimer.Stop(); _queuePendingPointer = null;
        var pointer = _queueDragPointer; _queueDragPointer = null; _queueDragTrack = null;
        _queueReorder?.Dispose(); _queueReorder = null;
        if (pointer is not null && ReferenceEquals(pointer.Captured, _queueList)) pointer.Capture(null);
    }
    private double DistanceFromQueuePress(Point point) => Math.Abs(point.X - _queueDragStart.X) + Math.Abs(point.Y - _queueDragStart.Y);
}
