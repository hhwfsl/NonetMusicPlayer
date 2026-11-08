using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>拖动虚影、占位和让位动画共用一份生命周期；动画始终从当前呈现位置接续。</summary>
internal sealed class TrackDragVisual : IDisposable
{
    private readonly ListBox _list;
    private readonly HashSet<string> _ids;
    private readonly Point? _grabPoint;
    private readonly RetargetableMotion _placement = new();
    private readonly Dictionary<ListBoxItem, RowMotion> _rows = [];
    private readonly Dictionary<ListBoxItem, (string Id, IDisposable Lease)> _placeholders = [];
    private Canvas? _layer;
    private Panel? _parent;
    private Border? _ghost;
    private Control? _content;
    private Vector _grabOffset;

    public TrackDragVisual(ListBox list, HashSet<string> ids, Point? grabPoint)
    {
        _list = list; _ids = ids; _grabPoint = grabPoint;
        _placement.Changed += DrawPlacement;
    }
    public Dictionary<string, double> CapturePositions() => _list.GetRealizedContainers().OfType<ListBoxItem>()
        .Where(r => r.DataContext is TrackItem).ToDictionary(r => ((TrackItem)r.DataContext!).Id, r => r.TranslatePoint(default, _list)!.Value.Y);

    public double LayoutTop(ListBoxItem row) => (row.TranslatePoint(default, _list)?.Y ?? 0)
        - (_rows.TryGetValue(row, out var motion) && row.DataContext is TrackItem t && t.Id == motion.Id ? motion.Transform.Y : 0);

    public void PrepareLayout()
    {
        _placement.Cancel();
        foreach (var motion in _rows.Values) motion.Lease.Dispose();
        _rows.Clear();
    }
    public void AnimatePlacement(Dictionary<string, double> positions)
    {
        // 显式时钟写入渲染偏移，避免新建 Transition 在首帧前直接跳到最终值。
        foreach (var row in _list.GetRealizedContainers().OfType<ListBoxItem>())
        {
            if (!InterfaceMotion.IsEnabled || row.DataContext is not TrackItem track || _ids.Contains(track.Id)
                || !positions.TryGetValue(track.Id, out var old)) continue;
            var offset = old - (row.TranslatePoint(default, _list)?.Y ?? old);
            if (Math.Abs(offset) < .1) continue;
            var translation = new TranslateTransform(0, offset);
            var group = new TransformGroup();
            if (row.RenderTransform is { } original) group.Children.Add(original as Transform ?? new MatrixTransform(original.Value));
            group.Children.Add(translation);
            var lease = row.Bind(Visual.RenderTransformProperty, new Constant(group), BindingPriority.Animation);
            _rows[row] = new(track.Id, translation, offset, lease);
        }
        _ = _placement.MoveAsync(0, 0);
        _ = _placement.MoveAsync(1, 210);
        UpdatePlaceholders();
    }
    private void DrawPlacement(double progress)
    {
        foreach (var (row, motion) in _rows.ToArray())
        {
            if (row.DataContext is not TrackItem track || track.Id != motion.Id || progress >= 1)
            { motion.Lease.Dispose(); _rows.Remove(row); }
            else motion.Transform.Y = motion.From * (1 - progress);
        }
    }
    public void Show(Point point)
    {
        if (_ghost is null) CreateGhost(point);
        if (_ghost is null || _layer is null) return;
        if (_layer.Parent is null) _parent!.Children.Add(_layer);
        var position = _list.TranslatePoint(point, _layer) ?? default;
        Canvas.SetLeft(_ghost, Math.Clamp(position.X - _grabOffset.X, 8, Math.Max(8, _parent!.Bounds.Width - _ghost.Width - 8)));
        Canvas.SetTop(_ghost, position.Y - _grabOffset.Y);
        UpdatePlaceholders();
    }
    private void CreateGhost(Point point)
    {
        var rows = _list.GetRealizedContainers().OfType<ListBoxItem>().Where(r => r.DataContext is TrackItem t && _ids.Contains(t.Id)).ToArray();
        var row = _grabPoint is { } grab ? rows.FirstOrDefault(r => grab.Y >= LayoutTop(r) && grab.Y < LayoutTop(r) + r.Bounds.Height) : null;
        row ??= rows.FirstOrDefault();
        if (row?.DataContext is not TrackItem track || row.Bounds.Width < 1 || row.Bounds.Height < 1
            || (TopLevel.GetTopLevel(_list) as Window)?.Content is not Panel parent) return;
        var top = row.TranslatePoint(default, _list) ?? default;
        var anchor = _grabPoint ?? new Point(point.X, top.Y + row.Bounds.Height / 2);
        _grabOffset = new(Math.Clamp(anchor.X - top.X, 0, row.Bounds.Width), Math.Clamp(anchor.Y - top.Y, 0, row.Bounds.Height));
        // 复用原行模板实时绘制一份副本，避免位图缩放的字形伪影；只生成一行，不复制音频或封面缓存。
        var content = _list.ItemTemplate?.Build(track) ?? new TextBlock { Text = track.Title };
        content.Name = "DraggedTrackGhostContent"; content.DataContext = track; _content = content;
        var body = new Grid { Children = { content } };
        if (_ids.Count > 1)
        {
            var badge = new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new(8), Padding = new(7, 2), CornerRadius = new(8), Child = new TextBlock { Text = "×" + _ids.Count, Foreground = Brushes.White } };
            badge.Bind(Border.BackgroundProperty, _list.GetResourceObservable("AccentBrush")); body.Children.Add(badge);
        }
        _ghost = new Border { Name = "DraggedTrackGhost", DataContext = track, Width = row.Bounds.Width, Height = row.Bounds.Height,
            Opacity = .78, CornerRadius = new(12), ClipToBounds = true, Child = body, IsHitTestVisible = false,
            BorderThickness = new(1), BoxShadow = BoxShadows.Parse("0 6 20 0 #38000000"),
            RenderTransform = new ScaleTransform(1.015, 1.015), RenderTransformOrigin = RelativePoint.Center };
        _ghost.Bind(Border.BackgroundProperty, _list.GetResourceObservable("SurfaceBrush"));
        _ghost.Bind(Border.BorderBrushProperty, _list.GetResourceObservable("DividerBrush"));
        _parent = parent;
        _layer = new Canvas { Name = "TrackDragOverlay", IsHitTestVisible = false, ClipToBounds = true, ZIndex = 3500, Children = { _ghost } };
        Grid.SetRowSpan(_layer, 100); Grid.SetColumnSpan(_layer, 100);
        parent.Children.Add(_layer);
    }
    public void UpdatePlaceholders()
    {
        foreach (var (row, placeholder) in _placeholders.ToArray())
            if (row.DataContext is not TrackItem track || track.Id != placeholder.Id || !_ids.Contains(track.Id))
            { placeholder.Lease.Dispose(); _placeholders.Remove(row); }
        foreach (var row in _list.GetRealizedContainers().OfType<ListBoxItem>())
            if (row.DataContext is TrackItem track && _ids.Contains(track.Id) && !_placeholders.ContainsKey(row))
                _placeholders[row] = (track.Id, row.Bind(Visual.OpacityProperty, new Constant(.12d), BindingPriority.Animation));
    }
    public void Reset()
    {
        PrepareLayout();
        foreach (var placeholder in _placeholders.Values) placeholder.Lease.Dispose();
        _placeholders.Clear();
        if (_layer is not null) _parent?.Children.Remove(_layer);
    }
    public void Dispose()
    {
        Reset(); _placement.Dispose();
        if (_content is not null) _content.DataContext = null;
        if (_ghost is not null) { _ghost.Child = null; _ghost.DataContext = null; }
        _layer?.Children.Clear(); _content = null; _ghost = null; _layer = null; _parent = null;
    }
    private sealed record RowMotion(string Id, TranslateTransform Transform, double From, IDisposable Lease);
    private sealed class Constant(object value) : IObservable<object>
    {
        public IDisposable Subscribe(IObserver<object> observer) { observer.OnNext(value); return new ElementLayoutService.ActionLease(() => { }); }
    }
}
