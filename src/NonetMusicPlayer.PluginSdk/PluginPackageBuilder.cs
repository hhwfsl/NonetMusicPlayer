using System.IO.Compression;
using System.Text.Json;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>仅打包清单实际引用的文件；先在临时包执行与播放器相同的校验，通过后原子发布。</summary>
public static class PluginPackageBuilder
{
    // 保留旧四参数 API；平台支持是新增重载，不破坏已编译工具的调用签名。
    public static string Pack(string sourceDirectory, string outputFile, bool overwrite = false, IEnumerable<string>? additionalFiles = null)
        => Pack(sourceDirectory, outputFile, overwrite, additionalFiles, null);
    public static string Pack(string sourceDirectory, string outputFile, bool overwrite, IEnumerable<string>? additionalFiles, string? rid)
    {
        var source = Path.GetFullPath(sourceDirectory); var output = Path.GetFullPath(outputFile);
        if (!Path.GetExtension(output).Equals(".impp", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Output must use the .impp extension.");
        PluginPathPolicy.RejectLinkedAncestors(source);
        if (File.Exists(output) && !overwrite) throw new IOException("Output already exists. Use --force to replace it.");
        using var input = File.OpenRead(Path.Combine(source, "manifest.json"));
        var manifest = JsonSerializer.Deserialize(input, PluginJsonContext.Default.PluginManifest) ?? throw new InvalidDataException("Missing plugin manifest.");
        manifest.Validate();
        if (rid is not null) PluginPlatformPolicy.RequireSupported(manifest, rid);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        if (manifest.Type is "ui" or "lyrics" or "agent" or "extension") files.Add(manifest.PageEntry);
        foreach (var entry in manifest.EntryPoints.Values) files.Add(entry);
        foreach (var schema in new[] { PluginConfigSchema.FileName, "plugin_config_schema" }) if (File.Exists(Path.Combine(source, schema))) files.Add(schema);
        // 许可文件是合法分发所需内容，不属于可剔除的开发资源；存在时自动随包保留。
        foreach (var notice in new[] { "LICENSE", "LICENSE.txt", "COPYING", "NOTICE", "THIRD_PARTY_NOTICES.txt" }) if (File.Exists(Path.Combine(source, notice))) files.Add(notice);
        foreach (var entry in additionalFiles ?? []) files.Add(entry);
        if (rid is not null)
        {
            files.RemoveWhere(path => !PluginPlatformPolicy.IncludesFile(path, manifest, rid));
            PluginPlatformPolicy.Select(manifest, rid);
        }
        if (files.Count > 1000) throw new InvalidDataException("Package contains too many files.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = Path.Combine(Path.GetDirectoryName(output)!, ".plugin-build-" + Guid.NewGuid().ToString("N") + ".impp");
        try
        {
            long totalBytes = 0;
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (var relative in files.Order(StringComparer.Ordinal))
                {
                    PluginPathPolicy.ValidateRelativePath(relative);
                    if (relative == "manifest.json" && rid is not null)
                    {
                        using var outputManifest = zip.CreateEntry(relative, CompressionLevel.Optimal).Open();
                        JsonSerializer.Serialize(outputManifest, manifest, PluginJsonContext.Default.PluginManifest); continue;
                    }
                    var path = Path.GetFullPath(Path.Combine(source, relative));
                    if (!path.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escapes source directory.");
                    PluginPathPolicy.RejectLinkedAncestors(Path.GetDirectoryName(path)!);
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Package files cannot be links.");
                    totalBytes += new FileInfo(path).Length;
                    if (totalBytes > 256_000_000) throw new InvalidDataException("Package is too large (256 MB maximum).");
                    zip.CreateEntryFromFile(path, relative, CompressionLevel.Optimal);
                }
            PluginPackageInspector.Inspect(temporary);
            File.Move(temporary, output, overwrite);
            return output;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
