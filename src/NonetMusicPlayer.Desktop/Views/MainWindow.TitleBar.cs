using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly Dictionary<Border, Cursor?> _resizeCursors = [];
    private readonly Cursor _titleMoveCursor = new(StandardCursorType.Arrow);
    private Win32Properties.CustomWndProcHookCallback? _titleMessageHook;
    private bool _movingTitle, _awaitingMovePointer, _nativeTitleMove;
    private NativePoint? _titleReleasePoint;
    private TouchWindowMover? _touchTitleMover;
    private void InitializeTitleInteraction()
    {
        _touchTitleMover = new TouchWindowMover(this);
        foreach (var border in ((Avalonia.Controls.Grid)Content!).Children.OfType<Border>().Where(b => b.Tag is string)) _resizeCursors[border] = border.Cursor;
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (!_awaitingMovePointer || _movingTitle || _nativeTitleMove || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            // 原生移动结束可能报告旧角落坐标；在真实指针移入非缩放区域前，不恢复缩放命中测试。
            if (p.X > 12 && p.Y > 12 && p.X < Bounds.Width - 12 && p.Y < Bounds.Height - 12) RestoreResizeHitTesting();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        if (OperatingSystem.IsWindows())
        {
            _titleMessageHook = TitleWindowMessage;
            Win32Properties.AddWndProcHookCallback(this, _titleMessageHook);
        }
        Closed += (_, _) =>
        {
            if (_titleMessageHook is not null) Win32Properties.RemoveWndProcHookCallback(this, _titleMessageHook);
            _titleMoveCursor.Dispose();
            _touchTitleMover.Dispose();
        };
    }
    private void ApplyTitleButtons()
    {
        var left = _vm?.Settings.TitleButtonsOnLeft ?? OperatingSystem.IsMacOS();
        TitleBarLayout.Margin = left ? new(16, 0, 16, 0) : new(20, 0, 12, 0);
        TitleBarLayout.ColumnDefinitions = new ColumnDefinitions(left ? "*,Auto,*" : "Auto,*,Auto");
        Grid.SetColumn(TitleButtons, left ? 0 : 2); Grid.SetColumn(TitleBrand, left ? 1 : 0);
        TitleBrand.HorizontalAlignment = left ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Left;
        TitleButtons.HorizontalAlignment = left ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
        // 颜色始终对应同一功能；位置设置仅改变符合平台习惯的按钮顺序。
        Control[] buttons = left ? [CloseWindowButton, MinimizeWindowButton, MaximizeWindowButton]
            : [MinimizeWindowButton, MaximizeWindowButton, CloseWindowButton];
        TitleButtons.Children.Clear(); foreach (var button in buttons) TitleButtons.Children.Add(button);
    }
    private void RestoreResizeHitTesting()
    {
        _awaitingMovePointer = false; _titleReleasePoint = null; Cursor = null;
        RefreshResizeBorders();
    }
    private void RefreshResizeBorders() { foreach (var pair in _resizeCursors) { pair.Key.Cursor = _awaitingMovePointer ? Cursor : pair.Value; pair.Key.IsHitTestVisible = WindowState == WindowState.Normal && !_awaitingMovePointer; } }
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Ancestors(e.Source).Any(c => c is Button)) return;
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen)
        {
            if (e.ClickCount == 2) { ToggleMaximize(); e.Handled = true; return; }
            RestoreResizeHitTesting(); _touchTitleMover!.TryBegin(e); return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        _movingTitle = true; _awaitingMovePointer = true; _titleReleasePoint = null; Cursor = _titleMoveCursor;
        foreach (var pair in _resizeCursors) { pair.Key.Cursor = Cursor; pair.Key.IsHitTestVisible = false; }
        try { BeginMoveDrag(e); }
        finally
        {
            _movingTitle = false;
            if (OperatingSystem.IsWindows() && _awaitingMovePointer && GetCursorPos(out var point)) _titleReleasePoint = point;
        }
    }
    private nint TitleWindowMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_awaitingMovePointer) return 0;
        if (message == 0x0231) _nativeTitleMove = true; // WM_ENTERSIZEMOVE
        if (message == 0x0232) // WM_EXITSIZEMOVE：原生移动循环已释放指针捕获。
        {
            _nativeTitleMove = false;
            if (GetCursorPos(out var release)) _titleReleasePoint = release;
            SetCursor(LoadCursor(0, 32512));
        }
        if (message == 0x0020) // WM_SETCURSOR：也处理释放时过时的命中信息。
        {
            SetCursor(LoadCursor(0, 32512)); handled = true; return 1;
        }
        if (message is 0x0200 or 0x00A0 && !_nativeTitleMove && !_movingTitle)
        {
            if (GetCursorPos(out var current) && _titleReleasePoint is { } previous && (current.X != previous.X || current.Y != previous.Y)) RestoreResizeHitTesting();
            else
            {
                // 仅抑制静止位置的过时原生鼠标消息，避免沿用旧缩放目标的指针，不抑制真实移动。
                SetCursor(LoadCursor(0, 32512)); handled = true; return 0;
            }
        }
        return 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern nint LoadCursor(nint instance, nint resource);
}
