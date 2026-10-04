using System.IO.Compression;
using System.Text.Json;
using L10n = NonetMusicPlayer.Core.Plugins.PluginMessages;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>桌面与 CLI 共用安装前审查：只接受 impp 包，限制解压大小并验证声明式页面。</summary>
public static class PluginPackageInspector
{
    public static PluginManifest Inspect(string package)
    {
        if (!Path.GetExtension(package).Equals(".impp", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L10n.Get("Plugins.PluginPackagesMustUseTheImppExtension"));
        using var zip = ZipFile.OpenRead(package);
        if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 256_000_000) throw new InvalidDataException("插件包过大（最多 1000 个文件 / 256 MB）。");
        var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            PluginPathPolicy.ValidateRelativePath(entry.FullName.TrimEnd('/'));
            if (!entryNames.Add(entry.FullName.TrimEnd('/'))) throw new InvalidDataException("插件包不能包含重复或仅大小写不同的路径。");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("插件包不允许符号链接。");
        }
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("包根目录缺少 manifest.json。");
        if (manifestEntry.Length > 100_000) throw new InvalidDataException("插件清单过大。");
        using var stream = manifestEntry.Open(); var manifest = JsonSerializer.Deserialize(stream, PluginJsonContext.Default.PluginManifest) ?? throw new InvalidDataException("插件清单为空。");
        manifest.Validate(); manifest.Enabled = false; manifest.Configuration = "{}";
        var schemaEntry = zip.GetEntry(PluginConfigSchema.FileName) ?? zip.GetEntry("plugin_config_schema");
        if (zip.GetEntry(PluginConfigSchema.FileName) is not null && zip.GetEntry("plugin_config_schema") is not null) throw new InvalidDataException("插件只能包含一份配置规范。");
        System.Text.Json.Nodes.JsonObject? configSchema = null;
        if (schemaEntry is not null) { using var schemaStream = schemaEntry.Open(); configSchema = PluginConfigSchema.Read(schemaStream); }
        foreach (var entry in manifest.EntryPoints.Values) if (zip.GetEntry(entry) is null) throw new InvalidDataException("插件入口未包含在安装包中。");
        foreach (var notice in zip.Entries.Where(e => IsLicenseFile(e.FullName)))
        {
            // 仅允许根目录固定名称的 UTF-8 许可文本；不把许可例外扩展成任意资源入口。
            if (notice.Length > 256 * 1024) throw new InvalidDataException("插件许可文件过大。");
            using var reader = new StreamReader(notice.Open(), new System.Text.UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            if (reader.ReadToEnd().Contains('\0')) throw new InvalidDataException("插件许可文件必须是文本。");
        }
        if (manifest.Type == "ui")
        {
            var page = zip.GetEntry(manifest.PageEntry) ?? throw new InvalidDataException("UI 页面入口未包含在安装包中。");
            if (zip.Entries.Where(e => !e.FullName.EndsWith('/')).Any(e => e.FullName != "manifest.json" && e.FullName != manifest.PageEntry && e != schemaEntry && !IsLicenseFile(e.FullName)))
                throw new InvalidDataException("声明式 UI 插件只允许清单、JSON 页面及配置规范，不允许夹带脚本或程序。");
            if (zip.Entries.Where(e => e.FullName.EndsWith('/')).Any(e => !manifest.PageEntry.StartsWith(e.FullName, StringComparison.Ordinal)))
                throw new InvalidDataException("UI 插件不允许未使用的目录。");
            using var pageStream = page.Open();
            if (schemaEntry is null) PluginPageContract.Read(pageStream, manifest.Permissions);
            else
            {
                if (page.Length > 256 * 1024) throw new InvalidDataException("插件页面过大。");
                var pageNode = System.Text.Json.Nodes.JsonNode.Parse(pageStream, documentOptions: new JsonDocumentOptions { MaxDepth = 24 }) ?? throw new InvalidDataException("插件页面为空。");
                using var resolved = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(PluginConfigSchema.Substitute(pageNode, PluginConfigSchema.Defaults(configSchema!)).ToJsonString()));
                PluginPageContract.Read(resolved, manifest.Permissions);
            }
        }
        return manifest;
    }
    private static bool IsLicenseFile(string name) => name is "LICENSE" or "LICENSE.txt" or "COPYING" or "NOTICE" or "THIRD_PARTY_NOTICES.txt";
}
