namespace NonetMusicPlayer.Core.Updates;

internal static class UpdatePaths
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool Same(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), Comparison);

    public static bool Contains(string root, string path)
    {
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return Same(root, path) || Path.GetFullPath(path).StartsWith(full + Path.DirectorySeparatorChar, Comparison);
    }

    // 校验叶文件与每级父目录，避免替换文件时穿越链接或联接。
    public static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Update paths cannot contain links");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
}
