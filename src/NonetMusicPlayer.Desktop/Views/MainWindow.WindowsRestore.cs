using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private WindowState _stateBeforeMinimize = WindowState.Normal;
    private Win32Properties.CustomWndProcHookCallback? _restoreMessageHook;
    private bool _restorePosted;
    private void InitializeWindowRestore()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState is WindowState.Normal or WindowState.Maximized)
                _stateBeforeMinimize = WindowState;
        };
        if (!OperatingSystem.IsWindows()) return;
        _restoreMessageHook = RestoreWindowMessage;
        Win32Properties.AddWndProcHookCallback(this, _restoreMessageHook);
        Closed += (_, _) => Win32Properties.RemoveWndProcHookCallback(this, _restoreMessageHook);
    }
    private nint RestoreWindowMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (!IsVisible || !IsEnabled || !RestoreIsWindowEnabled(hwnd) || !(WindowState == WindowState.Minimized || RestoreIsIconic(hwnd))) return 0;
        var restoreCommand = message == 0x0112 && (wParam.ToInt64() & 0xFFF0) == 0xF120;
        var activating = message == 0x0006 && (wParam.ToInt64() & 0xFFFF) != 0;
        if (!restoreCommand && !activating) return 0;
        // 无边框窗口由框架和 Windows 分别维护状态；一次用户还原请求只排队一次。
        // 截获 SC_RESTORE，避免默认系统菜单路径在状态不同步时鸣响并吞掉第一次点击。
        if (restoreCommand) handled = true;
        if (!_restorePosted)
        {
            _restorePosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                try { if (IsVisible && IsEnabled) RestoreWindowCore(); }
                finally { _restorePosted = false; }
            }, DispatcherPriority.Input);
        }
        return 0;
    }
    private void RestoreWindowCore()
    {
        if (!IsVisible) Show();
        var handle = OperatingSystem.IsWindows() ? TryGetPlatformHandle()?.Handle ?? 0 : 0;
        if (WindowState == WindowState.Minimized || handle != 0 && RestoreIsIconic(handle))
        {
            // SW_RESTORE 同步原生状态；随后用保存值保留最小化前的最大化状态。
            var target = _stateBeforeMinimize;
            if (handle != 0) RestoreShowWindow(handle, 9);
            SetCurrentValue(WindowStateProperty, target);
        }
        RestoreVisualMemory(); Activate();
    }
    [DllImport("user32.dll", EntryPoint = "IsIconic")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RestoreIsIconic(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RestoreIsWindowEnabled(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "ShowWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RestoreShowWindow(nint hwnd, int command);
}
