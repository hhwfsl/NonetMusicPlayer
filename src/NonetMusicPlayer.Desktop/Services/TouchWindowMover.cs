using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NonetMusicPlayer.Desktop.Services;

// Windows 原生拖动启动鼠标循环；触摸和手写笔使用独立捕获路径，以物理像素移动。
public sealed class TouchWindowMover : IDisposable
{
    private readonly Window _window;
    private IPointer? _pointer;
    private PixelPoint _start, _origin;
    private double _grabRatio;
    private bool _restoring;
    private readonly bool _allowMouse;
    private readonly Func<PixelPoint, PixelPoint, PixelPoint>? _constrain;
    private readonly Action? _completed;
    public bool IsDragging => _pointer is not null;
    public TouchWindowMover(Window window, bool allowMouse = false, Func<PixelPoint, PixelPoint, PixelPoint>? constrain = null, Action? completed = null)
    {
        _window = window; _allowMouse = allowMouse; _constrain = constrain; _completed = completed;
        window.AddHandler(InputElement.PointerMovedEvent, Move, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerReleasedEvent, Release, RoutingStrategies.Tunnel, true);
        window.PointerCaptureLost += CaptureLost;
    }
    public bool TryBegin(PointerPressedEventArgs e)
    {
        if (_pointer is not null || e.Pointer.Type is not (PointerType.Touch or PointerType.Pen) && !(_allowMouse && e.Pointer.Type == PointerType.Mouse && e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)) return false;
        _pointer = e.Pointer; _start = _window.PointToScreen(e.GetPosition(_window)); _origin = _window.Position;
        _grabRatio = Math.Clamp(e.GetPosition(_window).X / Math.Max(1, _window.Bounds.Width), 0, 1);
        _restoring = _window.WindowState == WindowState.Maximized;
        e.Pointer.Capture(_window); e.Handled = true; return true;
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _pointer) return;
        var point = _window.PointToScreen(e.GetPosition(_window));
        if (_restoring)
        {
            if (Math.Abs(point.X - _start.X) + Math.Abs(point.Y - _start.Y) < 8 * _window.RenderScaling) return;
            _window.WindowState = WindowState.Normal;
            _window.Position = new PixelPoint(point.X - (int)(_window.Width * _window.RenderScaling * _grabRatio), point.Y - (int)(24 * _window.RenderScaling));
            _origin = _window.Position; _start = point; _restoring = false;
        }
        var candidate = new PixelPoint(_origin.X + point.X - _start.X, _origin.Y + point.Y - _start.Y);
        var position = _constrain?.Invoke(candidate, point) ?? candidate;
        // 发布位置前限制工作区边界；保留原始抓取锚点，避免越界后定时复位造成抖动。
        if (_window.Position != position) _window.Position = position;
        e.Handled = true;
    }
    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _pointer) return;
        _pointer = null; e.Pointer.Capture(null); _completed?.Invoke(); e.Handled = true;
    }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) { if (e.Pointer == _pointer) { _pointer = null; _completed?.Invoke(); } }
    public void CancelDrag()
    {
        var pointer = _pointer; _pointer = null; pointer?.Capture(null);
        if (pointer is not null) _completed?.Invoke();
    }
    public void Dispose()
    {
        var pointer = _pointer; _pointer = null; pointer?.Capture(null);
        _window.RemoveHandler(InputElement.PointerMovedEvent, Move);
        _window.RemoveHandler(InputElement.PointerReleasedEvent, Release);
        _window.PointerCaptureLost -= CaptureLost;
    }
}
