using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>只移动可见集合做让位预览；业务顺序由投放方提交，取消时恢复，外部刷新优先。</summary>
public sealed class TrackReorderPreview : IDisposable
{
    private readonly ListBox _list;
    private readonly ObservableCollection<TrackItem> _items;
    private readonly TrackItem[] _original, _moving;
    private TrackItem[] _presented;
    private readonly HashSet<string> _ids;
    private readonly TrackDragVisual _visual;
    private readonly DispatcherTimer _scroll = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Point _point;
    private bool _disposed;
    public string? TargetId { get; private set; }
    public bool Before { get; private set; }
    public bool IsValid => !_disposed && _items.SequenceEqual(_presented);
    public IReadOnlyList<string> OriginalIds => _original.Select(t => t.Id).ToArray();

    public TrackReorderPreview(ListBox list, ObservableCollection<TrackItem> items, IEnumerable<string> movingIds, Point? grabPoint = null)
    {
        _list = list; _items = items; _original = _presented = items.ToArray();
        _ids = movingIds.ToHashSet(StringComparer.Ordinal); _moving = _original.Where(t => _ids.Contains(t.Id)).ToArray();
        _visual = new(list, _ids, grabPoint);
        _scroll.Tick += (_, _) =>
        {
            if (!IsValid) { Dispose(); return; }
            _visual.UpdatePlaceholders();
            var viewer = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (viewer is null || _point.X < 0 || _point.X > _list.Bounds.Width) return;
            var speed = _point.Y < 32 ? -Math.Clamp((32 - _point.Y) * .35, 0, 18)
                : _point.Y > _list.Bounds.Height - 32 ? Math.Clamp((_point.Y - _list.Bounds.Height + 32) * .35, 0, 18) : 0;
            if (speed == 0) return;
            var y = Math.Clamp(viewer.Offset.Y + speed, 0, Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height));
            if (Math.Abs(y - viewer.Offset.Y) < .1) return;
            viewer.Offset = new(viewer.Offset.X, y); _list.UpdateLayout(); Preview(_point);
        };
    }

    public void Preview(Point point)
    {
        _point = point;
        if (!IsValid || _moving.Length == 0) return;
        _visual.Show(point);
        _scroll.Start();
        var rows = _list.GetRealizedContainers().OfType<ListBoxItem>().Where(r => r.DataContext is TrackItem t && !_ids.Contains(t.Id))
            .OrderBy(r => LayoutTop(r)).ToArray();
        if (rows.Length == 0) return;
        var target = rows.FirstOrDefault(r => point.Y < LayoutTop(r) + r.Bounds.Height / 2);
        Before = target is not null; target ??= rows[^1]; TargetId = ((TrackItem)target.DataContext!).Id;
        var next = _original.Where(t => !_ids.Contains(t.Id)).ToList();
        var index = next.FindIndex(t => t.Id == TargetId) + (Before ? 0 : 1);
        next.InsertRange(index, _moving);
        if (_items.SequenceEqual(next)) return;
        // 先保存呈现位置再释放旧动画；换位后的新容器从该位置继续让位。
        var positions = _visual.CapturePositions(); _visual.PrepareLayout();
        ApplyOrder(next);
        _presented = _items.ToArray(); _list.UpdateLayout();
        _visual.AnimatePlacement(positions);
    }
    private double LayoutTop(ListBoxItem row) => _visual.LayoutTop(row);

    public void Restore()
    {
        _scroll.Stop(); TargetId = null; _visual.Reset();
        if (!IsValid) return;
        ApplyOrder(_original);
        _presented = _items.ToArray();
    }
    private void ApplyOrder(IReadOnlyList<TrackItem> order)
    {
        // 只移动被拖动的歌曲；长列表跨越许多行时不会逐行搬动全部未选中项目。
        var positions = order.Select((track, index) => (track, index)).ToDictionary(p => p.track, p => p.index);
        foreach (var track in _moving.Reverse())
        {
            var from = _items.IndexOf(track); var position = positions[track];
            var anchor = position + 1 < order.Count ? _items.IndexOf(order[position + 1]) : _items.Count;
            var to = anchor - (from < anchor ? 1 : 0);
            if (from != to) _items.Move(from, to);
        }
    }
    public void Dispose() { if (_disposed) return; Restore(); _disposed = true; _scroll.Stop(); _visual.Dispose(); }
}
