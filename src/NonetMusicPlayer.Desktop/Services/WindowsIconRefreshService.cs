using System.Runtime.InteropServices;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>可执行文件图标更新后定向通知系统，不清理其他应用的图标缓存。</summary>
public static class WindowsIconRefreshService
{
    public static void RefreshIfChanged(string dataRoot, string? applicationPath = null)
    {
        if (!OperatingSystem.IsWindows() || (applicationPath ?? Environment.ProcessPath) is not { } executable) return;
        var com = -1;
        try
        {
            var marker = Path.Combine(dataRoot, "Cache", "native-icon-revision.txt");
            var revision = "head-v4-b13:" + executable + ":" + File.GetLastWriteTimeUtc(executable).Ticks;
            if (File.Exists(marker) && File.ReadAllText(marker) == revision) return;
            // 发行程序只有第 0 组图标；无图标的测试宿主不得覆盖系统通用应用图标。
            if (ExtractIconEx(executable, -1, 0, 0, 0) == 0) return;
            com = CoInitializeEx(0, 0); // Shell 调用需要 COM，允许已有 STA。
            if (com < 0 && com != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(com);
            var cacheRefreshed = SHGetFileInfo(executable, 0, out var image, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x4000) != 0;
            if (cacheRefreshed) SHUpdateImage(executable, 0, 0, image.IconIndex);
            // 只刷新本应用的图标项，不清理无关用户图标缓存。
            SHChangeNotify(0x00002000, 0x0005 | 0x2000, executable, 0); // UPDATEITEM / PATHW / FLUSHNOWAIT
            if (!cacheRefreshed) { AppLog.Warning("Shell", "系统图标索引不可用，下次启动将再次刷新"); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!); File.WriteAllText(marker, revision);
            AppLog.Info("Shell", "Refreshed the updated application icon");
        }
        catch (Exception error) { AppLog.Warning("Shell", "应用图标刷新失败", error); }
        finally { if (com >= 0) CoUninitialize(); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int change, uint flags, string item, nint other);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHUpdateImage(string iconFile, int resourceIndex, uint flags, int imageIndex);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string executable, int iconIndex, nint large, nint small, uint count);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
