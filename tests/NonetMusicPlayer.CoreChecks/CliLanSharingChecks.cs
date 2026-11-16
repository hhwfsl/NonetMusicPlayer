using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Core.Runtime;

/// <summary>CLI 使用独立夹具验证真实插件解析、TLS/Range、只读投影及未收藏播放的统计。</summary>
internal static class CliLanSharingChecks
{
    public static async Task RunAsync(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var media = new Media();
        var hostAudio = new Audio(); var receiverAudio = new Audio(); var receiverData = Path.Combine(root, "receiver");
        await using (var source = new HeadlessPlayerSession(Path.Combine(root, "source"), hostAudio))
        await using (var receiver = new HeadlessPlayerSession(receiverData, receiverAudio))
        {
            var a = new PlayerCommandRouter(source); var b = new PlayerCommandRouter(receiver);
            async Task<JsonNode?> Run(PlayerCommandRouter router, string command) { var result = await router.ExecuteAsync(command); Check(result.Success, command + ": " + result.Message); return result.Data; }
            var wav = Path.Combine(root, "Local.wav"); DesktopParityChecks.Wave(wav); File.WriteAllText(Path.ChangeExtension(wav, ".lrc"), "[00:00]Source lyrics");
            var cover = Path.Combine(root, "cover.png"); File.WriteAllBytes(cover, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/ADSY2/8FUgKnWOt33AAAAABJRU5ErkJggg=="));
            var listId = (await Run(a, "playlist create Shared"))!["id"]!.GetValue<string>();
            await Run(a, "playlist add " + listId + " " + Quote(wav));
            var local = source.State.Tracks.Single(); var cloudIds = new List<string>(); var pluginIds = new List<string>();
            for (var i = 0; i < 3; i++) {
                var plugin = Install(source, root, i); pluginIds.Add(plugin.Id);
                var storage = Path.Combine(source.DataDirectory, "Plugins", "Storage", plugin.Id); Directory.CreateDirectory(storage);
                File.WriteAllText(Path.Combine(storage, "lan-playback-source.txt"), media.Url);
                var cloud = (await Run(a, "music query " + plugin.Id + " all"))!["items"]!.AsArray()[0]!["id"]!.GetValue<string>();
                cloudIds.Add(cloud); await Run(a, "playlist add " + listId + " " + Quote(cloud));
            }
            source.State.Tracks.Add(new() { ProviderId = "lan:other", ProviderTrackId = "x", Metadata = new("lan:other:x", "Other", "", "", "nonet-lan://other/x", ".remote", 0, 0, null) });
            source.State.Playlists.Single(p => p.Id == listId).TrackIds.Add("lan:other:x");
            await Run(a, "playlist describe " + listId + " " + Quote("Description 描述 説明")); await Run(a, "playlist cover " + listId + " " + Quote(cover));
            await Run(a, "network sharing on");
            var invitation = (await Run(a, "network invitation"))!["invitations"]!.AsArray().Select(n => n!.GetValue<string>()).Single(s => s.Contains("127.0.0.1"));
            var connecting = b.ExecuteAsync("network connect " + Quote(invitation));
            JsonArray? requests = null;
            for (var i = 0; i < 200; i++) { requests = (await Run(a, "network requests"))!.AsArray(); if (requests.Count > 0) break; await Task.Delay(20); }
            Check(requests!.Count == 1, "Explicit approval exists"); await Run(a, "network accept " + requests[0]!["id"]!.GetValue<string>());
            var connected = await connecting; Check(connected.Success, connected.Message); var peerId = connected.Data!["id"]!.GetValue<string>();
            var peers = (System.Collections.Concurrent.ConcurrentDictionary<string, LanLibraryClient>)typeof(HeadlessPlayerSession).GetField("_lanPeers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(receiver)!;
            var peer = peers[peerId]; var shared = peer.Catalog.Playlists.Single(p => p.Id == listId); var composite = "peer:" + peerId + ":" + listId;
            Check(shared.Description == "Description 描述 説明" && shared.Artwork && shared.ArtworkRevision.Length == 64 && shared.TrackIds.Length == 4 && !shared.TrackIds.Contains("lan:other:x"), "All local/plugin songs and playlist metadata transfer, excluding recursive references");
            Check((await peer.ReadPlaylistArtworkAsync(listId)).SequenceEqual(File.ReadAllBytes(source.State.Playlists.Single(p => p.Id == listId).CoverPath!)), "Playlist artwork travels through authenticated endpoint");
            await Run(b, "playlist tracks " + Quote(composite));
            var metadata = (await Run(b, "playlist list"))!.AsArray().Single(n => n!["id"]!.GetValue<string>() == composite)!;
            Check(metadata["readOnly"]!.GetValue<bool>() && File.Exists(metadata["coverPath"]!.GetValue<string>()) && metadata["description"]!.GetValue<string>().Contains("描述"), "CLI exposes remote read-only playlist and cached cover");
            Check((await Run(b, "music list"))!.AsArray().Count == 4 && (await Run(b, "album tracks 'Fixture album'"))!.AsArray().Count == 3 && (await Run(b, "artist tracks 'Fixture artist'"))!.AsArray().Count == 3, "Browse/album/artist use online projection");
            Check((await Run(b, "music query local Cloud"))!["items"]!.AsArray().Count == 3, "Local query includes connected songs");
            var tui = await receiver.CaptureTuiAsync(default); Check(tui.Playlists.Any(p => p.Id == composite) && tui.PlaylistTracks[composite].Count == 4 && tui.Music.Count == 4, "TUI exposes remote lists and songs");
            foreach (var cloud in cloudIds) using (var stream = LanLibraryClient.OpenAudio(new Uri(peer.TrackUri(cloud)))) {
                stream.Seek(65000, SeekOrigin.Begin); var bytes = new byte[4096]; stream.ReadExactly(bytes);
                Check(stream.Length == media.Bytes.Length && bytes.SequenceEqual(media.Bytes.Skip(65000).Take(4096)), "Real plugin/TLS/Range relay returns correct audio");
            }
            Check(hostAudio.Loads == 0 && source.State.ListeningEntries.Count == 0, "Serving audio does not play or count on source");
            foreach (var plugin in pluginIds) Check(File.ReadAllText(Path.Combine(source.DataDirectory, "Plugins", "Storage", plugin, "lan-resolve-count.txt")) == "1", "HEAD and ranged GET reuse bounded playback resolution");
            await Run(a, "plugins reload " + pluginIds[0]);
            using (var reloaded = LanLibraryClient.OpenAudio(new Uri(peer.TrackUri(cloudIds[0])))) Check(reloaded.ReadByte() == media.Bytes[0], "Reloaded source continues serving real audio");
            Check(File.ReadAllText(Path.Combine(source.DataDirectory, "Plugins", "Storage", pluginIds[0], "lan-resolve-count.txt")) == "2", "Plugin reload invalidates old playback URL cache");
            var remoteLocal = "lan:" + peerId + ":" + local.Id;
            receiver.State.DisabledLyrics.Add(remoteLocal); File.WriteAllText(Path.Combine(receiver.LyricsDirectory, MusicFileReader.StableId(remoteLocal) + ".lrc"), "Old receiver override");
            await Run(b, "queue play " + Quote(composite) + " " + Quote(remoteLocal));
            Check((await Run(b, "lyrics show"))!.GetValue<string>().Contains("Source lyrics"), "Remote source lyrics override neither source nor local files, ignoring legacy receiver edits");
            var current = receiver.Current!; Check(receiver.State.Tracks.Count == 0, "Remote playback does not auto-favorite");
            await Task.Delay(140); receiverAudio.Position += TimeSpan.FromSeconds(.14); await Run(b, "player status");
            Check(receiver.State.ListeningEntries.Sum(e => e.Seconds) > 0 && (await Run(b, "statistics hours"))!.AsArray().Sum(h => h!["seconds"]!.GetValue<double>()) > 0, "Unfavorited remote audio accrues actual time and hourly chart");
            await Run(b, "player pause");
            Check(!(await b.ExecuteAsync("lyrics import " + Quote(Path.ChangeExtension(wav, ".lrc")))).Success && !(await b.ExecuteAsync("lyrics clear -y")).Success, "Remote lyrics cannot be overridden");
            Check(!(await b.ExecuteAsync("playlist describe " + Quote(composite) + " changed")).Success && !(await b.ExecuteAsync("music remove " + Quote(current.Id) + " -y")).Success, "Remote source edits rejected");
            await Run(b, "favorite add " + Quote(current.Id)); await Run(b, "network disconnect " + peerId);
            Check((await Run(b, "music list"))!.AsArray().Count == 0 && (await Run(b, "album list"))!.AsArray().Count == 0 && (await Run(b, "artist list"))!.AsArray().Count == 0 && (await Run(b, "music results"))!["items"]!.AsArray().Count == 0, "Disconnect removes all remote browsing projections");
            Check((await receiver.CaptureTuiAsync(default)).Playlists.Count == 1 && receiver.State.Playlists[0].TrackIds.Contains(current.Id) && receiver.State.ListeningEntries.Sum(e => e.Seconds) > 0, "Personal reference and listening totals remain after disconnect");
            var info = (await Run(a, "music info " + local.Id))!.AsObject(); Check(info["sampleRate"]!.GetValue<long>() == 48000 && info["channels"]!.GetValue<long>() == 1 && info["fileSize"]!.GetValue<long>() > 0, "CLI rich info reports real audio specs");
        }
        await using (var reopened = new HeadlessPlayerSession(receiverData, new Audio())) Check(reopened.State.ListeningEntries.Sum(e => e.Seconds) > 0 && (await reopened.CaptureTuiAsync(default)).Music.Count == 0, "Statistics persist across restart without restoring offline browsing projection");
        Console.WriteLine("PASS CLI LAN: three real media-source plugins, authenticated covers/descriptions and Range relay, remote TUI/groups/search, read-only edits, unfavorited statistics/disconnect/restart, rich audio information.");
    }
    private static PluginManifest Install(HeadlessPlayerSession session, string root, int index)
    {
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0"));
        var manifest = new PluginManifest { Id = "fixture.cli-lan-" + index, Name = "Generic LAN source " + index, Version = "1.0.0", Type = "extension", ContractVersion = 2, Runtime = "process", PageEntry = "page.json", Permissions = ["network", "process"], ProvidedServices = ["media-source"], MusicSearch = new() { Label = "Generic " + index }, RequiredCapabilities = ["media.search.v1"], EntryPoints = new() { ["win-x64"] = "bin/NonetMusicPlayer.ExtensionFixture.exe" } };
        var package = Path.Combine(root, manifest.Id + ".impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, CoreJson.Options));
            using (var writer = new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"Fixture"}}""");
            foreach (var path in Directory.GetFiles(fixture).Where(f => !f.EndsWith(".pdb"))) zip.CreateEntryFromFile(path, "bin/" + Path.GetFileName(path));
        }
        var installed = session.Plugins.Install(package); session.Plugins.SetEnabled(installed, true); return installed;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Quote(string text) => NonetMusicPlayerCli.TuiForm.Quote(text);
    private sealed class Audio : IAudioPlayer
    {
        public int Loads; public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromMinutes(5); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) { Loads++; Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() => IsPlaying = false; public void Dispose() { }
    }
    private sealed class Media : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new();
        public byte[] Bytes { get; } = Enumerable.Range(0, 250_000).Select(i => (byte)(i % 251)).ToArray(); public string Url { get; }
        public Media() { _listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/audio?token={new string('A', 64)}"; _ = Accept(); }
        private async Task Accept() { try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = Serve(client); } } catch (Exception e) when (e is OperationCanceledException or SocketException) { } }
        private async Task Serve(TcpClient client) {
            using (client) try {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true); var first = await reader.ReadLineAsync(_stop.Token); string? range = null;
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line) if (line.StartsWith("Range: ", StringComparison.OrdinalIgnoreCase)) range = line[7..];
                long start = 0, end = Bytes.Length - 1; if (range is not null && !LanLibraryServer.TryRange(range, Bytes.Length, out start, out end)) return;
                var header = $"HTTP/1.1 {(range is null ? "200 OK" : "206 Partial Content")}\r\nContent-Length: {end - start + 1}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n" + (range is null ? "" : $"Content-Range: bytes {start}-{end}/{Bytes.Length}\r\n") + "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _stop.Token); if (first!.StartsWith("HEAD")) return;
                await stream.WriteAsync(Bytes.AsMemory((int)start, (int)(end - start + 1)), _stop.Token);
            } catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }
}
