using NonetMusicPlayer.Core.Plugins;
using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>桌面通用插件的新增命令策略独立于旧 Agent 策略，绝不引入任意路径或秘密读取。</summary>
public static class UniversalPluginCommandPolicy
{
    private static readonly string[] Additional = ["window.hide", "undo", "import.cancel", "selection.begin", "selection.end", "selection.all", "selection.clear", "selection.select", "selection.list", "help.open", "plugins.uninstall", "plugins.refresh",
        "player.stop", "player.locate", "queue.select", "queue.move", "queue.show", "queue.hide", "queue.toggle", "window.default-size", "layout.edit", "layout.reset",
        "music.sources", "music.query", "music.results", "music.more", "music.share", "playlist.tracks", "playlist.follow", "playlist.sync", "playlist.unfollow",
        "album.list", "album.tracks", "artist.list", "artist.tracks", "lyrics.show", "lyrics.reload", "lyrics.clear", "statistics.hours", "plugins.info",
        "network.status", "network.peers", "network.playlists", "network.tracks", "network.play", "updates.check", "updates.install",
        "lyrics.open", "lyrics.close", "lyrics.toggle", "music.details", "music.copy-link", "playlist.edit", "plugins.configure", "plugins.check-updates", "plugins.update", "data.info"];
    private static readonly string[] Settings = ["fontFamily", "backgroundImageStretch", "accentFromBackground", "myMusicExpanded", "pluginsExpanded", "playlistsExpanded", "desktopLyricsVisible", "desktopLyricsLocked", "desktopLyricsWidth", "desktopLyricsHeight", "optimizeMemoryWhenMinimized", "sidebarWidth", "playerHeight", "navigationRight", "playerTop", "titleButtonsOnLeft", "lanApprovalWaitSeconds"];
    public static JsonObject CatalogFor(IReadOnlyCollection<string> permissions)
    {
        var catalog = JsonNode.Parse(PluginCommandPolicy.CatalogFor(permissions))!.AsObject();
        foreach (var name in Additional)
        {
            if (HasPermission(name, permissions))
            {
                var definition = PlayerCommandRouter.Definitions.Single(d => d.Name == name);
                catalog["commands"]!.AsArray().Add(new JsonObject { ["operation"] = name, ["usage"] = definition.Usage, ["confirmation"] = RequiresConfirmation(name) });
            }
        }
        if (permissions.Contains("settings-write"))
            foreach (var setting in Settings) catalog["settings"]!.AsArray().Add(JsonValue.Create(setting));
        catalog["notes"] = "Use existing IDs from host queries, never file paths. Navigate to built-in pages or discovered playlist:, plugin:, album:, artist:, lan: and interface: destinations. settings.set values are JSON literals.";
        return catalog;
    }
    public static string ValidateFor(AgentToolCall call, IReadOnlyCollection<string> permissions)
    {
        var specialSetting = call.Operation is "settings.get" or "settings.set" && call.Arguments.Length > 0 && Settings.Contains(call.Arguments[0], StringComparer.OrdinalIgnoreCase);
        var specialNavigation = call.Operation == "navigate" && call.Arguments.Length == 1 && (call.Arguments[0] is "terminal" or "devices" || new[] { "plugin:", "album:", "artist:", "lan:", "interface:" }.Any(p => call.Arguments[0].StartsWith(p, StringComparison.Ordinal)));
        if (!Additional.Contains(call.Operation) && !specialSetting && !specialNavigation) return PluginCommandPolicy.ValidateFor(call, permissions);
        AgentPluginContract.Validate(new AgentReply("", [call]));
        if (!HasPermission(call.Operation, permissions)) throw new InvalidDataException("Plugin permission denied.");
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
    // 新命令使用现有权限域；联网播放同时要求网络和播放权限。
    private static bool HasPermission(string operation, IReadOnlyCollection<string> permissions) => operation switch
    {
        "help.open" or "player.locate" => permissions.Contains("navigation") && (operation != "player.locate" || permissions.Contains("player-control")),
        "lyrics.open" or "lyrics.close" or "lyrics.toggle" => permissions.Contains("navigation"),
        "undo" or "import.cancel" or "playlist.edit" => permissions.Contains("library-write"),
        "lyrics.reload" or "lyrics.clear" => permissions.Contains("lyrics-write"),
        "playlist.tracks" => permissions.Contains("music-read"),
        "layout.edit" or "layout.reset" => permissions.Contains("ui-extend"),
        "updates.check" or "updates.install" or "data.info" => permissions.Contains("settings-write"),
        "plugins.configure" => permissions.Contains("plugins-control") && permissions.Contains("settings-write"),
        "network.play" => permissions.Contains("network") && permissions.Contains("player-control"),
        _ when operation.StartsWith("selection.", StringComparison.Ordinal) => permissions.Contains("library-write"),
        _ when operation.StartsWith("network.", StringComparison.Ordinal) => permissions.Contains("music-read"),
        _ => permissions.Contains(PluginCommandPolicy.RequiredPermission(operation))
    };
    public static bool RequiresConfirmation(string operation) => operation is not ("plugins.info" or "plugins.check-updates") && (operation == "playlist.sync"
        || PluginCommandPolicy.RequiresConfirmation(operation)
        || PlayerCommandRouter.Definitions.Any(definition => definition.Name == operation && definition.Destructive));
}
