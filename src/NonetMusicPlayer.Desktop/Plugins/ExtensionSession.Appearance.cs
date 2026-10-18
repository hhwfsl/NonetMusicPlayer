using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>只处理当前用户已选背景，不接受插件提供的文件路径；写入沿用设置权限及审批。</summary>
public sealed partial class ExtensionSession
{
    private async Task<JsonObject> AppearanceServiceAsync(JsonObject args)
    {
        if (!Manifest.Permissions.Contains("ui-extend")) throw new InvalidDataException("UI extension permission required.");
        var vm = _manager.ExtensionViewModel;
        var operation = args["operation"]?.GetValue<string>() ?? "read";
        if (operation == "extract-background-accent")
        {
            try { var color = await vm.ExtractBackgroundAccentAsync(_lifetime.Token); return new() { ["success"] = true, ["color"] = color, ["found"] = color is not null }; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { AppLog.Warning("Background", "Background color extraction failed", error); return new() { ["success"] = false, ["message"] = L10n.T("Background.AccentFailed") }; }
        }
        if (operation == "set-auto-accent")
        {
            if (!Manifest.Permissions.Contains("settings-write")) throw new InvalidDataException("Settings permission required.");
            if (args["enabled"]?.GetValueKind() is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)) throw new InvalidDataException("Boolean enabled required.");
            if (!await ConfirmExtensionAsync("settings.set", true, L10n.T("Background.AutomaticAccent"))) return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
            vm.Settings.AccentFromBackground = args["enabled"]!.GetValue<bool>(); vm.ApplySettings();
            await vm.BackgroundAccentUpdate;
        }
        else if (operation != "read") throw new InvalidDataException("Unknown appearance operation.");
        return new() { ["success"] = true, ["accent"] = vm.Settings.Accent, ["automatic"] = vm.Settings.AccentFromBackground,
            ["hasBackground"] = !string.IsNullOrWhiteSpace(vm.Settings.BackgroundImagePath), ["pending"] = !vm.BackgroundAccentUpdate.IsCompleted,
            ["motionEnabled"] = InterfaceMotion.IsEnabled, ["lyricsTransition"] = "slide" };
    }
}
