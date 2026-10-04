namespace NonetMusicPlayer.Core.Plugins;

/// <summary>播放器与开发工具共用包路径约束，拒绝目录穿越、链接和跨平台歧义路径。</summary>
public static class PluginPathPolicy
{
    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\\') || path.Split('/').Any(x => x is ".." or "." or "") || path.Contains('\0'))
            throw new InvalidDataException("Unsafe relative plugin path.");
    }
    public static void RejectLinkedAncestors(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Plugin paths cannot contain links.");
    }
    /// <summary>退出进程后短暂重试文件操作，兼容 Windows 尚在释放的原生映射和目录句柄。</summary>
    public static void AfterProcessExit(Action operation)
    {
        for (var attempt = 0; ; attempt++)
            try { operation(); return; }
            catch (IOException) when (OperatingSystem.IsWindows() && attempt < 10) { Thread.Sleep(100); }
    }
}
