using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;
namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>异步插件可以请求交互，但只有宿主确认和选择才能产生用户操作上下文。</summary>
public sealed partial class ExtensionSession
{
    private async Task<JsonObject> InteractionAsync(JsonObject args)
    {
        var service = args["service"]?.GetValue<string>() ?? "";
        var data = args["arguments"] as JsonObject ?? new();
        if (service is not ("files" or "files.pick" or "config" or "dialogs.prompt" or "dialogs.confirm"))
            throw new InvalidDataException("Unsupported interactive service.");
        // 模型不能更改自身审批；这里只允许打开配置。文件选择授权不随自动审批取消。
        if (service == "config" && data.Count > 0) throw new InvalidDataException("Only opening configuration is interactive.");
        if (!await PlayerDialog.Confirm(Owner, Manifest.Name, L10n.T("Extensions.InteractionRequest") + service + "\n" + data.ToJsonString(), L10n.T("Common.Confirm")))
            return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
        var prior = _gesture;
        try { _gesture = true; return await ServiceAsync(new(Guid.NewGuid().ToString("N"), service, (JsonObject)data.DeepClone())); }
        finally { _gesture = prior; }
    }
    private async Task<JsonObject> ApprovalAsync(JsonObject args)
    {
        if (!Manifest.SupportsApprovalModes) throw new InvalidDataException("Approval modes unavailable.");
        var operation = args["operation"]?.GetValue<string>() ?? "open";
        if (operation == "approval.read") return new() { ["success"] = true, ["mode"] = Manifest.ApprovalMode };
        if (operation != "approval.set" || !_gesture) throw new InvalidDataException("Approval changes require a direct user action.");
        var mode = args["mode"]?.GetValue<string>() ?? "";
        if (!PluginApprovalPolicy.IsMode(mode)) throw new InvalidDataException("Invalid approval mode.");
        if (mode != Manifest.ApprovalMode && mode != "ask" &&
            !await PlayerDialog.Confirm(Owner, L10n.T("Extensions.ApprovalTitle"),
                L10n.T(mode == "full" ? "Extensions.ApprovalFullWarning" : "Extensions.ApprovalAssistWarning"), L10n.T("Common.Confirm")))
            return new() { ["success"] = false, ["mode"] = Manifest.ApprovalMode, ["reason"] = "User declined." };
        _manager.SetApprovalMode(Manifest, mode);
        return new() { ["success"] = true, ["mode"] = mode };
    }
}
