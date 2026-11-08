using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Core.Runtime;
using NonetMusicPlayerCli;

/// <summary>在独立目录验证真实 CLI 业务和通用进程音源，不依赖任何私人插件。</summary>
internal static class DesktopParityChecks
{
    public static async Task RunAsync(string root)
    {
        var output = Path.Combine(root, "parity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var media = Path.Combine(output, "音乐"); Directory.CreateDirectory(media);
        for (var i = 0; i < 12; i++) Wave(Path.Combine(media, "Track " + i + ".wav"));
        var data = Path.Combine(output, "Data"); var audio = new Audio(); string listId, current; string[] moved;
        await using (var session = new HeadlessPlayerSession(data, audio))
        {
            var router = new PlayerCommandRouter(session) { UnknownCommandsAsOutput = true };
            async Task<JsonNode?> Run(string command) { var result = await router.ExecuteAsync(command); Require(result.Success, command + ": " + result.Message); return result.Data; }
            var folders = (await Run("playlist from-folders " + Quote(media)))!.AsArray(); listId = folders[0]!["id"]!.GetValue<string>();
            Require(session.State.Tracks.Count == 12 && session.State.Playlists.Single(p => p.Id == listId).TrackIds.Count == 12, "Folder playlist and deduplicated import");
            var source = session.State.Playlists.Single(p => p.Id == listId).TrackIds.ToArray();
            current = source[3]; await Run("player mode shuffle"); await Run("queue play " + listId + " " + current);
            async Task<string[]> Queue() => (await Run("queue list"))!.AsArray().Select(t => t!["id"]!.GetValue<string>()).ToArray();
            var shuffled = await Queue(); Require(shuffled[0] == current && shuffled.Distinct().Count() == 12 && shuffled.ToHashSet().SetEquals(source), "Shuffle builds a whole queue once");
            for (var i = 1; i < 12; i++) { await Run("player next"); Require(session.Current!.Id == shuffled[i] && (await Queue()).SequenceEqual(shuffled), "Next follows visible queue without rerandomization"); }
            await Run("player next"); Require(session.Current!.Id == shuffled[0], "Shuffle wraps without changing order");
            await Run("player previous"); Require(session.Current!.Id == shuffled[^1], "Previous reverses the same queue");
            await Run("queue select " + current); Require((await Queue())[0] == current, "Manual queue selection starts a new permutation at the selected track");
            await Run("settings set playMode 1"); Require((await Queue()).SequenceEqual(source), "Setting mode changes also restore playlist order");
            await Run("player seek 42"); var generation = audio.Loads;
            await Run("queue move " + source[0] + " 6"); moved = await Queue(); Require(moved[5] == source[0] && audio.Loads == generation && audio.Position.TotalSeconds == 42 && session.Current!.Id == current, "Manual queue move leaves current decoder and position untouched");
            Require(session.State.Playlists.Single(p => p.Id == listId).TrackIds.SequenceEqual(source), "Queue edit does not change the source playlist");
            var invalid = await router.ExecuteAsync("queue move missing 1"); Require(!invalid.Success && (await Queue()).SequenceEqual(moved), "Invalid move is atomic");
            var snapshot = await session.CaptureTuiAsync(default); Require(snapshot.Queue.Select(t => t.Id).SequenceEqual(moved), "TUI sees the actual playback order");
            using (var terminal = new PlayerTerminalSession(router))
            {
                var tui = new ConsoleTuiView(session, router); tui.SetView(snapshot); tui.SelectPage("Queue");
                await tui.HandleKey(new('\0', ConsoleKey.DownArrow, false, false, true), terminal)!;
                Require((await Queue())[1] == moved[0] && audio.Loads == generation, "TUI Ctrl+Down moves queue through the shared backend");
                moved = await Queue();
            }
            var lyric = Path.Combine(output, "lyrics.lrc"); File.WriteAllText(lyric, "[00:01.000]逐行歌词\n[00:02.000]第二行", Encoding.UTF8);
            await Run("lyrics import " + Quote(lyric)); Require((await Run("lyrics show"))!.GetValue<string>().Contains("逐行歌词"), "Import and read lyrics");
            await Run("lyrics clear -y"); Require((await Run("lyrics show"))!.GetValue<string>() == "" && File.Exists(lyric), "Unlink preserves original lyrics");
            await Run("lyrics import " + Quote(lyric)); await Run("lyrics export " + Quote(Path.Combine(output, "export.lrc"))); await Run("lyrics reload");
            await Task.Delay(130); audio.Position += TimeSpan.FromSeconds(.13); await Run("player status");
            var hours = (await Run("statistics hours"))!.AsArray(); Require(hours.Count == 24 && hours.Sum(h => h!["seconds"]!.GetValue<double>()) > 0, "Actual audio advancement appears in the last 24 hours");
            var beforeStall = session.State.ListeningEntries.Sum(e => e.Seconds); await Task.Delay(100); await Run("player status"); Require(session.State.ListeningEntries.Sum(e => e.Seconds) == beforeStall, "Stalled decoder does not accrue listening time");
            await Run("player pause"); Require((await Run("statistics history session"))!.AsArray().Count > 0 && (await Run("statistics history interval"))!.AsArray().Count > 0 && (await Run("statistics history event"))!.AsArray().Any(e => e!["operation"]!.GetValue<string>() == "player.pause"), "Session, interval and playback event history");
            var exported = Path.Combine(output, "playlist.m3u8"); await Run("playlist export " + Quote(exported) + " " + listId);
            var count = session.State.Playlists.Count; await Run("playlist import " + Quote(exported)); Require(session.State.Playlists.Count == count + 1, "M3U relative/local import creates playlist");
            await Run("music undo"); Require(session.State.Playlists.Count == count && (await Queue()).SequenceEqual(moved), "Undo restores library without overwriting manual queue");
            await Run("album list"); await Run("artist list"); await Run("music search Track");
            var backup = Path.Combine(output, "backup.json"); await Run("data backup " + Quote(backup));
            await Run("queue move " + source[0] + " 1"); await Run("data restore " + Quote(backup) + " -y"); Require((await Queue()).SequenceEqual(moved), "Restore preserves the actual queued order");
            await Search(session, router, output);
            await Run("player stop"); Require(!audio.IsPlaying && audio.Position == TimeSpan.Zero, "Stop resets audio without discarding queue");
            Require((await router.ExecuteAsync("missing command")).Operation == "command.unknown", "Unknown CLI commands are ordinary output");
            await Run("queue play " + listId + " " + current); await Run("queue move " + source[0] + " 6"); moved = await Queue(); await Run("player pause");
        }
        await using (var reopened = new HeadlessPlayerSession(data, new Audio()))
        {
            var queue = (await new PlayerCommandRouter(reopened).ExecuteAsync("queue list")).Data!.AsArray().Select(t => t!["id"]!.GetValue<string>());
            Require(queue.SequenceEqual(moved) && reopened.State.ManualQueueOrder, "Actual queue order persists across restart");
        }
        await Network(output, media);
        Console.WriteLine("PASS CLI parity: fixed shuffle/mode switch/manual move/restart, TUI queue, M3U/folders/undo, lyrics, real listening history, three generic search sources and approved duplex LAN.");
    }
    private static async Task Search(HeadlessPlayerSession session, PlayerCommandRouter router, string output)
    {
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0"));
        var sources = new List<PluginManifest>();
        foreach (var i in Enumerable.Range(1, 3))
        {
            var manifest = new PluginManifest { Id = "fixture.cli-search-" + i, Name = "CLI Search " + i, Version = "1.0.0", ContractVersion = 2, Type = "extension", Runtime = "process", PageEntry = "page.json", Permissions = ["network", "process"], ProvidedServices = ["media-source"], MusicSearch = new() { Label = "Source " + i }, RequiredCapabilities = ["media.search.v1"], EntryPoints = new() { ["win-x64"] = "bin/NonetMusicPlayer.ExtensionFixture.exe" } };
            var package = Path.Combine(output, "source-" + i + ".impp");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, CoreJson.Options));
                using (var writer = new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"Generic fixture"}}""");
                foreach (var file in Directory.GetFiles(fixture).Where(f => !f.EndsWith(".pdb"))) zip.CreateEntryFromFile(file, "bin/" + Path.GetFileName(file));
            }
            var plugin = session.Plugins.Install(package); session.Plugins.SetEnabled(plugin, true); sources.Add(plugin);
        }
        foreach (var plugin in sources)
        {
            foreach (var query in new[] { "header:first", "header:second", "header:empty" })
            {
                var result = await router.ExecuteAsync("music query " + plugin.Id + " " + Quote(query)); Require(result.Success, "CLI query: " + result.Message);
                var tracks = result.Data!["items"]!.AsArray(); Require(query == "header:empty" ? tracks.Count == 0 : tracks.Count == 1 && tracks[0]!["title"]!.GetValue<string>() == query, "Each keyword/source replaces previous CLI results");
            }
        }
        var pages = await router.ExecuteAsync("music query " + sources[0].Id + " header:pages"); Require(pages.Success && pages.Data!["hasMore"]!.GetValue<bool>(), "CLI page cursor");
        Require((await router.ExecuteAsync("music more")).Data!["items"]!.AsArray().Count == 2, "CLI next page appends");
        var id = pages.Data!["items"]![0]!["id"]!.GetValue<string>(); Require((await router.ExecuteAsync("favorite add " + Quote(id))).Success, "Search result can be imported by stable ID");
        Require((await router.ExecuteAsync("plugins state " + sources[0].Id)).Success, "CLI extension state uses Contract v2");
        Require((await router.ExecuteAsync("plugins reload " + sources[0].Id)).Success && (await router.ExecuteAsync("music query " + sources[0].Id + " header:first")).Success, "Extension reload recreates process and handshake");
        session.Plugins.Configure(sources[0], """{"apiKey":"fixture-private-config","label":"public"}""");
        var protectedPath = ProtectedPluginConfiguration.PathFor(Path.Combine(session.DataDirectory, "Plugins"), sources[0]);
        Require(!File.ReadAllText(protectedPath).Contains("fixture-private-config") && !File.ReadAllText(Path.Combine(session.DataDirectory, "Plugins", "installed.json")).Contains("fixture-private-config")
            && ProtectedPluginConfiguration.Read(Path.Combine(session.DataDirectory, "Plugins"), sources[0])!.Contains("fixture-private-config"), "CLI config is account-encrypted and persists outside the index");
        var declarative = new PluginManifest { Id = "fixture.cli-command", Name = "CLI command", Version = "1.0.0", Type = "extension", ContractVersion = 2, Runtime = "declarative", PageEntry = "page.json", Permissions = ["player-control"], RequiredCapabilities = ["runtime.declarative.v1", "commands.v1"] };
        var declaration = Path.Combine(output, "declarative.impp");
        using (var zip = ZipFile.Open(declaration, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(declarative, CoreJson.Options));
            using (var writer = new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"Fixture"},"actions":{"volume":{"service":"commands","arguments":{"operation":"player.volume","arguments":["37"]}}}}""");
        }
        var installed = session.Plugins.Install(declaration); session.Plugins.SetEnabled(installed, true);
        var invocation = await router.ExecuteAsync("plugins invoke " + installed.Id + " volume -y").WaitAsync(TimeSpan.FromSeconds(5));
        Require(invocation.Success && session.State.Settings.Volume == 37 && invocation.Data!["state"]!["result"]!["success"]!.GetValue<bool>(), "Declarative action round-trips a permitted host command without self-deadlock");
    }
    private static async Task Network(string output, string media)
    {
        await using var first = new HeadlessPlayerSession(Path.Combine(output, "lan-a"), new Audio());
        await using var second = new HeadlessPlayerSession(Path.Combine(output, "lan-b"), new Audio());
        var a = new PlayerCommandRouter(first); var b = new PlayerCommandRouter(second);
        Require((await a.ExecuteAsync("playlist add liked " + Quote(media))).Success, "LAN publishes local playlist");
        Require((await a.ExecuteAsync("network sharing on")).Success, "CLI LAN sharing starts");
        var invitations = (await a.ExecuteAsync("network invitation")).Data!["invitations"]!.AsArray();
        var invitation = invitations.Select(n => n!.GetValue<string>()).Single(s => s.Contains("127.0.0.1"));
        var connection = b.ExecuteAsync("network connect " + Quote(invitation)); JsonArray? requests = null;
        for (var i = 0; i < 100; i++) { requests = (await a.ExecuteAsync("network requests")).Data!.AsArray(); if (requests.Count > 0) break; await Task.Delay(30); }
        Require(requests!.Count == 1 && !connection.IsCompleted, "Pairing waits for explicit approval");
        var requestId = requests[0]!["id"]!.GetValue<string>(); Require((await a.ExecuteAsync("network accept " + requestId)).Success, "CLI explicitly approves paired device");
        var result = await connection; Require(result.Success, "CLI duplex connection: " + result.Message);
        var peerId = result.Data!["id"]!.GetValue<string>();
        Require((await a.ExecuteAsync("network peers")).Data!.AsArray().Count == 1 && (await b.ExecuteAsync("network tracks " + peerId)).Data!.AsArray().Count == 12, "Both peers connect and remote tracks resolve");
        var remote = (await b.ExecuteAsync("network tracks " + peerId)).Data!.AsArray()[0]!["id"]!.GetValue<string>();
        Require((await b.ExecuteAsync("network play " + peerId + " liked")).Success && (await b.ExecuteAsync("queue list")).Data!.AsArray().Count == 12, "Remote playlist becomes a real playback queue");
        Require((await b.ExecuteAsync("favorite add " + Quote(remote))).Success, "Remote favorites persist references");
        var peer = typeof(HeadlessPlayerSession).GetField("_lanPeers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(second)!;
        var values = (IEnumerable<LanLibraryClient>)peer.GetType().GetProperty("Values")!.GetValue(peer)!;
        var paired = values.Single(); var songId = paired.Catalog.Tracks[0].Id;
        using (var stream = LanLibraryClient.OpenAudio(new Uri(paired.TrackUri(songId)))) { var bytes = new byte[64]; stream.ReadExactly(bytes); Require(stream.Length > 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF", "Paired audio stream reads real WAV bytes"); }
        Require((await b.ExecuteAsync("network disconnect " + peerId)).Success && second.State.Playlists[0].TrackIds.Contains(remote), "Disconnect preserves saved remote references");
        Require(!(await a.ExecuteAsync("network accept " + requestId)).Success, "Expired approval cannot be reused");
        Require((await a.ExecuteAsync("network sharing off")).Success && (await b.ExecuteAsync("network sharing off")).Success, "Sharing shuts down listeners and peers");
    }
    private static string Quote(string value) => TuiForm.Quote(value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Wave(string path)
    {
        using var writer = new BinaryWriter(File.Create(path)); const int size = 48000 * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + size); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Write(new byte[size]);
    }
    private sealed class Audio : IAudioPlayer
    {
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromMinutes(5); public float Volume { get; set; } public string DeviceName { get; set; } = "Playback.SystemDefault"; public IReadOnlyList<string> Devices => ["Playback.SystemDefault"]; public int Loads { get; private set; }
#pragma warning disable CS0067
        public event EventHandler? PlaybackStopped; public event EventHandler<AudioPlaybackErrorEventArgs>? PlaybackFailed; public event EventHandler<AudioOutputChangedEventArgs>? OutputDeviceChanged;
#pragma warning restore CS0067
        public Task LoadAsync(string path, CancellationToken token = default) { Loads++; Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; } public void Dispose() { }
    }
}
