using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>维护系统原生点击区域，实现真正的窗口点击穿透而非仅忽略控件命中。</summary>
public sealed class DesktopLyricsInputService : IDisposable
{
    private readonly Window _window;
    private readonly Func<IReadOnlyList<Rect>> _regions;
    private readonly Func<Point, bool> _accepts;
    private readonly Action<bool> _frame;
    private readonly Win32Properties.CustomWndProcHookCallback? _hook;
    private string _lastShape = "";
    private bool _locked;
    private bool _presentationPending;
    private long? _originalStyle;
    private nint _lastHandle;
    private readonly DesktopLyricsLayeredSurface _surface = new();
    private string _lastFrame = "";
    private nint _xDisplay;
    public bool NativeSupported { get; private set; }
    public DesktopLyricsInputService(Window window, Func<IReadOnlyList<Rect>> regions, Func<Point, bool> accepts, Action<bool> frame)
    {
        _window = window; _regions = regions; _accepts = accepts; _frame = frame;
        if (OperatingSystem.IsWindows()) { _hook = Message; Win32Properties.AddWndProcHookCallback(window, _hook); }
    }
    public void Refresh(bool locked, bool frameVisible)
    {
        _locked = locked;
        if (!_window.IsVisible) return;
        var handle = _window.TryGetPlatformHandle(); if (handle is null) return;
        if (handle.Handle != _lastHandle) { _lastHandle = handle.Handle; _lastShape = _lastFrame = ""; _originalStyle = null; _surface.Dispose(); }
        if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND")
        {
            NativeSupported = true;
            // 非悬停窗口的原生区域仅包含歌词字形；空白区域位于 HWND 外，点击传给后方窗口。
            var regions = frameVisible && !locked ? new[] { new Rect(_window.Bounds.Size) } : _regions().ToArray();
            if (regions.Length == 0) return; // 先允许首帧文字绘制，再限制原生窗口区域。
            var signature = locked + ":" + frameVisible + ":" + _window.RenderScaling + ":" + string.Join(';', regions);
            if (signature != _lastShape)
            {
                var combined = CreateRectRgn(0, 0, 0, 0);
                try
                {
                    foreach (var rect in regions)
                    {
                        var scale = _window.RenderScaling;
                        var region = CreateRectRgn((int)Math.Floor(rect.Left * scale), (int)Math.Floor(rect.Top * scale), (int)Math.Ceiling(rect.Right * scale), (int)Math.Ceiling(rect.Bottom * scale));
                        try { CombineRgn(combined, combined, region, 2); } finally { DeleteObject(region); }
                    }
                    if (SetWindowRgn(handle.Handle, combined, true) != 0) { combined = 0; _lastShape = signature; } // 成功后由 Windows 接管区域句柄。
                }
                finally { if (combined != 0) DeleteObject(combined); }
            }
            var style = GetWindowLongPtr(handle.Handle, -20).ToInt64();
            _originalStyle ??= style;
            const long inputMask = 0x20L | 0x08000000L;
            var target = (style & ~inputMask) | (_originalStyle.Value & inputMask) | 0x80000L;
            if (locked) target |= inputMask;
            var changed = target != style;
            if (changed) SetWindowLongPtr(handle.Handle, -20, (nint)target);
            // 锁定与解锁均使用相同的预乘 Alpha 呈现；避免混用 DWM 软件画面与异形窗口产生黑块。
            var visuals = _window.GetVisualDescendants().ToArray();
            var frame = _window.Position + ":" + _window.Bounds + ":" + _window.RenderScaling + ":" + _window.FontFamily + ":" + frameVisible + ":" +
                string.Join(';', visuals.OfType<KaraokeLine>().Select(l => l.Text + ":" + l.TextSize + ":" + l.TextColor + ":" + l.VisualRevision)) + ":" +
                string.Join(';', visuals.OfType<TextBlock>().Select(t => t.Text)) + ":" +
                string.Join(';', visuals.OfType<VectorIcon>().Select(i => i.Kind)) + ":" +
                string.Join(';', visuals.OfType<Button>().Select(b => b.IsPointerOver));
            if (changed || frame != _lastFrame)
            {
                if (_surface.Present(_window, handle.Handle)) _lastFrame = frame;
                else AppLog.Warning("Lyrics", "桌面歌词透明画面更新失败");
            }
        }
        else if (OperatingSystem.IsLinux() && handle.HandleDescriptor == "XID")
        {
            try
            {
                if (_xDisplay == 0) _xDisplay = XOpenDisplay(0);
                if (_xDisplay == 0 || XShapeQueryExtension(_xDisplay, out _, out _) == 0) return;
                var areas = locked ? [] : frameVisible ? new[] { new Rect(_window.Bounds.Size) } : _regions().ToArray();
                var rectangles = areas.Select(r => new XRectangle { X = (short)(r.X * _window.RenderScaling), Y = (short)(r.Y * _window.RenderScaling), Width = (ushort)Math.Ceiling(r.Width * _window.RenderScaling), Height = (ushort)Math.Ceiling(r.Height * _window.RenderScaling) }).ToArray();
                var signature = locked + ":" + frameVisible + ":" + _window.RenderScaling + ":" + string.Join(';', areas);
                if (signature != _lastShape) { XShapeCombineRectangles(_xDisplay, handle.Handle, 2, 0, 0, rectangles, rectangles.Length, 0, 0); XFlush(_xDisplay); _lastShape = signature; }
                NativeSupported = true;
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { AppLog.Warning("Lyrics", "系统不支持桌面歌词输入区域", error); }
        }
        else if (OperatingSystem.IsMacOS() && handle is IMacOSTopLevelPlatformHandle mac && mac.NSWindow != 0)
        {
            var location = SendPoint(mac.NSWindow, RegisterSelector("mouseLocationOutsideOfEventStream"));
            var point = new Point(location.X, _window.Bounds.Height - location.Y);
            var accepts = !locked && _accepts(point);
            _frame(accepts);
            SendBool(mac.NSWindow, RegisterSelector("setIgnoresMouseEvents:"), !accepts);
            NativeSupported = true;
        }
    }
    public bool IsPresented
    {
        get
        {
            if (!_window.IsVisible || _window.WindowState == WindowState.Minimized) return false;
            var handle = _window.TryGetPlatformHandle();
            return !OperatingSystem.IsWindows() || handle?.HandleDescriptor != "HWND" || IsWindowVisible(handle.Handle) && !IsIconic(handle.Handle);
        }
    }
    public void RestorePresentation(bool raise = false)
    {
        if (!_window.IsVisible) return;
        _window.Topmost = true;
        var handle = _window.TryGetPlatformHandle();
        if (OperatingSystem.IsWindows() && handle?.HandleDescriptor == "HWND")
        {
            if (!IsWindowVisible(handle.Handle) || IsIconic(handle.Handle)) ShowWindow(handle.Handle, 4); // SW_SHOWNOACTIVATE
            if (raise || (GetWindowLongPtr(handle.Handle, -20).ToInt64() & 8) == 0)
                SetWindowPos(handle.Handle, -1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200); // 保持尺寸与焦点，仅恢复置顶层级。
        }
    }
    private nint Message(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x0047 or 0x0005 or 0x0018 && _window.IsVisible && !_presentationPending &&
            (!IsWindowVisible(hwnd) || IsIconic(hwnd) || (GetWindowLongPtr(hwnd, -20).ToInt64() & 8) == 0))
        {
            // 及时响应原生层级与可见性变化；周期检查仅为恢复后备手段。
            _presentationPending = true;
            Dispatcher.UIThread.Post(() => { _presentationPending = false; RestorePresentation(); }, DispatcherPriority.Loaded);
        }
        if (message == 0x0084)
        {
            var point = _window.PointToClient(new PixelPoint(unchecked((short)(long)lParam), unchecked((short)((long)lParam >> 16))));
            var accepted = !_locked && _accepts(point);
            if (accepted) { _frame(true); Refresh(false, true); }
            handled = true; return accepted ? 1 : -1;
        }
        if (_locked && message is 0x0021 or 0x0201 or 0x0204) { handled = true; return message == 0x0021 ? 3 : 0; }
        return 0;
    }
    public void Dispose()
    {
        _surface.Dispose();
        if (_hook is not null) Win32Properties.RemoveWndProcHookCallback(_window, _hook);
        if (_xDisplay != 0) { XCloseDisplay(_xDisplay); _xDisplay = 0; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct XRectangle { public short X, Y; public ushort Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct NSPoint { public double X, Y; }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern nint RegisterSelector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern NSPoint SendPoint(nint receiver, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint region);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint hwnd, nint rect, nint region, uint flags);
    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libXext.so.6")] private static extern int XShapeQueryExtension(nint display, out int eventBase, out int errorBase);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(nint display, nint window, int kind, int x, int y, XRectangle[] rectangles, int count, int operation, int ordering);
}
