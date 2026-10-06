using System.Text.Json;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>宿主与打包工具共用的原生部件规范，不解析代码、绑定表达式、外部网址或 XAML。</summary>
public static class PluginPageContract
{
    public const int MaximumBytes = 32_768;
    public static PluginPageDefinition Read(Stream stream, IEnumerable<string>? permissions = null)
    {
        var granted = (permissions ?? []).ToHashSet(StringComparer.Ordinal);
        using var content = new MemoryStream();
        var block = new byte[4096];
        int read;
        while ((read = stream.Read(block)) != 0)
        {
            if (content.Length + read > MaximumBytes) throw new InvalidDataException("插件页面不能超过 32 KiB。");
            content.Write(block, 0, read);
        }
        using var document = JsonDocument.Parse(content.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        Fields(root, "schemaVersion", "title", "description", "widgets", "flows");
        if (Integer(root, "schemaVersion", 1, 1) != 1) throw new InvalidDataException("不支持此 UI 页面版本。");
        var title = Text(root, "title", 100, required: true);
        var description = Text(root, "description", 2000);
        if (!root.TryGetProperty("widgets", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 12)
            throw new InvalidDataException("插件页面必须包含 1–12 个原生部件。");
        var widgets = new List<PluginPageWidget>();
        var games = 0; var pets = 0; var editors = 0;
        foreach (var item in items.EnumerateArray())
        {
            var type = Text(item, "type", 32, required: true);
            var widgetTitle = Text(item, "title", 100);
            var text = Text(item, "text", 4000);
            SnakeGameOptions? snake = null;
            PluginPetOptions? pet = null; IReadOnlyList<PluginActionButton>? actions = null;
            if (type == "snake")
            {
                Fields(item, "type", "title", "text", "columns", "rows", "tickMilliseconds", "wrapWalls", "background", "snake", "food");
                if (++games > 1) throw new InvalidDataException("每个插件页最多一个游戏计时器。");
                snake = new(Integer(item, "columns", 12, 40, 24), Integer(item, "rows", 10, 30, 18),
                    Integer(item, "tickMilliseconds", 80, 600, 150), Boolean(item, "wrapWalls"),
                    Color(item, "background", "#111827"), Color(item, "snake", "#9CCF92"), Color(item, "food", "#F0526C"));
            }
            else if (type == "text") Fields(item, "type", "title", "text");
            else if (type == "actions") { Fields(item, "type", "title", "text", "actions"); actions = Actions(item, granted); }
            else if (type == "listening-summary") { Fields(item, "type", "title", "text"); Require(granted, "statistics"); }
            else if (type == "agent-chat")
            {
                Fields(item, "type", "title", "text"); Require(granted, "agent-control");
                if (++editors > 1) throw new InvalidDataException("每个插件页面最多一个交互工具。");
            }
            else if (type == "lyrics-search")
            {
                Fields(item, "type", "title", "text"); Require(granted, "lyrics-search");
                if (++editors > 1) throw new InvalidDataException("每个插件页面最多一个歌词工具。");
            }
            else if (type == "lyrics-timing")
            {
                Fields(item, "type", "title", "text"); Require(granted, "lyrics-editor"); Require(granted, "player-control");
                if (++editors > 1) throw new InvalidDataException("每个插件页面最多一个歌词标注部件。");
            }
            else if (type == "pet")
            {
                Fields(item, "type", "title", "text", "name", "body", "accent", "floating", "actions"); Require(granted, "desktop-widget");
                if (++pets > 1) throw new InvalidDataException("每个插件最多一个桌宠。");
                pet = new(Text(item, "name", 60) is { Length: > 0 } name ? name : "音乐猫", Color(item, "body", "#B8DCF2"), Color(item, "accent", "#F19BAC"), Boolean(item, "floating", true));
                actions = Actions(item, granted);
            }
            else throw new InvalidDataException("插件页面部件类型不受支持，不接受脚本、HTML 或 XAML。");
            widgets.Add(new(type, widgetTitle, text, snake, actions, pet));
        }
        var flows = new List<PluginFlow>();
        if (root.TryGetProperty("flows", out var rules))
        {
            if (rules.ValueKind != JsonValueKind.Array || rules.GetArrayLength() > 4) throw new InvalidDataException("插件最多定义 4 个流程。");
            foreach (var rule in rules.EnumerateArray())
            {
                Fields(rule, "event", "action", "intervalSeconds"); var trigger = Text(rule, "event", 24, true);
                if (trigger is not ("track-changed" or "interval")) throw new InvalidDataException("不支持此流程事件。");
                if (!rule.TryGetProperty("action", out var actionJson)) throw new InvalidDataException("流程缺少 action。");
                var action = Action(actionJson, granted, true);
                if (action.Kind != "show-message") throw new InvalidDataException("自动流程只允许显示消息；播放和导航动作必须由用户触发。");
                flows.Add(new(trigger, action, Integer(rule, "intervalSeconds", 60, 3600, 60)));
            }
            if (flows.Count > 0 && pets == 0) throw new InvalidDataException("消息流程需要 pet 部件。");
        }
        return new(title, description, widgets, flows);
    }
    private static IReadOnlyList<PluginActionButton> Actions(JsonElement item, HashSet<string> permissions)
    {
        if (!item.TryGetProperty("actions", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 8)
            throw new InvalidDataException("动作部件需包含 1–8 个按钮。");
        return array.EnumerateArray().Select(button =>
        {
            Fields(button, "label", "action");
            if (!button.TryGetProperty("action", out var action)) throw new InvalidDataException("按钮缺少 action。");
            return new PluginActionButton(Text(button, "label", 60, true), Action(action, permissions));
        }).ToArray();
    }
    private static PluginHostAction Action(JsonElement item, HashSet<string> permissions, bool allowMessage = false)
    {
        Fields(item, "kind", "value"); var kind = Text(item, "kind", 32, true); var value = Text(item, "value", 200);
        if (value.Any(char.IsControl)) throw new InvalidDataException("动作参数不能包含控制字符。");
        switch (kind)
        {
            case "play-pause": case "previous": case "next": case "favorite": Require(permissions, "player-control"); if (value.Length != 0) throw new InvalidDataException("播放动作不接受参数。"); break;
            case "navigate": Require(permissions, "navigation"); if (value is not ("library" or "albums" or "artists" or "statistics" or "settings" or "lyrics" or "playlist:liked")) throw new InvalidDataException("插件导航仅允许内建页面。"); break;
            case "search": Require(permissions, "navigation"); break;
            case "show-message": Require(permissions, "desktop-widget"); if (!allowMessage) throw new InvalidDataException("消息动作仅用于桌宠流程。"); break;
            default: throw new InvalidDataException("不支持此主机动作。");
        }
        return new(kind, value);
    }
    private static void Require(HashSet<string> permissions, string value) { if (!permissions.Contains(value)) throw new InvalidDataException("插件缺少权限：" + value); }
    private static void Fields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("插件页面字段必须是对象。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException("插件页面含未知或重复字段：" + property.Name);
    }
    private static string Text(JsonElement item, string name, int max, bool required = false)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            if (required) throw new InvalidDataException("插件页面缺少 " + name + "。");
            return "";
        }
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException(name + " 必须是文本。");
        var text = value.GetString()!;
        if (text.Length > max || required && string.IsNullOrWhiteSpace(text)) throw new InvalidDataException(name + " 长度不合法。");
        return text;
    }
    private static int Integer(JsonElement item, string name, int min, int max, int? fallback = null)
    {
        if (!item.TryGetProperty(name, out var value)) return fallback ?? throw new InvalidDataException("插件页面缺少 " + name + "。");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max) throw new InvalidDataException(name + $" 必须是 {min}–{max} 的整数。");
        return number;
    }
    private static bool Boolean(JsonElement item, string name, bool fallback = false)
    {
        if (!item.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException(name + " 必须是布尔值。");
        return value.GetBoolean();
    }
    private static string Color(JsonElement item, string name, string fallback)
    {
        var value = item.TryGetProperty(name, out _) ? Text(item, name, 7) : fallback;
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")) throw new InvalidDataException(name + " 必须是 #RRGGBB 颜色。");
        return value;
    }
}
