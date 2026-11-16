using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>真实 TLS、插件 RPC 和双层 Range 流验证混合歌单共享；所有音源和资料均为独立夹具。</summary>
internal static class LanCloudSharingChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var media = new Media();
        var storage = new AppStorage(Path.Combine(root, "data")); using var audio = new Audio();
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 680 }; window.Show(); Pump();
        try
        {
            var fixture = Path.GetFullPath("tests/NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0");
            var plugins = Enumerable.Range(0, 3).Select(i => Install(vm, fixture, root, i)).ToArray();
            foreach (var plugin in plugins)
            {
                var directory = Path.Combine(storage.PluginsFolder, "Storage", plugin.Id); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "lan-playback-source.txt"), media.Url);
            }
            var path = Path.Combine(root, "local.audio"); File.WriteAllBytes(path, media.Bytes);
            var cover = Path.Combine(root, "cover.png"); File.WriteAllBytes(cover, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/ADSY2/8FUgKnWOt33AAAAABJRU5ErkJggg=="));
            var playlist = vm.CreatePlaylist("Mixed playlist");
            var local = new TrackItem("local", "Local", "Artist", "Album", path, ".audio", media.Bytes.Length);
            var cloud = plugins.Select((p, i) => new TrackItem("cloud-" + i, "Cloud " + i, "Artist", "Album", "provider://reference", ".audio", 0) { ProviderId = p.Id, ProviderTrackId = "remote-1", DurationSeconds = 80 }).ToArray();
            vm.AddToPlaylist(playlist, new[] { local }.Concat(cloud));
            // 已保存的另一设备引用不能变成电脑到另一台设备的递归代理。
            vm.State.Tracks.Add(new("other-device", "Other device", "", "", "nonet-lan://other/track", ".lan", 0) { ProviderId = "lan:other", ProviderTrackId = "track" }); playlist.TrackIds.Add("other-device");
            vm.UpdatePlaylistDetails(playlist, playlist.Name, "Description · 描述 · 説明", cover);
            vm.ConfirmLanConnection = (_, _) => Task.FromResult(true); vm.SetLanSharing(true);
            var server = (LanLibraryServer)typeof(MainViewModel).GetField("_lanServer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
            var invitation = server.Invitation("127.0.0.1");
            using (var raw = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(cert.RawData)) == server.Fingerprint }))
            {
                var origin = LanAddress.ParseInvitation(invitation).Origin;
                Check(Wait(raw.GetAsync(new Uri(origin, "v1/audio/cloud-0"))).StatusCode == HttpStatusCode.Unauthorized, "Unauthenticated requests cannot invoke plugins");
                Check(!File.Exists(Path.Combine(storage.PluginsFolder, "Storage", plugins[0].Id, "lan-resolve-count.txt")), "No plugin resolution before approval");
            }
            using var phone = Wait(LanLibraryClient.ConnectAsync(invitation, Guid.NewGuid().ToString("N"), "Phone fixture"));
            var list = phone.Catalog.Playlists.Single(p => p.Id == playlist.Id);
            Check(list.Description == playlist.Description && list.Artwork && list.ArtworkRevision.Length == 64, "Playlist metadata includes description and cover revision");
            Check(list.TrackIds.SequenceEqual(new[] { "local", "cloud-0", "cloud-1", "cloud-2" }), "Mixed playlist order is complete and LAN references are excluded");
            Check(Wait(phone.ReadPlaylistArtworkAsync(list.Id)).SequenceEqual(File.ReadAllBytes(playlist.CoverPath!)), "Playlist artwork is transferred, not a local path");
            var json = JsonSerializer.Serialize(phone.Catalog, LanJson.Default.LanCatalog);
            Check(!json.Contains(root, StringComparison.Ordinal) && !json.Contains(media.Url, StringComparison.Ordinal) && !json.Contains("token=", StringComparison.Ordinal), "Catalog hides local paths and plugin credentials");
            // 第一小块在插件尚未交付整段音频时即可被手机读取。
            var firstRead = Task.Run(() => { using var stream = LanLibraryClient.OpenAudio(new Uri(phone.TrackUri("cloud-0"))); var bytes = new byte[4096]; stream.ReadExactly(bytes); return bytes; });
            var first = Wait(firstRead);
            Check(first.SequenceEqual(media.Bytes.Take(4096)) && !media.Continue.Task.IsCompleted, "Playback is incremental, not a full-file download");
            media.Continue.TrySetResult();
            foreach (var track in new[] { local }.Concat(cloud))
            {
                Wait(Task.Run(() =>
                {
                    using var stream = LanLibraryClient.OpenAudio(new Uri(phone.TrackUri(track.Id)));
                    Check(stream.Length == media.Bytes.Length, "HEAD reports actual media length");
                    stream.Seek(1_500_000, SeekOrigin.Begin); var bytes = new byte[1000]; stream.ReadExactly(bytes);
                    Check(bytes.SequenceEqual(media.Bytes.Skip(1_500_000).Take(1000)), "Seek/Range returns matching upstream bytes");
                }));
            }
            Check(audio.Loads == 0 && !vm.IsPlaying && vm.CurrentTrack is null, "Remote playback never changes desktop playback state");
            foreach (var plugin in plugins) Check(File.ReadAllText(Path.Combine(storage.PluginsFolder, "Storage", plugin.Id, "lan-resolve-count.txt")) == "1", "HEAD and Range share a bounded resolution lease");
            Wait(vm.Plugins.EnrichSourceTrackAsync(cloud[1], vm.Lyrics)); vm.Save(); Wait(phone.RefreshAsync());
            Check(Wait(phone.ReadAssetAsync("artwork", cloud[1].Id)).Length > 0, "Plugin song artwork also uses the desktop asset route");
            var previous = phone.Catalog.Revision;
            vm.UpdatePlaylistDetails(playlist, playlist.Name, "Updated description", null); Wait(phone.RefreshAsync());
            Check(phone.Catalog.Revision != previous && phone.Catalog.Playlists.Single(p => p.Id == playlist.Id).Description == "Updated description", "Description changes refresh the peer catalog");
            vm.DisablePlugin(plugins[2]); Wait(phone.RefreshAsync()); Check(phone.Catalog.Tracks.Any(t => t.Id == cloud[2].Id), "Disabled source references stay in the playlist");
            phone.Dispose(); vm.SetLanSharing(false);
            Console.WriteLine("PASS mixed local/three-plugin playlists, metadata/artwork revisions, real plugin RPC, authenticated incremental relay, HEAD/Range/seek, privacy, no desktop playback changes and no recursive forwarding.");
        }
        finally { media.Continue.TrySetResult(); vm.SetLanSharing(false); window.Close(); Pump(); }
    }
    private static PluginManifest Install(MainViewModel vm, string fixture, string root, int index)
    {
        var manifest = new PluginManifest { Id = "fixture.lan-source-" + index, Name = "LAN source " + index, Version = "1.0.0", ContractVersion = 2, Type = "extension", Runtime = "process", PageEntry = "page.json", Permissions = ["network", "process", "navigation"], ProvidedServices = ["media-source"], MusicSearch = new() { Label = "LAN source" }, RequiredCapabilities = ["media.search.v1"], EntryPoints = new() { ["win-x64"] = "bin/NonetMusicPlayer.ExtensionFixture.exe" } };
        var path = Path.Combine(root, manifest.Id + ".impp");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, AppStorage.Json));
            using (var writer = new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"LAN fixture"}}""");
            foreach (var file in Directory.GetFiles(fixture).Where(f => !f.EndsWith(".pdb"))) zip.CreateEntryFromFile(file, "bin/" + Path.GetFileName(file));
        }
        var installed = vm.Plugins.Install(path); vm.Plugins.SetEnabled(installed, true); return installed;
    }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { var end = Environment.TickCount64 + 20_000; while (!task.IsCompleted && Environment.TickCount64 < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(3); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Audio : IAudioPlayer
    {
        public int Loads; public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) { Loads++; return Task.CompletedTask; }
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
    private sealed class Media : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _stop = new(); private int _gets;
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] Bytes { get; } = Enumerable.Range(0, 2_500_000).Select(i => (byte)(i % 251)).ToArray();
        public string Url { get; }
        public Media() { _listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/audio?token={new string('A', 64)}"; _ = Accept(); }
        private async Task Accept() { try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = Serve(client); } } catch (Exception e) when (e is OperationCanceledException or SocketException) { } }
        private async Task Serve(TcpClient client)
        {
            using (client) try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true); var first = await reader.ReadLineAsync(_stop.Token); string? range = null;
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line) if (line.StartsWith("Range: ", StringComparison.OrdinalIgnoreCase)) range = line[7..];
                long start = 0, end = Bytes.Length - 1; if (range is not null && !LanLibraryServer.TryRange(range, Bytes.Length, out start, out end)) return;
                var header = $"HTTP/1.1 {(range is null ? "200 OK" : "206 Partial Content")}\r\nContent-Length: {end - start + 1}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n" + (range is null ? "" : $"Content-Range: bytes {start}-{end}/{Bytes.Length}\r\n") + "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _stop.Token);
                if (first!.StartsWith("HEAD")) return;
                var count = (int)(end - start + 1); var block = Math.Min(count, 4096);
                await stream.WriteAsync(Bytes.AsMemory((int)start, block), _stop.Token);
                if (Interlocked.Increment(ref _gets) == 1) await Continue.Task.WaitAsync(_stop.Token);
                if (count > block) await stream.WriteAsync(Bytes.AsMemory((int)start + block, count - block), _stop.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
        }
        public void Dispose() { Continue.TrySetResult(); _stop.Cancel(); _listener.Stop(); }
    }
}
