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
            foreach (var operation in new[] { "read", "elements", "update-elements", "reset-elements", "apply", "reset" })
                commands.Add(new JsonObject { ["operation"] = "extension.ui." + operation, ["usage"] = operation == "apply" ? "[legacy layout string; prefer extension.service ui/update-elements]" : operation == "update-elements" ? "use extension.service: {service:ui,arguments:{operation:update-elements,patches:[{target,properties,parent?,index?}]}}" : "no arguments", ["confirmation"] = operation != "read" });
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
        if (operation is "mount-element" or "unmount-element")
        {
            if (!await ConfirmExtensionAsync("ui.elements." + operation, true, L10n.T("Extensions.ApplyLayoutConfirm")))
                return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
            if (operation == "mount-element") Owner.MountPluginElement(Manifest, args);
            else Owner.UnmountPluginElement(Manifest.Id, args["key"]?.GetValue<string>());
            return new() { ["success"] = true };
        }
        if (operation == "elements") return Owner.ElementLayouts.Catalog(args["offset"]?.GetValue<int>() ?? 0, args["limit"]?.GetValue<int>() ?? 100);
        if (operation is "update-elements" or "reset-elements")
        {
            if (!await ConfirmExtensionAsync("ui.elements." + operation, true, L10n.T("Extensions.ApplyLayoutConfirm")))
                return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
            if (operation == "reset-elements") Owner.ElementLayouts.Remove(Manifest.Id);
            else Owner.ElementLayouts.Apply(Manifest.Id, args["patches"]?.AsArray() ?? throw new InvalidDataException("Missing patches."));
            return new() { ["success"] = true };
        }
        if (operation == "read")
            return new() { ["success"] = true, ["layout"] = Owner.LayoutConfiguration.AppliedJson, ["defaultLayout"] = UiLayoutService.DefaultJson,
                ["slots"] = new JsonArray(ExtensionContract.Slots.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                ["elementCapability"] = ElementLayoutContract.Capability,
                ["notes"] = "Use operation=elements to discover every realized control (including template controls); update-elements changes properties and Panel parents with reversible plugin-owned overrides. ui.element contributions insert custom controls at named anchors; mount-element/unmount-element also accept runtime IDs. Legacy workspace: Navigation/Content/Player; required playback IDs must occur exactly once. Page slots accept ExtensionNode trees. No arbitrary XAML, scripts, OS shell, or filesystem access. Settings and terminal remain recovery entry points." };
        if (operation is not ("apply" or "reset")) throw new InvalidDataException("Unknown UI operation.");
        var json = operation == "reset" ? UiLayoutService.DefaultJson : args["layout"]?.GetValue<string>() ?? "";
        Owner.LayoutConfiguration.Read(json);
        if (!await ConfirmExtensionAsync("ui.layout." + operation, true, L10n.T("Extensions.ApplyLayoutConfirm")))
            return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
        if (operation == "reset") Owner.ResetPluginLayout(Manifest.Id); else Owner.ApplyPluginLayout(Manifest.Id, json);
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
        // 生成项目属于应用管理数据，不再要求用户选择开发目录；保留原服务协议。
        var folder = Path.Combine(vm.Storage.Root, "PluginProjects");
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
