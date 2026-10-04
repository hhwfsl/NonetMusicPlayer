using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Controls;
public sealed class WorkspacePanel : Panel
{
    public AppSettings Profile { get; set; } = new();
    public string Scope { get; set; } = "Workspace";
    public bool Editing { get; private set; }
    public event EventHandler? LayoutChanged;
    private Point? _edgeStart;
    private bool _resizingNavigation;
    private double _originalExtent;
    private readonly Cursor _verticalCursor = new(StandardCursorType.SizeNorthSouth), _horizontalCursor = new(StandardCursorType.SizeWestEast);
    public WorkspacePanel()
    {
        AddHandler(PointerPressedEvent, EdgePressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, EdgeMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_edgeStart is null) return;
            _edgeStart = null; e.Pointer.Capture(null); Cursor = null; e.Handled = true;
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => { _edgeStart = null; Cursor = null; };
    }
    private bool NavigationEdgeAt(Point point, double tolerance)
    {
        var nav = RectFor("Navigation", Bounds.Size);
        return point.Y >= nav.Top && point.Y <= nav.Bottom && Math.Abs(point.X - (Profile.NavigationRight ? nav.Left : nav.Right)) <= tolerance;
    }
    private void EdgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Editing || e.Source is Visual source && (source is PlayerProgressSlider || source.GetVisualAncestors().OfType<PlayerProgressSlider>().Any()) || e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (!NavigationEdgeAt(point, e.Pointer.Type == PointerType.Touch ? 12 : 5)) return;
        _edgeStart = point; _resizingNavigation = true;
        _originalExtent = RectFor("Navigation", Bounds.Size).Width;
        e.Pointer.Capture(this); e.Handled = true;
    }
    public void BeginPlayerResize(PointerPressedEventArgs e)
    {
        if (Editing || e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _edgeStart = e.GetPosition(this); _resizingNavigation = false;
        _originalExtent = RectFor("Player", Bounds.Size).Height;
        Cursor = _verticalCursor; e.Pointer.Capture(this); e.Handled = true;
    }
    private void EdgeMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_edgeStart is not { } start)
        {
            Cursor = NavigationEdgeAt(point, 5) ? _horizontalCursor : null;
            return;
        }
        if (_resizingNavigation)
            Profile.SidebarWidth = Math.Clamp(_originalExtent + (point.X - start.X) * (Profile.NavigationRight ? -1 : 1), 170, Math.Min(320, Bounds.Width * .4));
        else Profile.PlayerHeight = Math.Clamp(_originalExtent + (point.Y - start.Y) * (Profile.PlayerTop ? 1 : -1), Bounds.Width < 640 ? 128 : Profile.TouchMode ? 110 : 96, 180);
        // 拖动边界恢复相邻区域标准布局，播放器内部嵌套 JSON 保留。
        Profile.Layout.Clear(); InvalidateMeasure(); e.Handled = true;
    }
    public void SetEditing(bool editing)
    {
        Editing = editing; foreach (var widget in Children.OfType<LayoutWidget>()) { widget.Editing = editing; widget.InvalidateVisual(); }
        InvalidateArrange();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var widget in Children.OfType<LayoutWidget>()) widget.Measure(RectFor(widget.Key, availableSize).Size);
        return availableSize;
    }
    public Rect RectFor(string key, Size size)
    {
        if (Profile.Layout.TryGetValue(key, out var custom) && custom.IsValid)
            return new Rect(custom.X * size.Width, custom.Y * size.Height, custom.Width * size.Width, custom.Height * size.Height);
        if (Scope == "Player")
        {
            var info = size.Width < 1050 ? 240d : 280d; var options = size.Width < 1050 ? 228d : 260d;
            return key switch { "Player.Info" => new Rect(0, 0, info, size.Height), "Player.Transport" => new Rect(info, 0, Math.Max(1, size.Width - info - options), size.Height), _ => new Rect(Math.Max(0, size.Width - options), 0, options, size.Height) };
        }
        var player = Math.Min(Math.Max(Profile.PlayerHeight, size.Width < 640 ? 128 : Profile.TouchMode ? 110 : 96), size.Height * .4);
        var sidebar = Math.Min(size.Width < 640 ? 170 : Profile.SidebarWidth, size.Width * .4);
        var bodyY = Profile.PlayerTop ? player : 0;
        return key switch
        {
            "Navigation" => new Rect(Profile.NavigationRight ? size.Width - sidebar : 0, bodyY, sidebar, size.Height - player),
            "Content" => new Rect(Profile.NavigationRight ? 0 : sidebar, bodyY, size.Width - sidebar, size.Height - player),
            _ => new Rect(0, Profile.PlayerTop ? 0 : size.Height - player, size.Width, player)
        };
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var widget in Children.OfType<LayoutWidget>()) widget.Arrange(RectFor(widget.Key, finalSize)); return finalSize;
    }
    public void Move(string key, Rect proposed)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var minimum = key switch { "Navigation" => new Size(170, 260), "Player" => new Size(Math.Min(740, Bounds.Width), 108), "Player.Info" => new Size(180, 64), "Player.Transport" => new Size(230, 76), "Player.Options" => new Size(180, 76), _ => new Size(Math.Min(660, Bounds.Width), 300) };
        var width = Math.Clamp(proposed.Width, Math.Min(minimum.Width, Bounds.Width), Bounds.Width);
        var height = Math.Clamp(proposed.Height, Math.Min(minimum.Height, Bounds.Height), Bounds.Height);
        var x = Math.Clamp(proposed.X, 0, Bounds.Width - width); var y = Math.Clamp(proposed.Y, 0, Bounds.Height - height);
        Profile.Layout[key] = new(x / Bounds.Width, y / Bounds.Height, width / Bounds.Width, height / Bounds.Height);
        InvalidateMeasure(); LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
public sealed class LayoutWidget : Grid
{
    public string Key { get; set; } = "Content";
    public string Label { get; set; } = L10n.T("Common.Content");
    public bool Editing { get => _overlay.Editing; set { _overlay.Editing = value; _overlay.Label = Label; _overlay.InvalidateVisual(); } }
    private readonly LayoutOverlay _overlay = new();
    private Control? _child;
    public Control? Child { get => _child; set { if (_child is not null) Children.Remove(_child); _child = value; if (value is not null) Children.Insert(0, value); } }
    private Point? _start;
    private Rect _origin;
    private bool _resize;
    public LayoutWidget()
    {
        ClipToBounds = true;
        Children.Add(_overlay);
        AddHandler(PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, Move, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Release, RoutingStrategies.Tunnel);
    }
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (!Editing || Parent is not WorkspacePanel panel || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this); _resize = point.X >= Bounds.Width - 24 && point.Y >= Bounds.Height - 24;
        if (!_resize && point.Y > 26) return;
        _start = e.GetPosition(panel); _origin = Bounds; e.Pointer.Capture(this); e.Handled = true;
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        if (_start is not { } start || Parent is not WorkspacePanel panel) return;
        var delta = e.GetPosition(panel) - start;
        panel.Move(Key, _resize ? new(_origin.X, _origin.Y, _origin.Width + delta.X, _origin.Height + delta.Y) : new(_origin.X + delta.X, _origin.Y + delta.Y, _origin.Width, _origin.Height)); e.Handled = true;
    }
    private void Release(object? sender, PointerReleasedEventArgs e) { if (_start is null) return; _start = null; e.Pointer.Capture(null); e.Handled = true; }
}
internal sealed class LayoutOverlay : Control
{
    public bool Editing { get; set; }
    public string Label { get; set; } = "";
    public LayoutOverlay() { IsHitTestVisible = false; ZIndex = 10; }
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (!Editing) return;
        context.DrawRectangle(null, new Pen(Brushes.MediumPurple, 2), new Rect(1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2)));
        context.DrawRectangle(Brushes.MediumPurple, null, new Rect(0, 0, Bounds.Width, 26));
        context.DrawText(new FormattedText(Label + " · 拖动此处，右下角缩放", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 12, Brushes.White), new Point(8, 4));
        context.DrawRectangle(Brushes.MediumPurple, null, new Rect(Math.Max(0, Bounds.Width - 24), Math.Max(0, Bounds.Height - 24), 24, 24));
    }
}
