using System.Text;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Playback;
using NonetMusicPlayer.Core.Runtime;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/core-command-checks"); Directory.CreateDirectory(root);
            NonetMusicPlayer.Core.Diagnostics.AppLog.Initialize(Path.Combine(root, "initial-logs-" + Guid.NewGuid().ToString("N")));
            AgentPolicyChecks.Run();
            await TerminalContractChecks.RunAsync(root);
            await PluginUpdateChecks.RunAsync(root);
            await PluginPlatformChecks.RunAsync(root);
            await TuiInteractionChecks.RunAsync(root);
            var shuffled = new WeightedShuffleSelector(new Random(174));
            Require(shuffled.Choose([], null) is null, "Empty shuffle"); Require(shuffled.Choose(["only", "only"], "only") == "only", "One distinct song repeats");
            for (var i = 0; i < 50; i++) shuffled.Choose(["old"], null);
            Require(shuffled.Weight("old") < shuffled.Weight("fresh") && shuffled.Weight("old") > 0, "Previously chosen songs lose probability without being excluded");
            for (var i = 0; i < 2000; i++) Require(shuffled.Choose(["old", "fresh", "another"], "fresh") != "fresh", "Never repeat current when another song exists");
            var chosenOld = 0; var chosenNew = 0;
            for (var i = 0; i < 2000; i++)
            {
                var selector = new WeightedShuffleSelector(new Random(i)); for (var j = 0; j < 12; j++) selector.Choose(["old"], null);
                if (selector.Choose(["old", "new"], null) == "old") chosenOld++; else chosenNew++;
            }
            Require(chosenOld > 0 && chosenNew > chosenOld * 5, "Sampling agrees with history weighting");
            var parsed = CommandLineParser.Parse("nonet playlist add 'my list' \"D:\\音乐\\Test.wav\" -y");
            Require(parsed.Name == "playlist.add" && parsed.Arguments[1] == "D:\\音乐\\Test.wav" && parsed.Confirmed, "Quoted Windows path and explicit confirmation");
            Require(CommandLineParser.Parse("nonet player play").Name == "player.play", "Public space-separated command");
            var backend = new ProbeBackend(); var probe = new PlayerCommandRouter(backend);
            Require(!(await probe.ExecuteAsync("powershell whoami")).Success && backend.Calls == 0, "Shell command rejected");
            Require(!(await probe.ExecuteAsync("nonet player pause extra")).Success && backend.Calls == 0, "Invalid arity rejected before mutation");
            Require((await probe.ExecuteAsync("nonet playlist delete x")).RequiresConfirmation && backend.Calls == 0, "Dangerous action does not run without confirmation");
            Require((await probe.ExecuteAsync("nonet playlist delete x -y")).Success && backend.Calls == 1, "Explicit -y authorizes action");
            Require(!(await probe.ExecuteAsync("nonet window maximize")).Success, "Headless host rejects window commands");
            probe.Publish(CommandResults.Completed("music.list", new JsonArray(Enumerable.Range(0, 10000).Select(i => (JsonNode)JsonValue.Create(i)!).ToArray())));
            Require(probe.Journal.Last().Data!["items"]!.AsArray().Count == 100, "Large query history is bounded rather than retaining the entire library");
            using (var terminal = new PlayerTerminalSession(probe))
            {
                Require(terminal.Snapshot.Prompt == "nonet $ " && terminal.Snapshot.AcceptsInput, "Short shared terminal prompt");
                terminal.SetDraft("nonet player volume 42");
                probe.Publish(CommandResults.Completed("player.pause"));
                Require(terminal.Snapshot.Draft == "nonet player volume 42", "Asynchronous UI results preserve command draft");
                await terminal.SubmitAsync();
                NonetMusicPlayer.Core.Diagnostics.AppLog.Flush();
                Require(terminal.Snapshot.Transcript.Contains("nonet $ nonet player volume 42") && terminal.Snapshot.Transcript.Contains("INFO"), "Command echo and transaction log share transcript");
                terminal.SetDraft("unfinished"); await terminal.ClearAsync();
                Require(terminal.Snapshot.Transcript == "" && terminal.Snapshot.Draft == "unfinished", "Clear removes display only and preserves current edit");
                terminal.Recall(-1); Require(terminal.Snapshot.Draft == "nonet player volume 42", "History survives clear");
                terminal.SetDraft("nonet playlist delete x"); var deleting = terminal.SubmitAsync();
                Require(!deleting.IsCompleted && terminal.Snapshot.Prompt.Contains("y/N"), "Destructive operation confirms inline");
                terminal.SetDraft("n"); await terminal.SubmitAsync(); await deleting;
                Require(terminal.Snapshot.Transcript.Contains("y/N") && terminal.Snapshot.Prompt == PlayerTerminalSession.CommandPrompt, "Confirmation remains in transcript, returns to prompt");
                terminal.SetDraft("nonet plugins config p '{\"token\":\"private-test-token\"}'"); await terminal.SubmitAsync();
                Require(!terminal.Snapshot.Transcript.Contains("private-test-token"), "Secret configuration is not echoed");
                terminal.Recall(-1); Require(!terminal.Snapshot.Draft.Contains("private-test-token"), "Secret configuration is excluded from history");
                terminal.SetDraft("status\nplayer.pause"); Require(!terminal.Snapshot.Draft.Contains('\n'), "Pasting multiple lines cannot execute a hidden second command");
                terminal.SetDraft("nonet clear"); await terminal.SubmitAsync();
                Require(terminal.Snapshot.Text == PlayerTerminalSession.CommandPrompt && probe.Journal.Count == 1, "Shared clear command resets visible transcript and bounded result journal");
            }
            var foreignDirectory = Path.Combine(root, "desktop-data-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(foreignDirectory);
            var foreignState = Path.Combine(foreignDirectory, "state.json"); File.WriteAllText(foreignState, "{\"tracks\":[],\"settings\":{\"uiRevision\":1}}");
            var originalState = File.ReadAllText(foreignState); var refused = false;
            try { await using var foreign = new HeadlessPlayerSession(foreignDirectory, new FakeAudio()); }
            catch (InvalidDataException) { refused = true; }
            Require(refused && File.ReadAllText(foreignState) == originalState && Directory.GetFileSystemEntries(foreignDirectory).Length == 1, "CLI refuses desktop Data before any write");
            File.Delete(foreignState); File.WriteAllText(Path.Combine(foreignDirectory, "library.db"), "desktop database sentinel"); refused = false;
            try { await using var foreign = new HeadlessPlayerSession(foreignDirectory, new FakeAudio()); } catch (InvalidDataException) { refused = true; }
            Require(refused && Directory.GetFileSystemEntries(foreignDirectory).Length == 1, "CLI also refuses SQLite desktop data before any write");
            File.WriteAllText(foreignState, originalState);
            var fixture = Path.Combine(root, "song.wav"); WriteWave(fixture); File.WriteAllText(Path.ChangeExtension(fixture, ".lrc"), "[00:01]Hello", Encoding.UTF8);
            var data = Path.Combine(root, Guid.NewGuid().ToString("N")); var audio = new FakeAudio(); string playlistId; string trackId;
            await using (var session = new HeadlessPlayerSession(data, audio))
            {
                var router = new PlayerCommandRouter(session);
                playlistId = (await router.ExecuteAsync("nonet playlist create \"My playlist\"")).Data!["id"]!.GetValue<string>();
                Require((await router.ExecuteAsync($"nonet playlist add {playlistId} \"{fixture}\"")).Success, "Import into playlist");
                trackId = session.State.Tracks.Single().Id;
                var tui = await session.CaptureTuiAsync(default); Require(tui.Music.Single().Id == trackId && tui.Playlists.Any(p => p.Id == playlistId), "TUI observes shared command state without GUI dependency");
                Require(!(await router.ExecuteAsync("nonet data restore \"" + foreignState + "\" -y")).Success && session.State.Tracks.Single().Id == trackId, "Foreign-host backup rejection leaves CLI library intact");
                Require(File.Exists(Path.Combine(session.LyricsDirectory, trackId + ".lrc")), "Adjacent lyrics imported");
                Require((await router.ExecuteAsync("nonet queue play " + playlistId)).Success && audio.IsPlaying, "Shared player starts playlist");
                Require((await router.ExecuteAsync("nonet player seek 3")).Success && audio.Position.TotalSeconds == 3, "Seek reaches backend");
                Require((await router.ExecuteAsync("nonet player pause")).Success && !audio.IsPlaying, "Pause reaches backend");
                Require((await router.ExecuteAsync("nonet player resume")).Success && audio.IsPlaying && audio.Position.TotalSeconds == 3, "Resume retains position");
                Require(!(await router.ExecuteAsync("nonet settings set volume 200")).Success && session.State.Settings.Volume == 80, "Invalid settings are not silently applied");
                Require((await router.ExecuteAsync("nonet player volume 45")).Success && Math.Abs(audio.Volume - .45) < .001, "Volume applies");
                Require((await router.ExecuteAsync("nonet playlist delete " + playlistId)).RequiresConfirmation && session.State.Playlists.Count == 2, "Playlist mutation requires confirmation");
                Require((await router.ExecuteAsync("nonet music remove " + trackId + " -y")).Success && File.Exists(fixture), "Removing a track never deletes original audio");
                var previousListening = session.State.ListeningEntries.Sum(e => e.Seconds);
                Require((await router.ExecuteAsync("nonet player play \"" + fixture + "\"")).Success && session.State.Tracks.Count == 0 && session.State.History.Count == 1, "Temporary playback stays out of library but enters history");
                Require(session.State.ListeningEntries.Sum(e => e.Seconds) == previousListening, "Temporary playback not counted");
                Require((await router.ExecuteAsync("nonet favorite add " + trackId)).Success && session.State.Tracks.Count == 1, "Temporary audio can be added to liked playlist");
                audio.Position = TimeSpan.FromSeconds(4); await router.ExecuteAsync("nonet player pause");
            }
            await using (var restored = new HeadlessPlayerSession(data, new FakeAudio()))
            {
                Require(restored.Current?.Id == trackId && restored.State.LastPosition == 4 && restored.State.Settings.Volume == 45, "State restores without autoplay");
                Require(restored.State.Playlists.Single(p => p.Id == "liked").TrackIds.Contains(trackId), "Favorites persist");
            }
            Require(!typeof(HeadlessPlayerSession).Assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("Avalonia", StringComparison.Ordinal)), "Core has no Avalonia dependency");
            foreach (var key in LocalizationCatalog.Keys) Require(LocalizationCatalog.Translations(key).Count == 3, "Three-language catalogs");
            if (args.Length > 1) await CheckProviderRestartAsync(root, args[1]);
            Console.WriteLine($"PASS CORE: English resources, no GUI dependency, weighted shuffle ({chosenOld}/{chosenNew}), parser, command safety/arity/confirmation, import/lyrics, audio controls, independent state and temporary exclusions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    /// <summary>实际音源进程跨宿主重启测试，验证精简包测试发现的目录初始化缺失。</summary>
    private static async Task CheckProviderRestartAsync(string root, string package)
    {
        var data = Path.Combine(root, "native-provider-" + Guid.NewGuid().ToString("N")); string trackId;
        await using (var first = new HeadlessPlayerSession(data))
        {
            var router = new PlayerCommandRouter(first);
            Require((await router.ExecuteAsync($"nonet plugins install '{Path.GetFullPath(package)}' -y")).Success, "Native provider install");
            var configuration = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["fixtureFolder"] = root });
            Require((await router.ExecuteAsync($"nonet plugins config sample.http-provider '{configuration}'")).Success, "Native provider configure");
            Require((await router.ExecuteAsync("nonet plugins enable sample.http-provider -y")).Success, "Native provider enable");
            Require((await router.ExecuteAsync("nonet plugins refresh sample.http-provider")).Success, "Native provider catalog");
            trackId = first.State.Tracks.First().Id;
        }
        await using (var second = new HeadlessPlayerSession(data))
        {
            var router = new PlayerCommandRouter(second);
            Require((await router.ExecuteAsync("nonet player play " + trackId)).Success, "Provider playback after host restart without manual catalog refresh");
            Require((await router.ExecuteAsync("nonet player seek 2")).Success, "Provider ranged seek after host restart");
            Require((await router.ExecuteAsync("nonet player pause")).Success, "Provider pause");
            var status = (await router.ExecuteAsync("nonet player status")).Data!;
            Require(status["duration"]!.GetValue<double>() >= 7 && status["position"]!.GetValue<double>() >= 1.9 && status["playing"]!.GetValue<bool>() == false, "Native streaming duration, retained seek and pause");
            Require((await router.ExecuteAsync("nonet plugins uninstall sample.http-provider -y")).Success && second.Plugins.Installed.Count == 0, "Provider disable-before-uninstall");
        }
        Console.WriteLine("PASS PROVIDER RESTART: real process, saved catalog, automatic reinitialization, loopback streaming, ranged seek and uninstall.");
    }
    private static void WriteWave(string path)
    {
        using var writer = new BinaryWriter(File.Create(path)); const int bytes = 44100 * 2 * 8;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
    }
    private sealed class ProbeBackend : IPlayerCommandBackend
    {
        public bool HasWindow => false; public int Calls;
        public Task<CommandResult> ExecuteAsync(PlayerCommand command, CancellationToken token) { Calls++; return Task.FromResult(CommandResults.Completed(command.Name)); }
    }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(8); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string file, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; } public void Dispose() { }
    }
}
