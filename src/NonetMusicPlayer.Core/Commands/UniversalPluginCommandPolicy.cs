using NonetMusicPlayer.Core.Plugins;
using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>桌面通用插件的新增命令策略独立于旧 Agent 策略，绝不引入任意路径或秘密读取。</summary>
public static class UniversalPluginCommandPolicy
{
    private static readonly string[] Additional = ["window.hide", "undo", "import.cancel", "selection.begin", "selection.end", "selection.all", "selection.clear", "selection.select", "selection.list", "help.open", "plugins.uninstall", "plugins.refresh"];
    private static readonly string[] Settings = ["fontFamily", "backgroundImageStretch", "myMusicExpanded", "pluginsExpanded", "playlistsExpanded", "desktopLyricsVisible", "desktopLyricsLocked", "desktopLyricsWidth", "desktopLyricsHeight", "optimizeMemoryWhenMinimized", "sidebarWidth", "playerHeight", "navigationRight", "playerTop", "titleButtonsOnLeft"];
    public static JsonObject CatalogFor(IReadOnlyCollection<string> permissions)
    {
        var catalog = JsonNode.Parse(PluginCommandPolicy.CatalogFor(permissions))!.AsObject();
        foreach (var name in Additional)
        {
            var permission = name == "help.open" ? "navigation" : name.StartsWith("selection.", StringComparison.Ordinal) || name is "undo" or "import.cancel" ? "library-write" : PluginCommandPolicy.RequiredPermission(name);
            if (permissions.Contains(permission))
            {
                var definition = PlayerCommandRouter.Definitions.Single(d => d.Name == name);
                catalog["commands"]!.AsArray().Add(new JsonObject { ["operation"] = name, ["usage"] = definition.Usage, ["confirmation"] = PluginCommandPolicy.RequiresConfirmation(name) });
            }
        }
        if (permissions.Contains("settings-write"))
            foreach (var setting in Settings) catalog["settings"]!.AsArray().Add(JsonValue.Create(setting));
        return catalog;
    }
    public static string ValidateFor(AgentToolCall call, IReadOnlyCollection<string> permissions)
    {
        var specialSetting = call.Operation is "settings.get" or "settings.set" && call.Arguments.Length > 0 && Settings.Contains(call.Arguments[0], StringComparer.OrdinalIgnoreCase);
        var specialNavigation = call.Operation == "navigate" && call.Arguments.Length == 1 && (call.Arguments[0] == "terminal" || new[] { "plugin:", "album:", "artist:" }.Any(p => call.Arguments[0].StartsWith(p, StringComparison.Ordinal)));
        if (!Additional.Contains(call.Operation) && !specialSetting && !specialNavigation) return PluginCommandPolicy.ValidateFor(call, permissions);
        AgentPluginContract.Validate(new AgentReply("", [call]));
        var permission = call.Operation == "help.open" ? "navigation" : call.Operation.StartsWith("selection.", StringComparison.Ordinal) || call.Operation is "undo" or "import.cancel" ? "library-write" : PluginCommandPolicy.RequiredPermission(call.Operation);
        if (!permissions.Contains(permission)) throw new InvalidDataException("Plugin permission denied.");
        var definition = PlayerCommandRouter.Definitions.Single(d => d.Name == call.Operation);
        var required = System.Text.RegularExpressions.Regex.Matches(definition.Usage, "<[^>]+>").Count;
        var optional = System.Text.RegularExpressions.Regex.Matches(definition.Usage, "\\[[^]]+\\]").Count;
        if (call.Arguments.Length < required || !definition.Usage.Contains("...", StringComparison.Ordinal) && call.Arguments.Length > required + optional) throw new InvalidDataException("Wrong argument count.");
        if (specialSetting && call.Operation == "settings.set") SettingsContract.Validate(call.Arguments[0], CommandResults.ParseSettingValue(call.Arguments[1]));
        var encoded = CommandSyntax.PublicName(call.Operation) + " -- " + string.Join(" ", call.Arguments.Select(a => "\"" + a.Replace("\"", "\\\"") + "\""));
        var parsed = CommandLineParser.Parse(encoded);
        if (parsed.Confirmed || parsed.Declined || parsed.Name != call.Operation || !parsed.Arguments.SequenceEqual(call.Arguments)) throw new InvalidDataException("Invalid argument encoding.");
        return encoded;
    }
}
