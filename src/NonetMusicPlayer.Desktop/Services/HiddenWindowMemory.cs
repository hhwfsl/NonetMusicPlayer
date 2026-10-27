using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>仅在主窗口持续隐藏时回收闲置内存，不暂停播放、连接或插件运行。</summary>
internal static class HiddenWindowMemory
{
    public static void Collect(CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        // 强制发起后台回收，避免 Optimized 模式将整个请求跳过；不压缩堆或等待终结器阻塞播放。
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
        if (token.IsCancellationRequested) return;
        // Windows 可归还闲置驻留页；提交内存与工作集分别记录，不能把分页当作对象泄漏已修复。
        using var process = Process.GetCurrentProcess();
        process.Refresh(); var before = process.WorkingSet64;
        if (OperatingSystem.IsWindows() && !token.IsCancellationRequested) EmptyWorkingSet(process.Handle);
        process.Refresh();
        AppLog.Info("Memory", $"Hidden-window cleanup: working set {before / 1048576d:0.0} -> {process.WorkingSet64 / 1048576d:0.0} MiB; private {process.PrivateMemorySize64 / 1048576d:0.0} MiB");
    }
    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
