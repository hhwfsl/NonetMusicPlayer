using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>触摸和笔使用指针捕获缩放窗口，不进入只接受鼠标的系统缩放循环。</summary>
public sealed class TouchWindowResizer : IDisposable
{
    private readonly Window _window;
    private IPointer? _pointer;
    private PixelPoint _start, _origin;
    private Size _size;
    private WindowEdge _edge;
    private double _scale;
    public bool IsResizing => _pointer is not null;

    public TouchWindowResizer(Window window)
    {
        _window = window;
        window.AddHandler(InputElement.PointerPressedEvent, Press, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerMovedEvent, Move, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerReleasedEvent, Release, RoutingStrategies.Tunnel, true);
        window.PointerCaptureLost += CaptureLost;
        window.PropertyChanged += WindowChanged;
    }
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (_pointer is not null || !_window.CanResize || _window.WindowState != WindowState.Normal || e.Pointer.Type is not (PointerType.Touch or PointerType.Pen)) return;
        // 原生浮窗的事件会经过所属窗口，点击浮窗选项不能启动背后窗口的边缘缩放。
        if (e.Source is Control source)
        {
            if (TopLevel.GetTopLevel(source) != _window) return;
            // 扩大的边缘不能抢占标题按钮或贴边输入控件的正常触摸区域。
            if (source.GetVisualAncestors().Prepend(source).Any(v => v is Button or TextBox or ComboBox or Slider or ScrollBar)) return;
        }
        var point = e.GetPosition(_window);
        // 手指无需精确命中五像素装饰边框；角落比直边更宽，内侧按钮仍保留完整命中区。
        var edge = EdgeAt(point, _window.Bounds.Size);
        if (edge is null) return;
        _edge = edge.Value; _scale = _window.RenderScaling;
        _start = _window.PointToScreen(point); _origin = _window.Position; _size = _window.Bounds.Size;
        _pointer = e.Pointer; e.Pointer.Capture(_window); e.Handled = true;
    }
    public static WindowEdge? EdgeAt(Point point, Size size)
    {
        if (!new Rect(size).Contains(point)) return null;
        var left = point.X < 14; var right = point.X >= size.Width - 14;
        var top = point.Y < 14; var bottom = point.Y >= size.Height - 14;
        if ((left || top) && point.X < 24 && point.Y < 24) return WindowEdge.NorthWest;
        if ((right || top) && point.X >= size.Width - 24 && point.Y < 24) return WindowEdge.NorthEast;
        if ((left || bottom) && point.X < 24 && point.Y >= size.Height - 24) return WindowEdge.SouthWest;
        if ((right || bottom) && point.X >= size.Width - 24 && point.Y >= size.Height - 24) return WindowEdge.SouthEast;
        return left ? WindowEdge.West : right ? WindowEdge.East : top ? WindowEdge.North : bottom ? WindowEdge.South : null;
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _pointer) return;
        var current = _window.PointToScreen(e.GetPosition(_window));
        var delta = new Vector(current.X - _start.X, current.Y - _start.Y);
        var result = Resize(_origin, _size, _edge, delta, _scale,
            new(_window.MinWidth, _window.MinHeight), new(_window.MaxWidth, _window.MaxHeight));
        // 尺寸按逻辑像素提交，位置按物理像素提交；最小尺寸处固定对边，不随手指继续漂移。
        _window.Width = result.Size.Width; _window.Height = result.Size.Height;
        _window.Position = result.Position; e.Handled = true;
    }
    public static (PixelPoint Position, Size Size) Resize(PixelPoint origin, Size size, WindowEdge edge, Vector physicalDelta, double scale, Size minimum, Size maximum)
    {
        var left = edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
        var right = edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
        var top = edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
        var bottom = edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;
        var width = Math.Clamp(size.Width + (left ? -physicalDelta.X : right ? physicalDelta.X : 0) / scale, minimum.Width, Math.Max(minimum.Width, maximum.Width));
        var height = Math.Clamp(size.Height + (top ? -physicalDelta.Y : bottom ? physicalDelta.Y : 0) / scale, minimum.Height, Math.Max(minimum.Height, maximum.Height));
        var position = new PixelPoint(origin.X + (left ? (int)Math.Round((size.Width - width) * scale) : 0), origin.Y + (top ? (int)Math.Round((size.Height - height) * scale) : 0));
        return (position, new(width, height));
    }
    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _pointer) return;
        Cancel(); e.Handled = true;
    }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) { if (e.Pointer == _pointer) _pointer = null; }
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.CanResizeProperty || e.Property == Visual.IsVisibleProperty && !_window.IsVisible) Cancel();
    }
    private void Cancel() { var pointer = _pointer; _pointer = null; pointer?.Capture(null); }
    public void Dispose()
    {
        Cancel();
        _window.RemoveHandler(InputElement.PointerPressedEvent, Press); _window.RemoveHandler(InputElement.PointerMovedEvent, Move); _window.RemoveHandler(InputElement.PointerReleasedEvent, Release);
        _window.PointerCaptureLost -= CaptureLost; _window.PropertyChanged -= WindowChanged;
    }
}
