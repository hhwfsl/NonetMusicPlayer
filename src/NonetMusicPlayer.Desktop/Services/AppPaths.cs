using System.Text;
using System.Text.Json;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>通过程序目录内的引导文件定位数据，不依赖隐式配置副本。</summary>
public sealed class AppPaths
{
    public string InstallationDirectory { get; }
    public string BootstrapPath => Path.Combine(InstallationDirectory, "Nonet.bootstrap.json");
    public string DataDirectory { get; }
    public string BackupDirectory { get; }
    public bool IsOverride { get; }
    public string? RecoveryMessage { get; }
    public static string LegacyDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NonetMusicPlayer");

    public AppPaths(string? explicitRoot = null, string? installationDirectory = null)
    {
        InstallationDirectory = Path.GetFullPath(installationDirectory ?? AppContext.BaseDirectory);
        // 新品牌使用 NONET_DATA_DIR；保留旧环境变量以兼容已有隔离启动脚本。
        var environmentRoot = Environment.GetEnvironmentVariable("NONET_DATA_DIR") ?? Environment.GetEnvironmentVariable("LMP_DATA_DIR");
        IsOverride = !string.IsNullOrWhiteSpace(explicitRoot) || !string.IsNullOrWhiteSpace(environmentRoot);
        var root = string.IsNullOrWhiteSpace(explicitRoot) ? (string.IsNullOrWhiteSpace(environmentRoot) ? null : environmentRoot) : explicitRoot;
        string? backup = null;
        var readableBootstrap = File.Exists(BootstrapPath) ? BootstrapPath : Path.Combine(InstallationDirectory, "LittleMusicPlayer.bootstrap.json");
        if (!IsOverride && File.Exists(readableBootstrap))
        {
            try
            {
                if (new FileInfo(readableBootstrap).Length > 32 * 1024) throw new InvalidDataException("引导文件超过 32 KB。");
                using var json = JsonDocument.Parse(File.ReadAllText(readableBootstrap));
                if (!json.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1) throw new InvalidDataException("引导文件版本不受支持。");
                root = ResolveRelative(json.RootElement.GetProperty("dataDirectory").GetString() ?? "Data");
                if (json.RootElement.TryGetProperty("backupDirectory", out var b) && !string.IsNullOrWhiteSpace(b.GetString())) backup = ResolveRelative(b.GetString()!);
            }
            catch (Exception error) when (error is JsonException or IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
            {
                root = null; backup = null;
                RecoveryMessage = "数据目录引导文件无法读取，原文件已保留；暂使用软件目录中的 Data。请在设置中重新指定原数据目录。";
            }
        }
        DataDirectory = Path.GetFullPath(root ?? Path.Combine(InstallationDirectory, "Data"));
        BackupDirectory = Path.GetFullPath(backup ?? Path.Combine(DataDirectory, "Backups"));
    }

    private string ResolveRelative(string value) => Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(InstallationDirectory, value));

    public void EnsureBootstrap()
    {
        if (!IsOverride && !File.Exists(BootstrapPath)) SaveConfiguration(DataDirectory, BackupDirectory);
    }

    public void SaveConfiguration(string dataDirectory, string backupDirectory)
    {
        if (IsOverride) return; // 隔离测试及显式环境覆盖不修改安装目录引导文件。
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("dataDirectory", PortablePath(dataDirectory)); writer.WriteString("backupDirectory", PortablePath(backupDirectory)); writer.WriteEndObject();
        }
        AppStorage.AtomicWrite(BootstrapPath, Encoding.UTF8.GetString(bytes.ToArray()));
    }
    private string PortablePath(string path) => DataDirectoryService.Contains(InstallationDirectory, path) ? Path.GetRelativePath(InstallationDirectory, path) : Path.GetFullPath(path);
}

public sealed record DataDirectoryChangeResult(string OldRoot, string NewRoot, string BackupFolder, bool RequiresRestart);

public static class DataDirectoryService
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("请选择数据目录。");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!), Comparison)) throw new InvalidDataException("不能将磁盘根目录作为数据或备份目录。");
        return full;
    }
    public static bool Contains(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile) return false;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); path = Path.GetFullPath(path);
        return string.Equals(root, Path.TrimEndingDirectorySeparator(path), Comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, Comparison);
    }
    public static string Remap(string path, string oldRoot, string newRoot) => Contains(oldRoot, path) ? Path.Combine(newRoot, Path.GetRelativePath(oldRoot, path)) : path;
    public static void ValidateBackup(string dataRoot, string backup)
    {
        dataRoot = Normalize(dataRoot); backup = Normalize(backup);
        RejectLinkedAncestors(dataRoot); RejectLinkedAncestors(backup);
        if (Contains(backup, dataRoot)) throw new InvalidDataException("备份目录不能等于或包含数据目录；请使用 Data/Backups 或独立目录。");
        foreach (var reserved in new[] { "Artwork", "Lyrics", "Logs", "Plugins", "Temp", "Layouts" })
            if (Contains(Path.Combine(dataRoot, reserved), backup)) throw new InvalidDataException("备份目录不能混入封面、歌词、日志、插件或布局目录。");
    }
    public static void CopyNonDestructive(string source, string destination, bool skipRuntimeFiles = false)
    {
        source = Normalize(source); destination = Normalize(destination);
        if (Contains(source, destination) || Contains(destination, source)) throw new InvalidDataException("新旧数据目录不能相同或相互包含，以免递归复制。只更改备份目录请使用对应设置。");
        RejectLinkedAncestors(source); RejectLinkedAncestors(destination);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new InvalidDataException("目标目录不是空目录。为保护现有文件，请选择新的空目录。");
        Directory.CreateDirectory(destination);
        var pending = new Stack<(string Source, string Destination)>(); pending.Push((source, destination));
        while (pending.TryPop(out var pair))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pair.Source))
            {
                if (skipRuntimeFiles && pair.Source == source && Path.GetFileName(entry) is "Temp" or "Logs") continue;
                if (skipRuntimeFiles && Path.GetExtension(entry).Equals(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                if (skipRuntimeFiles && Path.GetRelativePath(source, entry).Split(Path.DirectorySeparatorChar)[0].Equals("Plugins", StringComparison.OrdinalIgnoreCase) && IsPrivatePluginEntry(entry)) continue;
                if (!File.Exists(entry) && !Directory.Exists(entry)) continue; // 原子保存完成后，临时文件可能已移除。
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("数据目录中包含符号链接或联接；为避免复制到目录外，请先使用普通目录。");
                var target = Path.Combine(pair.Destination, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0) { Directory.CreateDirectory(target); pending.Push((entry, target)); }
                else
                {
                    try { File.Copy(entry, target, false); }
                    catch (FileNotFoundException) when (!File.Exists(entry)) { /* Concurrent lyric replacement / completed atomic save. */ }
                }
            }
        }
    }
    internal static bool IsPrivatePluginEntry(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name.StartsWith(".stage-") || name is "secrets" or ".secrets" or "credentials" or ".sessions" or ".env"
            || name.StartsWith(".env.") || name.StartsWith("secrets.") || name.StartsWith("credentials.") || name.StartsWith("token.") || name.StartsWith("session.")
            || Path.GetExtension(name) is ".key" or ".pem" or ".pfx" or ".p12";
    }
    internal static void RejectLinkedAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("数据迁移目录不能经过符号链接或联接，请使用明确的实际目录。");
    }
}
