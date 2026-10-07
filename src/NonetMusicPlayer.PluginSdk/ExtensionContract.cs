using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>通用扩展协议基线 v2。新增能力通过协商提供，宿主必须继续支持已发布的基线。</summary>
public static class ExtensionContract
{
    public const int Version = 2;
    public static readonly string[] Capabilities = ["ui.tree.v2", "lyrics.v1", "dialogs.v1", "state.v1", "actions.v1", "events.v1", "commands.v1", "services.v1", "files.pick.v1", "ui.layout-bind.v1", "ui.context-menu.v1", "ui.controls.v1", "dialogs.prompt.v1", "approvals.v1", "ui.workspace.v1", "runtime.declarative.v1", "development.v1", "ui.assets.v1", "ui.composer.v1", "config.approval.v1", "interaction.v1", "ui.overlay.v1", "ui.large-text.v1", .. UniversalExtensionContract.Capabilities];
    public static readonly string[] Slots = ["lyrics.more", "song.more", "playlist.more", "player.actions", "titlebar.actions", "home.cards", "overlay", "page.music", "page.lyrics", "page.songs", "page.history", "page.albums", "page.artists", "page.playlist", "page.statistics", .. UniversalExtensionContract.Slots];
    public static readonly string[] Permissions = ["process", "network", "navigation", "player-control", "music-read", "library-write",
        "settings-write", "window-control", "plugins-control", "lyrics-write", "user-files", "in-process", "ui-extend", "plugin-services", "plugin-development", "workflow", "native-ui", "audio-tags", "audio-processing"];
    public static ExtensionPage ReadPage(Stream input)
    {
        using var buffer = new MemoryStream(); input.CopyTo(buffer);
        if (buffer.Length > 128 * 1024) throw new InvalidDataException("Extension page exceeds 128 KiB.");
        var page = JsonSerializer.Deserialize(buffer.ToArray(), ExtensionJson.Default.ExtensionPage) ?? throw new InvalidDataException("Empty page.");
        if (page.SchemaVersion != 2 || page.Root is null) throw new InvalidDataException("Unsupported extension page.");
        page.Actions ??= []; page.InitialState ??= new();
        var count = 0;
        void Visit(ExtensionNode node, int depth)
        {
            if (++count > 512 || depth > 24) throw new InvalidDataException("Extension tree is too large.");
            if (node.Type is not ("grid" or "stack" or "border" or "scroll" or "text" or "selectable-text" or "input" or "button" or "toggle" or "repeat" or "select" or "slider" or "image" or "checkbox" or "icon"))
                throw new InvalidDataException("Unknown UI primitive: " + node.Type);
            if (node.Text.Length > 24000 || node.Action.Length > 100 || node.Bind.Length > 200 || node.Input.Length > 100
                || !double.IsFinite(node.FontSize) || node.FontSize is < 0 or > 96 || !double.IsFinite(node.CornerRadius) || node.CornerRadius is < 0 or > 100 || !double.IsFinite(node.MaxHeight) || node.MaxHeight is < 0 or > 4000 || node.Width is < 0 or > 4000 || node.Height is < 0 or > 4000)
                throw new InvalidDataException("Invalid UI property.");
            if (node.Row is < 0 or > 31 || node.Column is < 0 or > 31 || !double.IsFinite(node.Spacing) || node.Spacing is < 0 or > 64
                || !double.IsFinite(node.Minimum) || !double.IsFinite(node.Maximum) || node.Minimum > node.Maximum || node.Tooltip.Length > 2000)
                throw new InvalidDataException("Invalid layout limits.");
            foreach (var dimension in (node.Rows + "," + node.Columns).Split(','))
            {
                var value = dimension.Trim();
                if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase) || value == "*") continue;
                if (value.EndsWith('*')) value = value[..^1];
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size is < 0 or > 4000)
                    throw new InvalidDataException("Invalid grid dimensions.");
            }
            foreach (var thickness in new[] { node.Padding, node.Margin })
            {
                var values = thickness.Split(',');
                if (values.Length is not (1 or 2 or 4) || values.Any(value => !double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || Math.Abs(size) > 512))
                    throw new InvalidDataException("Invalid layout spacing.");
            }
            if (node.Asset.Length > 0) PluginPathPolicy.ValidateRelativePath(node.Asset);
            if (node.SelectedBind.Length > 200 || node.ColumnsBind.Length > 200 || node.ContextActions.Count > 16 || node.SelectedIf.Length > 200 || node.Variant is not ("" or "ghost" or "pill" or "row" or "accent")) throw new InvalidDataException("Invalid optional UI capability.");
            foreach (var action in node.ContextActions)
            {
                if (action.Type != "button" || action.Action.Length == 0 || action.Text.Length is 0 or > 200 || action.ContextActions.Count != 0 || action.Children.Count != 0)
                    throw new InvalidDataException("Invalid context action.");
                Visit(action, depth + 1);
            }
            foreach (var child in node.Children) Visit(child, depth + 1);
            if (node.Template is not null) Visit(node.Template, depth + 1);
        }
        Visit(page.Root, 0);
        if (page.Actions.Count > 64 || page.InitialState.ToJsonString().Length > 128000) throw new InvalidDataException("Declarative state/actions exceed limit.");
        foreach (var (name, action) in page.Actions)
        {
            if (name.Length is 0 or > 100 || !UniversalExtensionContract.IsService(action.Service) && action.Service is not ("commands" or "ui" or "lyrics" or "dialogs.notify") || action.Arguments.ToJsonString().Length > 128000)
                throw new InvalidDataException("Invalid declarative action.");
        }
        return page;
    }
    public static void ValidateFrame(ExtensionFrame frame)
    {
        // 状态是数据而不是代码，控制 UI 的输入仍受控件树上限约束。
        if (frame.State.ToJsonString().Length > (frame.LargeTextState ? 30_000_000 : 2_000_000) || frame.Requests.Count > 16) throw new InvalidDataException("Extension state is too large.");
        foreach (var request in frame.Requests)
            if (request.Id.Length is 0 or > 100 || !UniversalExtensionContract.IsService(request.Service) && request.Service is not ("commands" or "catalog" or "config" or "interaction" or "files.pick" or "lyrics" or "dialogs.confirm" or "dialogs.notify" or "dialogs.prompt" or "ui" or "development") && !request.Service.StartsWith("plugin:", StringComparison.Ordinal)
                || request.Arguments.ToJsonString().Length > (request.Service is "ui" or "development" or "lyrics" ? 128000 : 32000)) throw new InvalidDataException("Invalid service request.");
    }
}
public sealed class ExtensionPage
{
    public int SchemaVersion { get; set; } = 2;
    public ExtensionNode Root { get; set; } = new();
    public bool OwnsHeader { get; set; }
    /// <summary>纯数据扩展的可选初始状态和动作；旧页面省略这些字段仍有效。</summary>
    public JsonObject InitialState { get; set; } = new();
    public Dictionary<string, ExtensionHostRequest> Actions { get; set; } = [];
}
/// <summary>无 GUI 依赖的控件描述；状态路径只做键查找，不求值脚本或表达式。</summary>
public sealed class ExtensionNode
{
    public string Type { get; set; } = "text";
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Bind { get; set; } = "";
    public string Input { get; set; } = "";
    public string Action { get; set; } = "";
    public string EnterAction { get; set; } = "";
    public string Parameter { get; set; } = "";
    public string VisibleIf { get; set; } = "";
    public string Columns { get; set; } = "*";
    public string Rows { get; set; } = "Auto";
    public string ColumnsBind { get; set; } = "";
    public string Align { get; set; } = "stretch";
    /// <summary>可选的原生控件样式及选中状态，不接受插件提供的任意样式代码。</summary>
    public string Variant { get; set; } = "";
    public string SelectedIf { get; set; } = "";
    /// <summary>可选绑定选项、圆角与高度限制，缺省值保持旧页面布局。</summary>
    public string SelectedBind { get; set; } = "";
    public double CornerRadius { get; set; } = 12;
    public double MaxHeight { get; set; }
    public bool Borderless { get; set; }
    public bool OpenMenuOnClick { get; set; }
    public string Background { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Tooltip { get; set; } = "";
    public string Padding { get; set; } = "0";
    public string Margin { get; set; } = "0";
    public int Row { get; set; }
    public int Column { get; set; }
    public double Spacing { get; set; } = 8;
    public double FontSize { get; set; } = 14;
    public double Width { get; set; }
    public double Height { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public string Asset { get; set; } = "";
    public bool Horizontal { get; set; }
    public bool Multiline { get; set; }
    public bool ScrollToEnd { get; set; }
    public List<ExtensionNode> Children { get; set; } = [];
    public List<ExtensionNode> ContextActions { get; set; } = [];
    public ExtensionNode? Template { get; set; }
}
public sealed record ExtensionContribution(string Slot, string Label, string Action, string Icon = "")
{
    public ExtensionNode? View { get; init; }
    /// <summary>可信托管插件可选原生视图；旧贡献省略时仍使用控件树。</summary>
    public bool Native { get; init; }
    public ExtensionOverlayOptions? Overlay { get; init; }
}
/// <summary>独立浮层的可选窗口表现；不授予主窗口或 OS 内部访问。</summary>
public sealed record ExtensionOverlayOptions(double Width = 340, double Height = 260, bool Transparent = false, bool Topmost = true, bool ShowInTaskbar = true);
public sealed class ExtensionFrame
{
    /// <summary>可选长文本状态，最多 30 MB；旧扩展仍使用原 2 MB 上限。</summary>
    public bool LargeTextState { get; set; }
    public long Revision { get; set; }
    public bool Busy { get; set; }
    public JsonObject State { get; set; } = new();
    public List<ExtensionHostRequest> Requests { get; set; } = [];
}
public sealed record ExtensionHostRequest(string Id, string Service, JsonObject Arguments);
public sealed record ExtensionInvocation(string Action, JsonObject Values);
public sealed record ExtensionInitialization(int ContractVersion, string Configuration, string StorageDirectory, string[] Capabilities);
public sealed record ExtensionEvent(string Name, JsonObject Data);
/// <summary>托管扩展与进程扩展使用同一数据协议。托管扩展具有普通用户权限，不是安全沙箱。</summary>
public interface INonetExtension : IAsyncDisposable
{
    ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization initialization, CancellationToken cancellationToken);
    ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation invocation, CancellationToken cancellationToken);
    ValueTask<ExtensionFrame> SyncAsync(CancellationToken cancellationToken);
    ValueTask<ExtensionFrame> CompleteAsync(string requestId, JsonObject result, CancellationToken cancellationToken);
    ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken cancellationToken);
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, MaxDepth = 32)]
[JsonSerializable(typeof(List<ExtensionContribution>))]
[JsonSerializable(typeof(ExtensionPage))]
[JsonSerializable(typeof(ExtensionFrame))]
[JsonSerializable(typeof(ExtensionInvocation))]
[JsonSerializable(typeof(ExtensionInitialization))]
[JsonSerializable(typeof(ExtensionEvent))]
public partial class ExtensionJson : JsonSerializerContext;
