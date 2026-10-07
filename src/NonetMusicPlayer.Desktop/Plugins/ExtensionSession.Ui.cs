using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>界面扩展使用有界数据协议；不把模型提供的路径或代码作为系统命令执行。</summary>
public sealed partial class ExtensionSession
{
    private JsonObject ExtensionCatalog()
    {
        var catalog = UniversalPluginCommandPolicy.CatalogFor(Manifest.Permissions);
        if (Manifest.Permissions.Contains("ui-extend"))
        {
            var commands = catalog["commands"]!.AsArray();
            foreach (var operation in new[] { "read", "apply", "reset" })
                commands.Add(new JsonObject { ["operation"] = "extension.ui." + operation, ["usage"] = operation == "apply" ? "[layout JSON string]" : "no arguments", ["confirmation"] = operation != "read" });
            if (Manifest.Permissions.Contains("plugins-control") && Manifest.Permissions.Contains("plugin-development"))
            {
                commands.Add(new JsonObject { ["operation"] = "extension.package", ["usage"] = "one JSON string: {id:custom.name,name,version,layout:optional layout JSON string,page:{schemaVersion:2,root:{...}},contributions:[...]}. Generates and installs a data-only plugin; never executable code.", ["confirmation"] = true });
                commands.Add(new JsonObject { ["operation"] = "extension.source", ["usage"] = "one JSON string: {id,files:{relativePath:UTF8 text}}. Creates a NEW C# source folder, never builds/runs code.", ["confirmation"] = true });
                commands.Add(new JsonObject { ["operation"] = "extension.template", ["usage"] = "optional allowlisted relative file; get fixed template docs/source (README.md, docs/AGENT_GUIDE.md, plugin/PluginDefinition.cs, plugin/manifest.json, plugin/page.json, Directory.Build.props, build.ps1, host/UNIVERSAL_EXTENSIONS.md, host/EXTENSIONS.md)", ["confirmation"] = false });
            }
        }
        catalog["commands"]!.AsArray().Add(new JsonObject { ["operation"] = "extension.service", ["usage"] = "one JSON string: {service,arguments:{...}}. Use host to discover capabilities, services, slots and hooks. All calls enforce manifest permissions; interactive files/config/dialogs require host confirmation. No paths, shell, arbitrary reads, secret configuration or approval escalation." });
        return catalog;
    }
    private async Task<bool> ConfirmExtensionAsync(string operation, bool required, string description)
        => !PluginApprovalPolicy.RequiresConfirmation(Manifest.ApprovalMode, operation, required)
            || await PlayerDialog.Confirm(Owner, Manifest.Name, description, L10n.T("Common.Confirm"));
    private async Task<JsonObject> UiServiceAsync(JsonObject args)
    {
        if (!Manifest.Permissions.Contains("ui-extend")) throw new InvalidDataException("UI extension permission required.");
        var operation = args["operation"]?.GetValue<string>() ?? "read";
        if (operation == "read")
            return new() { ["success"] = true, ["layout"] = Owner.LayoutConfiguration.AppliedJson, ["defaultLayout"] = UiLayoutService.DefaultJson,
                ["slots"] = new JsonArray(ExtensionContract.Slots.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                ["notes"] = "Workspace: Navigation/Content/Player; required playback IDs must occur exactly once. Page slots accept ExtensionNode trees. No arbitrary XAML, scripts, OS shell, or filesystem access. Settings and terminal remain recovery entry points." };
        if (operation is not ("apply" or "reset")) throw new InvalidDataException("Unknown UI operation.");
        var json = operation == "reset" ? UiLayoutService.DefaultJson : args["layout"]?.GetValue<string>() ?? "";
        Owner.LayoutConfiguration.Read(json);
        if (!await ConfirmExtensionAsync("ui.layout." + operation, true, L10n.T("Extensions.ApplyLayoutConfirm")))
            return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
        if (operation == "reset") Owner.RestoreDefaultLayout(); else Owner.ApplyLayoutConfiguration(json);
        return new() { ["success"] = true };
    }
    private async Task<JsonObject> DevelopmentServiceAsync(JsonObject args)
    {
        if (!Manifest.Permissions.Contains("ui-extend") || !Manifest.Permissions.Contains("plugins-control") || !Manifest.Permissions.Contains("plugin-development")) throw new InvalidDataException("Development permissions required.");
        var operation = args["operation"]?.GetValue<string>() ?? "";
        if (operation is not ("package" or "source")) throw new InvalidDataException("Unknown development operation.");
        // 文件写入发生在固定、独立的新项目目录；不会覆盖已有插件或执行生成代码。
        var id = args["id"]?.GetValue<string>() ?? "";
        PluginDevelopmentService.ValidateId(id);
        if (!await ConfirmExtensionAsync("development." + operation, true, L10n.Format("Extensions.CreatePluginConfirm", id)))
            return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
        var vm = _manager.ExtensionViewModel;
        var folder = vm.Settings.PluginDevelopmentFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            var chosen = await Owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                { Title = L10n.T("Extensions.SelectProjectFolder") });
            folder = chosen.FirstOrDefault()?.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(folder)) return new() { ["success"] = false, ["reason"] = "User declined workspace selection; do not retry." };
            vm.Settings.PluginDevelopmentFolder = Path.GetFullPath(folder); vm.Save();
        }
        if (operation == "source")
        {
            var project = PluginDevelopmentService.WriteSource(folder, id, args["files"]?.AsObject() ?? throw new InvalidDataException("Missing source files."));
            return new() { ["success"] = true, ["project"] = project, ["note"] = "Source only. Not compiled, executed, or installed. Build and review it with the fixed template instructions." };
        }
        var package = PluginDevelopmentService.CreatePackage(folder, args, Owner.LayoutConfiguration.Read);
        var plugin = _manager.Install(package); _manager.SetEnabled(plugin, true);
        return new() { ["success"] = true, ["id"] = plugin.Id, ["package"] = package };
    }
}
