using System.Runtime.InteropServices;

namespace NonetMusicPlayerCli;

/// <summary>Windows 旧控制台显式开启 VT；能力不可用时降级逐行模式，不依赖图形桌面。</summary>
internal static class ConsoleCapabilities
{
    public static bool EnableAnsi()
    {
        if (!OperatingSystem.IsWindows()) return true;
        var handle = GetStdHandle(-11);
        return GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | 0x0004);
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int type);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
