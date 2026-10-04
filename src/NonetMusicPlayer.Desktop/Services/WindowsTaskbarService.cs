using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>Windows 任务栏缩略图播放控制，不安装 Shell 扩展或辅助进程。</summary>
public sealed class WindowsTaskbarService : IDisposable
{
    private const uint PreviousId = 0x4C01, PlayId = 0x4C02, NextId = 0x4C03;
    private readonly Window _window;
    private readonly MainViewModel _vm;
    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private readonly uint _taskbarCreated;
    private nint _taskbar, _hwnd;
    private bool _added, _disposed, _comInitialized;
    private readonly Dictionary<Glyph, nint> _icons = [];

    private WindowsTaskbarService(Window window, MainViewModel vm)
    {
        _window = window; _vm = vm; _hook = WindowMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarButtonCreated");
        if (_taskbarCreated == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        Win32Properties.AddWndProcHookCallback(window, _hook);
        window.Closed += Closed; vm.PropertyChanged += Changed; L10n.LanguageChanged += LanguageChanged;
    }

    /// <summary>在窗口显示前接入，以免错过 TaskbarButtonCreated 通知。</summary>
    public static WindowsTaskbarService? Attach(Window window, MainViewModel vm)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return new WindowsTaskbarService(window, vm); }
        catch (Exception error) { AppLog.Warning("Taskbar", "任务栏播放控制无法初始化", error); return null; }
    }
    public bool IsAvailable => _added && !_disposed;
    private nint WindowMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed) return 0;
        try
        {
            if (message == _taskbarCreated)
            {
                _hwnd = hwnd; _added = false; EnsureTaskbar(); UpdateButtons();
            }
            else if (message == 0x0111 && ((wParam.ToInt64() >> 16) & 0xffff) == 0x1800)
            {
                var id = (uint)(wParam.ToInt64() & 0xffff);
                if (id is not (PreviousId or PlayId or NextId)) return 0;
                handled = true;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed) return;
                    if (id == PreviousId) _vm.PlayPreviousCommand.Execute(null);
                    else if (id == NextId) _vm.PlayNextCommand.Execute(null);
                    else _vm.TogglePlayPauseCommand.Execute(null);
                });
            }
            else if (message is 0x001A or 0x02E0) // 系统设置或窗口 DPI 发生变化。
            {
                ClearIcons(); UpdateButtons();
            }
        }
        catch (Exception error) { AppLog.Warning("Taskbar", "任务栏播放控制消息未完成", error); }
        return 0;
    }
    private void EnsureTaskbar()
    {
        if (_taskbar != 0) return;
        if (!_comInitialized)
        {
            var result = CoInitializeEx(0, 2); // STA 初始化；S_FALSE 也增加一次 COM 引用。
            if (result >= 0) _comInitialized = true;
            else if (result != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(result);
        }
        var clsid = new Guid("56FDF344-FD6D-11D0-958A-006097C9A090");
        var iid = new Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, 0, 1, ref iid, out _taskbar));
        try { Marshal.ThrowExceptionForHR(Method<HrInit>(3)(_taskbar)); }
        catch { Method<Release>(2)(_taskbar); _taskbar = 0; throw; }
    }
    private T Method<T>(int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(_taskbar), slot * IntPtr.Size));
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsPlaying) or nameof(MainViewModel.CurrentTrack) or nameof(MainViewModel.HasCurrentTrack) or nameof(MainViewModel.TrackCountText)) SafeUpdate();
    }
    private void LanguageChanged(object? sender, EventArgs e) => SafeUpdate();
    private void SafeUpdate()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(SafeUpdate); return; }
        try { UpdateButtons(); } catch (Exception error) { AppLog.Warning("Taskbar", "任务栏按钮无法更新", error); }
    }
    private void UpdateButtons()
    {
        if (_disposed || _taskbar == 0 || _hwnd == 0) return;
        uint flags = _vm.State.Tracks.Count == 0 ? 1u : 0u;
        ThumbButton Button(uint id, Glyph glyph, string tooltip) => new()
        {
            Mask = 0xE, Id = id, Icon = GetIcon(glyph), Tooltip = L10n.T(tooltip), Flags = flags
        };
        ThumbButton[] buttons = [Button(PreviousId, Glyph.Previous, L10n.T("Common.Previous")), Button(PlayId, _vm.IsPlaying ? Glyph.Pause : Glyph.Play, _vm.IsPlaying ? L10n.T("Common.Pause") : L10n.T("Playback.Play")), Button(NextId, Glyph.Next, L10n.T("Common.Next"))];
        var size = Marshal.SizeOf<ThumbButton>(); var memory = Marshal.AllocHGlobal(size * buttons.Length);
        try
        {
            for (var index = 0; index < buttons.Length; index++) Marshal.StructureToPtr(buttons[index], memory + size * index, false);
            var result = Method<ThumbButtons>(_added ? 16 : 15)(_taskbar, _hwnd, (uint)buttons.Length, memory);
            Marshal.ThrowExceptionForHR(result); _added = true;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private nint GetIcon(Glyph glyph)
    {
        if (_icons.TryGetValue(glyph, out var stored)) return stored;
        var size = GetSystemMetrics(11);
        try { var dpi = GetDpiForWindow(_hwnd); if (dpi > 0) size = GetSystemMetricsForDpi(11, dpi); } catch (EntryPointNotFoundException) { }
        size = Math.Clamp(size, 16, 128);
        var dark = true;
        try { if (OperatingSystem.IsWindows()) dark = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is not int value || value == 0; } catch (Exception error) { AppLog.Warning("Taskbar", "系统主题颜色无法读取", error); }
        var pixels = new byte[size * size * 4]; byte color = dark ? (byte)245 : (byte)25;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var coverage = 0;
                for (var sy = 0; sy < 4; sy++)
                    for (var sx = 0; sx < 4; sx++)
                    {
                        var px = (x + (sx + .5) / 4) / size * 24; var py = (y + (sy + .5) / 4) / size * 24;
                        if (Inside(glyph, px, py)) coverage++;
                    }
                if (coverage == 0) continue;
                var offset = (y * size + x) * 4; pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = color;
                pixels[offset + 3] = (byte)(coverage * 255 / 16);
            }
        var info = new BitmapInfo { HeaderSize = 40, Width = size, Height = -size, Planes = 1, BitCount = 32, ImageSize = (uint)pixels.Length };
        var bitmap = CreateDIBSection(0, ref info, 0, out var bits, 0, 0);
        if (bitmap == 0 || bits == 0) { if (bitmap != 0) DeleteObject(bitmap); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        nint mask = 0;
        try
        {
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            mask = CreateBitmap(size, size, 1, 1, new byte[((size + 15) / 16) * 2 * size]);
            if (mask == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var iconInfo = new IconInfo { IsIcon = true, Color = bitmap, Mask = mask };
            var icon = CreateIconIndirect(ref iconInfo); if (icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _icons[glyph] = icon; return icon;
        }
        finally { DeleteObject(bitmap); if (mask != 0) DeleteObject(mask); }
    }
    private static bool Inside(Glyph glyph, double x, double y)
    {
        if (y is < 5 or > 19) return false;
        return glyph switch
        {
            Glyph.Pause => x is >= 7 and <= 10 or >= 14 and <= 17,
            Glyph.Play => x >= 7 && x <= 19 - Math.Abs(y - 12) * 12 / 7,
            Glyph.Previous => x is >= 4 and <= 6 || x <= 19 && x >= 8 + Math.Abs(y - 12) * 11 / 7,
            _ => x is >= 18 and <= 20 || x >= 5 && x <= 16 - Math.Abs(y - 12) * 11 / 7
        };
    }
    private enum Glyph { Previous, Play, Pause, Next }
    private void ClearIcons() { foreach (var icon in _icons.Values) DestroyIcon(icon); _icons.Clear(); }
    private void Closed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Dispose); return; }
        _disposed = true;
        _window.Closed -= Closed; _vm.PropertyChanged -= Changed; L10n.LanguageChanged -= LanguageChanged;
        Win32Properties.RemoveWndProcHookCallback(_window, _hook); ClearIcons();
        if (_taskbar != 0) { Method<Release>(2)(_taskbar); _taskbar = 0; }
        if (_comInitialized) { CoUninitialize(); _comInitialized = false; }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int HrInit(nint instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Release(nint instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ThumbButtons(nint instance, nint hwnd, uint count, nint buttons);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ThumbButton { public uint Mask, Id, Bitmap; public nint Icon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Tooltip; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public uint HeaderSize; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ColorsUsed, ColorsImportant, Colors; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint XHotspot, YHotspot; public nint Mask, Color; }
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
}
