using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Diagnostics;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>共享命令目录、危险操作确认与结果流；不执行任意程序或系统命令。</summary>
public sealed class PlayerCommandRouter(IPlayerCommandBackend backend)
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly AsyncLocal<CommandScope?> _scope = new();
    private sealed class CommandScope { public bool Active = true; public int Depth; }
    private readonly Queue<CommandResult> _journal = new();
    private readonly object _journalGate = new();
    /// <summary>可选宿主流程闸门，在参数与确认校验之后执行；不能替换或预批准命令。</summary>
    public Func<PlayerCommand, CancellationToken, Task<bool>>? BeforeExecute { get; set; }
    /// <summary>桌面终端将未知命令直接作为输出；默认保留既有 CLI 的日志与结果行为。</summary>
    public bool UnknownCommandsAsOutput { get; set; }
    public event EventHandler<CommandResult>? ResultPublished;
    public IReadOnlyList<CommandResult> Journal { get { lock (_journalGate) return _journal.ToArray(); } }
    public static IReadOnlyList<CommandDefinition> Definitions { get; } =
    [
        new("help", "help [category] [command]"), new("clear", "clear"), new("terminal.exit", "terminal.exit"), new("status", "status"), new("app.exit", "app.exit"),
        new("player.play", "player.play [track-id|file]"), new("player.pause", "player.pause"), new("player.resume", "player.resume"), new("player.next", "player.next"), new("player.previous", "player.previous"),
        new("player.seek", "player.seek <seconds>"), new("player.volume", "player.volume <0-100>"), new("player.mute", "player.mute"), new("player.mode", "player.mode <repeat-all|repeat-one|shuffle>"),
        new("player.devices", "player.devices"), new("player.device", "player.device <name>"), new("queue.list", "queue.list"), new("queue.play", "queue.play <playlist-id> [track-id]"),
        new("player.stop", "player.stop"), new("queue.select", "queue.select <track-id>"), new("queue.move", "queue.move <track-id> <1-based-position>"),
        new("playlist.tracks", "playlist.tracks <playlist-id>"), new("playlist.from-folders", "playlist.from-folders <folder...>"),
        new("music.sources", "music.sources"), new("music.query", "music.query <source-id|local> <query>"), new("music.results", "music.results"), new("music.more", "music.more"),
        new("album.list", "album.list"), new("album.tracks", "album.tracks <name>"), new("artist.list", "artist.list"), new("artist.tracks", "artist.tracks <name>"),
        new("lyrics.reload", "lyrics.reload"), new("lyrics.clear", "lyrics.clear", true), new("lyrics.export", "lyrics.export <file>"),
        new("statistics.hours", "statistics.hours [hours]"),
        new("plugins.info", "plugins.info <id>"), new("plugins.reload", "plugins.reload <id>"), new("plugins.state", "plugins.state <id>"), new("plugins.invoke", "plugins.invoke <id> <action> [json]", true),
        new("plugins.trust", "plugins.trust <id>", true),
        new("music.list", "music.list [query]"), new("music.info", "music.info <track-id>"), new("music.remove", "music.remove <track-id...>", true),
        new("playlist.list", "playlist.list"), new("playlist.create", "playlist.create <name>"), new("playlist.rename", "playlist.rename <id> <name>"), new("playlist.describe", "playlist.describe <id> <description>"),
        new("playlist.delete", "playlist.delete <id>", true), new("playlist.add", "playlist.add <id> <file|folder|track-id...>"), new("playlist.remove", "playlist.remove <id> <track-id...>", true),
        new("playlist.move", "playlist.move <id> <track-id> <1-based-position>"), new("playlist.reorder", "playlist.reorder <id> <1-based-position>"), new("playlist.cover", "playlist.cover <id> <file|default>"),
        new("favorite.add", "favorite.add <track-id>"), new("favorite.remove", "favorite.remove <track-id>"),
        new("history.list", "history.list"), new("history.remove", "history.remove <track-id...>"), new("history.clear", "history.clear", true), new("statistics", "statistics [yyyy-MM-dd] [day|month|year]"),
        new("statistics.history", "statistics.history [session|interval|event|hour] [limit] [offset] [session-id]"),
        new("settings.list", "settings.list"), new("settings.get", "settings.get <name>"), new("settings.set", "settings.set <name> <json-value>"), new("settings.reset", "settings.reset", true),
        new("data.backup", "data.backup <file>"), new("data.restore", "data.restore <file>", true),
        new("lyrics.show", "lyrics.show"), new("lyrics.import", "lyrics.import <file>"), new("lyrics.path", "lyrics.path"),
        new("plugins.list", "plugins.list"), new("plugins.install", "plugins.install <impp-file|github-release-url>", true),
        new("plugins.enable", "plugins.enable <id>", true), new("plugins.disable", "plugins.disable <id>"), new("plugins.uninstall", "plugins.uninstall <id> [keep-files|delete-files]", true),
        new("plugins.config", "plugins.config <id> [json]"), new("plugins.schema", "plugins.schema <id>"), new("plugins.refresh", "plugins.refresh <id>"),
        new("layout.reset", "layout.reset", true, true),
        new("window.show", "window.show", DesktopOnly:true), new("window.hide", "window.hide", DesktopOnly:true), new("window.minimize", "window.minimize", DesktopOnly:true), new("window.maximize", "window.maximize", DesktopOnly:true), new("window.restore", "window.restore", DesktopOnly:true), new("window.close", "window.close", DesktopOnly:true),
        new("desktop-lyrics.toggle", "desktop-lyrics.toggle", DesktopOnly:true), new("desktop-lyrics.lock", "desktop-lyrics.lock <true|false>", DesktopOnly:true),
        new("album.cover", "album.cover <name> <file|auto|default>"), new("artist.cover", "artist.cover <name> <file|auto|default>"),
        new("navigate", "navigate <page>", DesktopOnly:true), new("undo", "undo"),
        new("music.search", "music.search <query>"), new("import.cancel", "import.cancel", DesktopOnly:true),
        new("playlist.import", "playlist.import <m3u-file> [playlist-id]"), new("playlist.export", "playlist.export <m3u-file> [playlist-id]"),
        new("queue.show", "queue.show", DesktopOnly:true), new("queue.hide", "queue.hide", DesktopOnly:true), new("queue.toggle", "queue.toggle", DesktopOnly:true),
        new("player.locate", "player.locate", DesktopOnly:true), new("window.default-size", "window.default-size", DesktopOnly:true), new("layout.edit", "layout.edit <true|false>", DesktopOnly:true),
        new("layout.export", "layout.export <json-file>", DesktopOnly:true), new("layout.apply", "layout.apply <json-file>", true, true),
        new("updates.check", "updates.check", DesktopOnly:true), new("updates.install", "updates.install", true, true),
        new("data.directory", "data.directory <folder>", true, true), new("help.open", "help.open", DesktopOnly:true),
        new("selection.begin", "selection.begin", DesktopOnly:true), new("selection.end", "selection.end", DesktopOnly:true),
        new("selection.all", "selection.all", DesktopOnly:true), new("selection.clear", "selection.clear", DesktopOnly:true),
        new("selection.select", "selection.select <track-id...>", DesktopOnly:true), new("selection.list", "selection.list", DesktopOnly:true),
        new("network.status", "network.status"), new("network.sharing", "network.sharing <on|off>"),
        new("network.connect", "network.connect <invitation>"), new("network.peers", "network.peers"),
        new("network.requests", "network.requests"), new("network.accept", "network.accept <request-id>"),
        new("network.reject", "network.reject <request-id>"), new("network.disconnect", "network.disconnect <peer-id|all>"),
        new("network.renew", "network.renew"), new("network.invitation", "network.invitation"),
        new("network.playlists", "network.playlists <peer-id>"), new("network.tracks", "network.tracks <peer-id>"), new("network.play", "network.play <peer-id> [playlist-id] [track-id]")
    ];

    public async Task<CommandResult> ExecuteAsync(string input, Func<CommandDefinition, Task<bool>>? confirm = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var previous = _scope.Value; var scope = new CommandScope(); _scope.Value = scope;
        try { return await ExecuteCoreAsync(input, confirm, cancellationToken); }
        finally { scope.Active = false; _scope.Value = previous; _gate.Release(); }
    }
    /// <summary>仅供宿主处理已校验的扩展请求；复用确认/参数/钩子检查，避免动作回调等待自己的路由锁。</summary>
    public async Task<CommandResult> ExecuteFromExtensionAsync(string input, Func<CommandDefinition, Task<bool>>? confirm = null, CancellationToken cancellationToken = default)
    {
        if (_scope.Value is not { Active: true } scope) return await ExecuteAsync(input, confirm, cancellationToken);
        if (scope.Depth >= 8) return CommandResults.Failed("plugins.invoke", "Extension command nesting limit reached.");
        scope.Depth++;
        try { return await ExecuteCoreAsync(input, confirm, cancellationToken); }
        finally { scope.Depth--; }
    }
    private async Task<CommandResult> ExecuteCoreAsync(string input, Func<CommandDefinition, Task<bool>>? confirm, CancellationToken cancellationToken)
    {
        CommandResult result;
        try
        {
            var command = CommandLineParser.Parse(input);
            var definition = Definitions.FirstOrDefault(d => d.Name == command.Name && (!d.DesktopOnly || backend.HasWindow)) ?? throw new UnknownCommandException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownCommand"));
            ValidateArguments(command, definition);
            if (definition.DesktopOnly && !backend.HasWindow) result = new(command.Name, false, NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.WindowRequired"));
            else if (definition.Destructive && !command.Confirmed && (command.Declined || confirm is null || !await confirm(definition))) result = new(command.Name, false, NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get(command.Declined ? "Commands.Cancelled" : "Commands.ConfirmationRequired"), RequiresConfirmation:!command.Declined);
            else if (command.Name == "help")
            {
                var category = command.Arguments.FirstOrDefault();
                var matches = Definitions.Where(d => !d.DesktopOnly || backend.HasWindow)
                    .Where(d => category is null || d.PublicUsage.StartsWith("nonet " + category + " ", StringComparison.OrdinalIgnoreCase))
                    .Where(d => command.Arguments.Count < 2 || d.PublicUsage.StartsWith("nonet " + category + " " + command.Arguments[1], StringComparison.OrdinalIgnoreCase));
                result = new("help", true, string.Join(Environment.NewLine, matches.Select(d => d.PublicUsage)));
            }
            else if (command.Name == "clear")
            {
                // 清屏只影响结果历史，不触碰磁盘日志、命令历史或播放器业务数据。
                AppLog.Flush();
                lock (_journalGate) _journal.Clear();
                result = CommandResults.Completed("clear");
            }
            else if (BeforeExecute is not null && !await BeforeExecute(command, cancellationToken)) result = new(command.Name, false, NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.Cancelled"));
            else result = await backend.ExecuteAsync(command with { Confirmed = true }, cancellationToken);
        }
        catch (UnknownCommandException) when (UnknownCommandsAsOutput) { result = new("command.unknown", false, NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownCommandOutput")); }
        catch (OperationCanceledException) { result = new("command", false, NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.Cancelled")); }
        catch (Exception error) { AppLog.Warning("Commands", "Player command failed", error); result = new("command", false, AppLog.Redact(error.Message)); }
        Publish(result); return result;
    }

    /// <summary>宿主的鼠标/快捷键动作也调用此方法，使用与命令相同的结构化结果。</summary>
    public void Publish(CommandResult result)
    {
        // 事务日志与终端走同一结果流，只记录脱敏后的摘要，不记录配置参数和结构化载荷。
        if (result.Operation is not ("help" or "clear") && !(UnknownCommandsAsOutput && result.Operation == "command.unknown"))
        {
            if (result.Success) AppLog.Info("Commands", AppLog.Redact(result.Message));
            else AppLog.Warning("Commands", AppLog.Redact(result.Message));
        }
        // 返回数据可含路径，日志中的消息必须脱敏；配置结果由宿主先剔除敏感值。
        // 大型列表只保留摘要，不让重复查询把整个音乐库复制进历史数百次。
        var archived = result;
        if (result.Data is JsonArray array && array.Count > 100)
            archived = result with { Data = new JsonObject { ["count"] = array.Count, ["items"] = new JsonArray(array.Take(100).Select(item => item?.DeepClone()).ToArray()) } };
        else if (result.Data?.ToJsonString().Length > 131072)
            archived = result with { Data = JsonValue.Create(Localization.LocalizationCatalog.Get("Terminal.StructuredOutput")) };
        lock (_journalGate) { _journal.Enqueue(archived); while (_journal.Count > 500) _journal.Dequeue(); }
        if (ResultPublished is null) return;
        foreach (EventHandler<CommandResult> subscriber in ResultPublished.GetInvocationList())
            try { subscriber(this, result); } catch (Exception error) { AppLog.Warning("Commands", "Result subscriber failed", error); }
    }
    private static void ValidateArguments(PlayerCommand command, CommandDefinition definition)
    {
        var required = System.Text.RegularExpressions.Regex.Matches(definition.Usage, "<[^>]+>").Count;
        var optional = System.Text.RegularExpressions.Regex.Matches(definition.Usage, "\\[[^]]+\\]").Count;
        var maximum = definition.Usage.Contains("...", StringComparison.Ordinal) ? int.MaxValue : required + optional;
        if (command.Arguments.Count < required || command.Arguments.Count > maximum) throw new FormatException(Localization.LocalizationCatalog.Format("Commands.Usage", definition.PublicUsage));
    }
}
