using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>扩展目录独立于应用版本；新增域采用可选能力，不修改已冻结的 v2 接口。</summary>
public static class UniversalExtensionContract
{
    public static readonly string[] Capabilities = ["host.catalog.v1", "host.query.v1", "storage.v1", "logging.v1", "events.publish.v1", "workflow.v1", "ui.surfaces.v1", "ui.native.v1", "files.transactions.v1", "audio.pcm.v1", "metadata.v1", "artwork.v1", "maintenance.v1"];
    public static readonly string[] Services = ["host", "query", "storage", "log", "events", "files", "metadata", "artwork", "maintenance"];
    public static readonly string[] Slots = ["shell.workspace", "shell.navigation", "shell.player", "shell.titlebar", "settings.sections", "navigation.items", "tray.actions", "album.more", "artist.more", "history.more", "selection.more", "library.more"];
    public static readonly string[] Hooks = ["command.before", "playback.before", "navigation.before", "import.before"];
    public static bool IsService(string name) => Services.Contains(name, StringComparer.Ordinal);
    public static bool MatchesEvent(IEnumerable<string> subscriptions, string name)
        => subscriptions.Any(s => s == name || s.EndsWith(".*", StringComparison.Ordinal) && name.StartsWith(s[..^1], StringComparison.Ordinal));

    /// <summary>钩子只返回数据决定，不得在同步决策中回调宿主、执行另一个命令或改变审批。</summary>
    public static void ValidateDecision(ExtensionHookDecision value)
    {
        if (value.Data.ToJsonString().Length > 32000) throw new InvalidDataException("Hook decision exceeds limit.");
    }
}

public sealed record ExtensionHook(string Name, JsonObject Data);
public sealed class ExtensionHookDecision
{
    public bool Cancel { get; set; }
    public JsonObject Data { get; set; } = new();
}

/// <summary>可选流程接口。旧 INonetExtension 实现无需添加方法；进程通过 extension.hook 提供相同数据。</summary>
public interface INonetWorkflowExtension
{
    ValueTask<ExtensionHookDecision> EvaluateAsync(ExtensionHook hook, CancellationToken cancellationToken);
}

/// <summary>可信托管扩展的可选原生视图工厂；SDK 不引用 Avalonia，宿主负责验证视图类型。</summary>
public interface INonetNativeViewExtension
{
    INonetNativeView CreateView(ExtensionViewContext context);
}

public interface INonetNativeView : IAsyncDisposable
{
    object View { get; }
    void Update(ExtensionFrame frame);
}

/// <summary>原生视图仍通过公共服务调用宿主，不取得播放器、数据库或主窗口内部引用。</summary>
public sealed class ExtensionViewContext(string slot, JsonObject context,
    Func<ExtensionHostRequest, bool, CancellationToken, Task<JsonObject>> request,
    Func<string, JsonObject, Task> invoke)
{
    public string Slot { get; } = slot;
    public JsonObject Context { get; } = (JsonObject)context.DeepClone();
    public Task<JsonObject> RequestAsync(string service, JsonObject arguments, CancellationToken cancellationToken = default)
        => request(new(Guid.NewGuid().ToString("N"), service, arguments), false, cancellationToken);
    /// <summary>只在明确的按钮、菜单或键盘动作中使用；宿主仍执行文件选择和权限确认。</summary>
    public Task<JsonObject> RequestFromUserAsync(string service, JsonObject arguments, CancellationToken cancellationToken = default)
        => request(new(Guid.NewGuid().ToString("N"), service, arguments), true, cancellationToken);
    public Task InvokeAsync(string action, JsonObject values) => invoke(action, values);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ExtensionHook))]
[JsonSerializable(typeof(ExtensionHookDecision))]
public partial class UniversalExtensionJson : JsonSerializerContext;
