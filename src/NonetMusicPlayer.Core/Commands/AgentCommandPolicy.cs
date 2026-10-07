using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>Agent 的不可绕过白名单；不暴露文件操作、配置凭据、外部程序或插件安装。</summary>
public static class PluginCommandPolicy
{
    private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
    {
        "status", "player.play", "player.pause", "player.resume", "player.next", "player.previous", "player.seek",
        "player.volume", "player.mute", "player.mode", "player.devices", "player.device", "queue.list", "queue.play",
        "music.list", "music.info", "music.remove", "music.search", "playlist.list", "playlist.create", "playlist.rename",
        "playlist.describe", "playlist.delete", "playlist.add", "playlist.remove", "playlist.move", "playlist.reorder",
        "favorite.add", "favorite.remove", "history.list", "history.remove", "history.clear", "statistics",
        "settings.list", "settings.get", "settings.set", "settings.reset", "plugins.list",
        "plugins.enable", "plugins.disable", "navigate", "window.show", "window.minimize", "window.maximize",
        "window.restore", "window.close", "desktop-lyrics.toggle", "desktop-lyrics.lock"
    };
    private static readonly HashSet<string> SafeSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "volume", "historyLimit", "fontSize", "theme", "accent", "language", "playMode", "lyricOffset",
        "terminalScrollbackLines", "terminalFontSize", "terminalMinimumLogLevel", "terminalOpacity",
        "backgroundImageOpacity", "titleBarOpacity", "navigationOpacity", "contentOpacity", "playerOpacity",
        "uiOpacity", "controlCornerRadius", "desktopLyricsFontSize", "closeToTray"
    };
    public static string RequiredPermission(string operation) => operation.StartsWith("player.") || operation.StartsWith("queue.") || operation.StartsWith("desktop-lyrics.") ? "player-control" : operation == "navigate" ? "navigation" : operation.StartsWith("window.") ? "window-control" : operation.StartsWith("plugins.") ? "plugins-control" : operation.StartsWith("settings.") ? "settings-write" : RequiresConfirmation(operation) || operation.StartsWith("playlist.") && operation != "playlist.list" || operation.StartsWith("favorite.") ? "library-write" : "music-read";
    public static string CatalogFor(IReadOnlyCollection<string> permissions)
    {
        var catalog = JsonNode.Parse(Catalog())!.AsObject(); var commands = catalog["commands"]!.AsArray();
        for (var i = commands.Count - 1; i >= 0; i--) if (!permissions.Contains(RequiredPermission(commands[i]!["operation"]!.GetValue<string>()))) commands.RemoveAt(i);
        return catalog.ToJsonString();
    }
    public static string ValidateFor(AgentToolCall call, IReadOnlyCollection<string> permissions)
    {
        if (!permissions.Contains(RequiredPermission(call.Operation))) throw new InvalidDataException("Plugin permission denied.");
        return ValidateAndEncode(call);
    }
    public static string Catalog()
    {
        var definitions = new JsonArray();
        foreach (var item in PlayerCommandRouter.Definitions.Where(d => Operations.Contains(d.Name)))
            definitions.Add(new JsonObject { ["operation"] = item.Name, ["usage"] = item.Usage,
                ["confirmation"] = RequiresConfirmation(item.Name) });
        return new JsonObject { ["commands"] = definitions, ["settings"] = new JsonArray(SafeSettings.Order().Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            ["notes"] = "Only existing track/playlist IDs. Never file paths. Navigate only to built-in pages or playlist:<id>. settings.set values are JSON literals. Query lists before using IDs." }.ToJsonString();
    }
    public static bool RequiresConfirmation(string operation)
        => operation.StartsWith("settings.", StringComparison.Ordinal) && operation is not ("settings.list" or "settings.get")
            || operation.StartsWith("plugins.", StringComparison.Ordinal) && operation != "plugins.list"
            || operation is "music.remove" or "playlist.delete" or "playlist.remove" or "history.remove" or "history.clear" or "window.close";
    public static string ValidateAndEncode(AgentToolCall call)
    {
        AgentPluginContract.Validate(new AgentReply("", [call]));
        if (!Operations.Contains(call.Operation)) throw new InvalidDataException("Agent operation is not allowed.");
        var definition = PlayerCommandRouter.Definitions.Single(d => d.Name == call.Operation);
        var required = System.Text.RegularExpressions.Regex.Matches(definition.Usage, "<[^>]+>").Count;
        var optional = System.Text.RegularExpressions.Regex.Matches(definition.Usage, @"\[[^]]+\]").Count;
        if (call.Arguments.Length < required || !definition.Usage.Contains("...", StringComparison.Ordinal) && call.Arguments.Length > required + optional)
            throw new InvalidDataException("Wrong tool argument count.");
        if (call.Operation is "settings.get" or "settings.set")
        {
            if (!SafeSettings.Contains(call.Arguments[0])) throw new InvalidDataException("This setting is not available to agents.");
            if (call.Operation == "settings.set") SettingsContract.Validate(call.Arguments[0], CommandResults.ParseSettingValue(call.Arguments[1]));
        }
        if (call.Operation == "navigate" && call.Arguments[0] is not ("library" or "songs" or "albums" or "artists" or "statistics" or "settings" or "lyrics" or "history" or "plugins")
            && !call.Arguments[0].StartsWith("playlist:", StringComparison.Ordinal)) throw new InvalidDataException("Navigation target is not allowed.");
        // -- 结束选项解析，模型参数中的 -y/--yes 永远只是参数，不能伪造用户确认。
        var command = CommandSyntax.PublicName(call.Operation) + " -- " + string.Join(" ", call.Arguments.Select(a => "\"" + a.Replace("\"", "\\\"") + "\""));
        var parsed = CommandLineParser.Parse(command);
        if (parsed.Confirmed || parsed.Declined || parsed.Name != call.Operation || !parsed.Arguments.SequenceEqual(call.Arguments))
            throw new InvalidDataException("Tool argument encoding failed.");
        return command;
    }
    /// <summary>向模型提供有限元数据；路径、配置文件和敏感字段从结果中去除。</summary>
    public static JsonNode? Sanitize(JsonNode? value, int depth = 0)
    {
        if (depth > 8) return null;
        if (value is JsonObject obj)
        {
            var clean = new JsonObject();
            foreach (var pair in obj)
                if (!System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "path|folder|directory|token|secret|password|api.?key|authorization|configuration", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    clean[pair.Key] = Sanitize(pair.Value, depth + 1);
            return clean;
        }
        if (value is JsonArray array) return new JsonArray(array.Take(100).Select(v => Sanitize(v, depth + 1)).ToArray());
        if (value is JsonValue item && item.TryGetValue<string>(out var text)) return JsonValue.Create(text.Length > 1000 ? text[..1000] : text);
        return value?.DeepClone();
    }
}

    
/// <summary>保留旧 Agent API 的入口，由公共权限策略统一验证。</summary>
public static class AgentCommandPolicy
{
    public static string Catalog() => PluginCommandPolicy.Catalog();
    public static bool RequiresConfirmation(string operation) => PluginCommandPolicy.RequiresConfirmation(operation);
    public static string ValidateAndEncode(AgentToolCall call) => PluginCommandPolicy.ValidateAndEncode(call);
    public static JsonNode? Sanitize(JsonNode? value, int depth = 0) => PluginCommandPolicy.Sanitize(value, depth);
}
