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
        Check(AgentCommandPolicy.RequiresConfirmation("plugins.enable") && AgentCommandPolicy.RequiresConfirmation("settings.set")
            && AgentCommandPolicy.RequiresConfirmation("playlist.delete") && !AgentCommandPolicy.RequiresConfirmation("player.pause"), "Host owns confirmation policy");
        var clean = AgentCommandPolicy.Sanitize(JsonNode.Parse("""{"filePath":"D:/private/song.flac","apiKey":"TEST-NOT-SECRET","nested":{"title":"音楽","lyricsPath":"x"}}"""))!;
        Check(clean["filePath"] is null && clean["apiKey"] is null && clean["nested"]!["lyricsPath"] is null && clean["nested"]!["title"]!.GetValue<string>() == "音楽", "Metadata sanitizes paths and secrets");
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
