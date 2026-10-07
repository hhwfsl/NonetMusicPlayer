using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>生成数据插件或源码项目，不编译模型代码，不覆盖任何已有文件。</summary>
public static class PluginDevelopmentService
{
    public static void ValidateId(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^custom[.][a-z][a-z0-9-]{2,60}$"))
            throw new InvalidDataException("Generated plugin IDs must use custom.<lowercase-name>.");
    }
    private static string CreateRoot(string parent, string id)
    {
        ValidateId(id); parent = Path.GetFullPath(parent); PluginPathPolicy.RejectLinkedAncestors(parent);
        var folder = Path.GetFullPath(Path.Combine(parent, id));
        if (!folder.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid project path.");
        PluginPathPolicy.RejectLinkedAncestors(folder);
        if (Directory.Exists(folder) || File.Exists(folder)) throw new InvalidDataException("Project already exists; no files were replaced.");
        Directory.CreateDirectory(folder); return folder;
    }
    public static string WriteSource(string parent, string id, JsonObject files)
    {
        ValidateId(id);
        if (files.Count is < 1 or > 40 || files.ToJsonString().Length > 128000) throw new InvalidDataException("Source bundle is too large.");
        foreach (var (path, value) in files)
        {
            PluginPathPolicy.ValidateRelativePath(path);
            if (path.Split('/').Any(p => p.StartsWith('.') || p is "bin" or "obj" or "dist") || Path.GetExtension(path) is not (".cs" or ".csproj" or ".slnx" or ".props" or ".json" or ".md" or ".ps1" or ".sh") || value is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || text.Contains('\0'))
                throw new InvalidDataException("Invalid source file.");
        }
        var folder = CreateRoot(parent, id);
        foreach (var (path, value) in files) { var target = Path.Combine(folder, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, value!.GetValue<string>(), new UTF8Encoding(false)); }
        return folder;
    }
    public static string CreatePackage(string parent, JsonObject input, Func<string, UiLayoutDocument> validateLayout)
    {
        var id = input["id"]?.GetValue<string>() ?? ""; ValidateId(id);
        var page = input["page"]?.Deserialize(ExtensionJson.Default.ExtensionPage) ?? new ExtensionPage { Root = new() { Type = "stack", Children = [new() { Type = "text", Text = input["name"]?.GetValue<string>() ?? id }] } };
        if (input["layout"] is JsonValue value && value.TryGetValue<string>(out var layout))
        {
            validateLayout(layout);
            page.Actions["apply-layout"] = new("layout", "ui", new() { ["operation"] = "apply", ["layout"] = layout });
            page.Root = new() { Type = "stack", Children = [page.Root, new() { Type = "button", Text = L10n.T("Extensions.ApplyLayout"), Action = "apply-layout" }] };
        }
        var manifest = new PluginManifest { Id = id, Name = input["name"]?.GetValue<string>() ?? id, Version = input["version"]?.GetValue<string>() ?? "1.0.0",
            Author = "Author", Type = "extension", ContractVersion = 2, Runtime = "declarative", PageEntry = "page.json",
            Permissions = ["ui-extend", "navigation", "player-control", "music-read"], RequiredCapabilities = ["runtime.declarative.v1", "ui.tree.v2"],
            Contributions = input["contributions"]?.Deserialize(ExtensionJson.Default.ListExtensionContribution) ?? [] };
        // 不允许生成的数据插件申请后台进程、联网、任意路径或自动审批权限。
        manifest.Validate();
        using var checkedPage = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(page, ExtensionJson.Default.ExtensionPage)); ExtensionContract.ReadPage(checkedPage);
        var folder = CreateRoot(parent, id);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(manifest, AppStorage.Json));
        File.WriteAllText(Path.Combine(folder, "page.json"), JsonSerializer.Serialize(page, ExtensionJson.Default.ExtensionPage));
        File.WriteAllText(Path.Combine(folder, "README.md"), "# " + manifest.Name + "\n\n数据驱动的 Nonet 界面插件。清单声明稳定 ID 与版本；页面动作由宿主权限服务处理，不包含可执行代码。\n");
        var dist = Path.Combine(folder, "dist"); Directory.CreateDirectory(dist);
        var package = Path.Combine(dist, id + "-" + manifest.Version + ".impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) { zip.CreateEntryFromFile(Path.Combine(folder, "manifest.json"), "manifest.json"); zip.CreateEntryFromFile(Path.Combine(folder, "page.json"), "page.json"); }
        PluginPackageInspector.Inspect(package); return package;
    }
}
