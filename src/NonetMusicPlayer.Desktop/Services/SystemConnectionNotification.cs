using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>配对不抢占前台焦点；系统通知仅携带设备说明，不含邀请与会话令牌。</summary>
internal sealed class SystemConnectionNotification : IDisposable
{
    private static int _sequence = 800;
    private readonly Window _window;
    private readonly Action _open;
    private readonly Win32Properties.CustomWndProcHookCallback _hook;
    private NotifyData _data;
    private bool _added, _disposed;
    private const uint Message = 0x8000 + 79;
    private SystemConnectionNotification(Window window, string title, string body, int seconds, Action open)
    {
        _window = window; _open = open; _hook = Receive;
        if (OperatingSystem.IsWindows())
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? 0; if (handle == 0) return;
            _data = new() { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = handle, Id = (uint)Interlocked.Increment(ref _sequence), Flags = 7, Callback = Message,
                Icon = LoadIcon(0, (nint)32516), Tip = "Nonet", Info = "", Title = "" };
            Win32Properties.AddWndProcHookCallback(window, _hook);
            _added = ShellNotify(0, ref _data);
            if (_added) { _data.Flags = 0x10; _data.Info = body[..Math.Min(body.Length, 255)]; _data.Title = title[..Math.Min(title.Length, 63)]; _data.Timeout = (uint)(seconds * 1000); _data.InfoFlags = 1; ShellNotify(1, ref _data); }
        }
        else
        {
            var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (OperatingSystem.IsLinux()) { start.FileName = "notify-send"; foreach (var arg in new[] { "--app-name=Nonet", "--expire-time=" + seconds * 1000, "--", title, body }) start.ArgumentList.Add(arg); }
            else if (OperatingSystem.IsMacOS()) { start.FileName = "/usr/bin/osascript"; foreach (var arg in new[] { "-e", "on run argv\ndisplay notification (item 2 of argv) with title (item 1 of argv)\nend run", title, body }) start.ArgumentList.Add(arg); }
            else return;
            Process.Start(start)?.Dispose();
        }
    }
    public static IDisposable Show(Window window, string title, string body, int seconds, Action open)
    {
        try { return new SystemConnectionNotification(window, title, body, seconds, open); }
        catch (Exception error) { AppLog.Warning("LAN", "System notification unavailable; pending request remains available in Nonet", error); return new ElementLayoutService.ActionLease(() => { }); }
    }
    private nint Receive(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (!_disposed && message == Message && (uint)wParam == _data.Id && (uint)lParam is 0x405 or 0x202)
        { handled = true; Dispatcher.UIThread.Post(_open); }
        return 0;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (OperatingSystem.IsWindows() && _data.Window != 0) { if (_added) ShellNotify(2, ref _data); Win32Properties.RemoveWndProcHookCallback(_window, _hook); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotify(uint action, ref NotifyData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIcon(nint instance, nint name);
}
