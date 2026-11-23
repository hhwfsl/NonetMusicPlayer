using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Plugins;

internal static class AgentPolicyChecks
{
    public static void Run()
    {
        var command = AgentCommandPolicy.ValidateAndEncode(new("a", "playlist.create", ["-y"]));
        var parsed = CommandLineParser.Parse(command);
        Check(!parsed.Confirmed && parsed.Arguments.Single() == "-y", "Model cannot supply confirmation flags");
        Check(CommandLineParser.Parse(AgentCommandPolicy.ValidateAndEncode(new("b", "playlist.rename", ["x", "中文 \"音楽\""]))).Arguments[1] == "中文 \"音楽\"", "Quoted Unicode arguments survive");
        foreach (var action in new[] { "plugins.install", "plugins.config", "data.restore", "lyrics.import", "layout.apply", "powershell.run", "app.exit" })
            Reject(() => AgentCommandPolicy.ValidateAndEncode(new("x", action, [])));
        Reject(() => AgentCommandPolicy.ValidateAndEncode(new("x", "settings.set", ["dataDirectory", "\"D:/Music\""])));
        Reject(() => AgentCommandPolicy.ValidateAndEncode(new("x", "settings.set", ["accent", "\"invalid\""])));
        Reject(() => AgentCommandPolicy.ValidateAndEncode(new("x", "navigate", ["plugin:attacker"])));
        var modern = UniversalPluginCommandPolicy.CatalogFor(ExtensionContract.Permissions)["commands"]!.AsArray();
        var supported = modern.Select(item => item!["operation"]!.GetValue<string>()).ToHashSet();
        Check(new[] { "queue.move", "music.share", "music.query", "playlist.follow", "playlist.sync", "statistics.hours", "network.play", "updates.check", "updates.install" }.All(supported.Contains), "Current command families are advertised to the agent");
        Check(new[] { "lyrics.open", "music.copy-link", "playlist.edit", "plugins.check-updates", "plugins.update" }.All(supported.Contains), "Desktop operations remain available through the direct plugin command catalog");
        Reject(() => UniversalPluginCommandPolicy.ValidateFor(new("x", "plugins.configure", ["another"]), ["plugins-control"]));
        Reject(() => UniversalPluginCommandPolicy.ValidateFor(new("x", "lyrics.open", []), ["music-read"]));
        Check(UniversalPluginCommandPolicy.RequiresConfirmation("playlist.follow") && UniversalPluginCommandPolicy.RequiresConfirmation("updates.install") && !UniversalPluginCommandPolicy.RequiresConfirmation("plugins.info"), "New operations retain host confirmation rules");
        Reject(() => UniversalPluginCommandPolicy.ValidateFor(new("x", "network.play", ["other"]), ["player-control"]));
        Reject(() => UniversalPluginCommandPolicy.ValidateFor(new("x", "network.invitation", []), ExtensionContract.Permissions));
        Reject(() => UniversalPluginCommandPolicy.ValidateFor(new("x", "plugins.config", ["another"]), ExtensionContract.Permissions));
        var reordered = CommandLineParser.Parse(UniversalPluginCommandPolicy.ValidateFor(new("x", "queue.move", ["track", "1"]), ["player-control"]));
        Check(reordered.Name == "queue.move" && !reordered.Confirmed && reordered.Arguments.SequenceEqual(["track", "1"]), "New command uses the same argument codec");
        Check(AgentCommandPolicy.RequiresConfirmation("plugins.enable") && AgentCommandPolicy.RequiresConfirmation("settings.set")
            && AgentCommandPolicy.RequiresConfirmation("playlist.delete") && !AgentCommandPolicy.RequiresConfirmation("player.pause"), "Host owns confirmation policy");
        var clean = AgentCommandPolicy.Sanitize(JsonNode.Parse("""{"filePath":"D:/private/song.flac","apiKey":"TEST-NOT-SECRET","nested":{"title":"音楽","lyricsPath":"x"}}"""))!;
        Check(clean["filePath"] is null && clean["apiKey"] is null && clean["nested"]!["lyricsPath"] is null && clean["nested"]!["title"]!.GetValue<string>() == "音楽", "Metadata sanitizes paths and secrets");
        Check(typeof(AgentReply).GetConstructor([typeof(string), typeof(AgentToolCall[])]) is not null
            && typeof(AgentMessage).GetConstructor([typeof(string), typeof(string), typeof(AgentToolCall[]), typeof(string)]) is not null, "SDK 3.4 constructor signatures remain");
        var legacyReply = JsonSerializer.Deserialize("""{"text":"legacy","calls":[]}""", AgentPluginJson.Default.AgentReply)!;
        Check(legacyReply.Text == "legacy" && legacyReply.Reasoning is null, "Old Agent reply remains valid");
        var turn = new AgentTurn([new("user", "你好")], AgentCommandPolicy.Catalog());
        AgentPluginContract.Validate(turn);
        Check(JsonSerializer.Deserialize(JsonSerializer.Serialize(turn, AgentPluginJson.Default.AgentTurn), AgentPluginJson.Default.AgentTurn)!.Messages[0].Text == "你好", "Source-generated RPC roundtrip");
        Reject(() => AgentPluginContract.Validate(new AgentReply("", [new("same", "status", []), new("same", "status", [])])));
        var manifest = new PluginManifest { Id = "fixture.agent", Name = "Generic Agent", Type = "agent", PageEntry = "page.json",
            Permissions = ["network","process","agent-control"], EntryPoints = new() { ["win-x64"] = "worker.exe" },
            LifecycleMethods = ["lifecycle.shutdown"] };
        manifest.Validate();
        using var page = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"title":"Agent","widgets":[{"type":"agent-chat"}]}"""));
        Check(PluginPageContract.Read(page, manifest.Permissions).Widgets.Single().Type == "agent-chat", "Optional agent page contract");
        manifest.Permissions.Remove("agent-control"); Reject(manifest.Validate);
        Console.WriteLine("PASS Agent allowlist, argument quoting, no model confirmation bypass, safe settings, privacy, bounded source-generated Contract");
    }
    private static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or FormatException) { return; } throw new InvalidOperationException("Unsafe action accepted"); }
}
