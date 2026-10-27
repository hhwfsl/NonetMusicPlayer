using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>所有插件形态共用的类型级风格服务；覆盖只属于调用插件，不能移除其他插件的层。</summary>
public sealed partial class ExtensionSession
{
    private async Task<JsonObject> StylesServiceAsync(JsonObject args)
    {
        if (!Manifest.Permissions.Contains("ui-extend")) throw new InvalidDataException("UI extension permission required.");
        var operation = args["operation"]?.GetValue<string>() ?? "catalog";
        if (operation == "catalog") return Owner.StyleLayers.Catalog();
        if (operation == "composition-catalog") return Owner.InterfaceCatalog();
        if (operation == "resolve-page-transition")
        {
            var from = args["from"]?.GetValue<string>() ?? _manager.ExtensionViewModel.Page;
            var to = args["to"]?.GetValue<string>() ?? "";
            if (from.Length > 200 || to.Length is 0 or > 200 || from.Any(char.IsControl) || to.Any(char.IsControl)) throw new InvalidDataException("Invalid transition route.");
            return new() { ["success"] = true, ["profile"] = JsonSerializer.SerializeToNode(Owner.ResolvePageTransition(from, to), StyleSheetJson.Default.ResolvedPageTransition) };
        }
        if (operation is "apply" or "reset")
        {
            var layers = operation == "reset" ? [] : args["layers"]?.Deserialize(StyleSheetJson.Default.ListStyleSheet) ?? throw new InvalidDataException("Missing style layers.");
            foreach (var sheet in layers) StyleSheetContract.Validate(sheet);
            // 信任由插件启用审批建立；后台重启恢复风格不重复打断，但不能提升插件权限。
            Owner.ApplyInterfaceLayers(Manifest.Id, layers);
            return new() { ["success"] = true, ["count"] = layers.Count };
        }
        if (operation == "morph")
        {
            if (!_gesture) throw new InvalidDataException("Morph requires a user gesture.");
            var target = args["target"]?.GetValue<string>() ?? "";
            var control = Owner.ElementLayouts.FindTarget(target) ?? throw new InvalidDataException("Morph target not found.");
            await Owner.MorphContainerAsync(control, _lifetime.Token);
            return new() { ["success"] = true };
        }
        throw new InvalidDataException("Unknown style operation.");
    }
}
