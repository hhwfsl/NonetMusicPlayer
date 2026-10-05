using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>新包按 RID 分发；旧多平台包仍可安装，但仅提取本机与共享资源。</summary>
public static class PluginPlatformPolicy
{
    public static string CurrentRid => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public static bool IsRid(string value) => Regex.IsMatch(value, "^(win|osx|linux)-(x64|x86|arm64|arm)$");
    public static void RequireSupported(PluginManifest manifest, string rid)
    {
        if (!IsRid(rid)) throw new InvalidDataException("Unsupported plugin platform: " + rid);
        if (manifest.Platform.Length > 0 && manifest.Platform != rid || manifest.EntryPoints.Count > 0 && !manifest.EntryPoints.ContainsKey(rid))
            throw new InvalidDataException(PluginMessages.Get("Plugins.NoPlatformPackage"));
    }
    /// <summary>只解压经过检查的包中的本机文件，共享许可/配置/页面始终保留。</summary>
    public static void ExtractCurrent(string package, PluginManifest manifest, string stage)
    {
        var rid = CurrentRid; RequireSupported(manifest, rid); PluginPathPolicy.RejectLinkedAncestors(stage);
        using var archive = ZipFile.OpenRead(package);
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/') && IncludesFile(e.FullName, manifest, rid)))
        {
            var path = Path.Combine(stage, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            PluginPathPolicy.RejectLinkedAncestors(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)(entry.ExternalAttributes >> 16 & 0x1FF) | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Select(manifest, rid);
        File.WriteAllText(Path.Combine(stage, "manifest.json"), JsonSerializer.Serialize(manifest, PluginJsonContext.Default.PluginManifest));
    }
    /// <summary>回收已安装旧包中的明确异平台文件；不碰配置、共享文件、外部路径或链接。</summary>
    public static bool PruneInstalled(string directory, PluginManifest manifest)
    {
        var rid = CurrentRid; RequireSupported(manifest, rid); PluginPathPolicy.RejectLinkedAncestors(directory);
        if (!Directory.Exists(directory)) return false;
        var directories = new List<string>(); var files = new List<string>(); var pending = new Stack<string>(); pending.Push(Path.GetFullPath(directory));
        while (pending.TryPop(out var folder))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                PluginPathPolicy.RejectLinkedAncestors(entry);
                if (Directory.Exists(entry)) { directories.Add(entry); pending.Push(entry); } else files.Add(entry);
            }
        }
        var changed = false;
        foreach (var file in files)
            if (!IncludesFile(Path.GetRelativePath(directory, file).Replace('\\', '/'), manifest, rid)) { File.Delete(file); changed = true; }
        foreach (var folder in directories.OrderByDescending(p => p.Length))
            if (!IncludesPath(Path.GetRelativePath(directory, folder).Replace('\\', '/'), rid) && !Directory.EnumerateFileSystemEntries(folder).Any()) { Directory.Delete(folder); changed = true; }
        if (manifest.EntryPoints.Keys.Any(key => key != rid)) { Select(manifest, rid); changed = true; }
        if (changed) File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, PluginJsonContext.Default.PluginManifest));
        return changed;
    }
    public static bool IncludesPath(string relative, string rid)
        => !relative.Split('/').Any(part => IsRid(part) && part != rid);
    public static bool IncludesFile(string relative, PluginManifest manifest, string rid)
    {
        if (!IncludesPath(relative, rid)) return false;
        // 旧清单的可执行文件也可能位于根目录，不能只按目录名过滤入口。
        return !manifest.EntryPoints.Where(p => p.Key != rid).Any(p => p.Value == relative)
            || manifest.EntryPoints.TryGetValue(rid, out var current) && current == relative;
    }
    public static void Select(PluginManifest manifest, string rid)
    {
        RequireSupported(manifest, rid);
        manifest.EntryPoints = manifest.EntryPoints.Where(p => p.Key == rid).ToDictionary(p => p.Key, p => p.Value);
        manifest.Platform = rid;
    }
    public static bool MatchesAsset(string filename, string rid)
    {
        var matches = Regex.Matches(filename, @"(?:^|[-_.])(win|osx|linux)-(x64|x86|arm64|arm)(?=[-_.]|$)");
        // 没有 RID 的旧包/纯声明式包保持兼容；有 RID 时必须精确匹配，不退回其他平台。
        return matches.Count == 0 || matches.Any(m => m.Groups[1].Value + "-" + m.Groups[2].Value == rid);
    }
    public static string? AssetRid(string filename)
        => Regex.Match(filename, @"(?:^|[-_.])((?:win|osx|linux)-(?:x64|x86|arm64|arm))(?=[-_.]|$)") is { Success: true } m ? m.Groups[1].Value : null;
}
