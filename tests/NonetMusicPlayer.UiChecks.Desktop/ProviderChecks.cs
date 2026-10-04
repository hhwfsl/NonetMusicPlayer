using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

internal static class ProviderChecks
{
    public static void Run(MainViewModel vm, string package, string fixtureFolder, bool nativeAudio)
    {
        var manifest = vm.Plugins.Install(Path.GetFullPath(package));
        vm.Plugins.Configure(manifest, JsonSerializer.Serialize(new Dictionary<string, string> { ["fixtureFolder"] = fixtureFolder, ["authorization"] = "Bearer session-secret" }, AppStorage.Json));
        var persisted = File.ReadAllText(Path.Combine(vm.Storage.PluginsFolder, "installed.json"));
        Check(!persisted.Contains("session-secret"), "Plugin secrets must not persist");
        vm.Plugins.SetEnabled(manifest, true); ApplicationIntegrationChecks.Wait(vm.RefreshProviderAsync(manifest));
        var track = vm.State.Tracks.FirstOrDefault(t => t.ProviderId == manifest.Id) ?? throw new InvalidOperationException("Provider catalog failed: " + vm.StatusText);
        var source = ApplicationIntegrationChecks.Wait(vm.Plugins.ResolveAsync(track)); LoopbackRangeStream.Validate(new Uri(source));
        using (var http = new HttpClient())
        {
            using var head = ApplicationIntegrationChecks.Wait(http.SendAsync(new HttpRequestMessage(HttpMethod.Head, source)));
            Check(head.IsSuccessStatusCode && head.Content.Headers.ContentLength > 1000, "Loopback HEAD length");
            using var request = new HttpRequestMessage(HttpMethod.Get, source); request.Headers.Range = new RangeHeaderValue(0, 15);
            using var partial = ApplicationIntegrationChecks.Wait(http.SendAsync(request)); var bytes = ApplicationIntegrationChecks.Wait(partial.Content.ReadAsByteArrayAsync());
            Check(partial.StatusCode == HttpStatusCode.PartialContent && bytes.Length == 16 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF", "Loopback GET Range 206");
            using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, source); invalidRequest.Headers.Range = new RangeHeaderValue(1000000000, null);
            using var invalid = ApplicationIntegrationChecks.Wait(http.SendAsync(invalidRequest)); Check(invalid.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable, "Range 416");
            var badToken = source.Replace("token=", "token=bad"); using var denied = ApplicationIntegrationChecks.Wait(http.GetAsync(badToken)); Check(denied.StatusCode == HttpStatusCode.Forbidden, "Invalid token rejected");
        }
        using (var stream = new LoopbackRangeStream(new Uri(source))) { var bytes = new byte[4]; stream.ReadExactly(bytes); Check(System.Text.Encoding.ASCII.GetString(bytes) == "RIFF", "Host streaming adapter"); stream.Seek(100, SeekOrigin.Begin); Check(stream.Read(bytes) == 4, "Host ranged seek"); }
        if (nativeAudio)
        {
            using var player = new NativeAudioPlayer(); ApplicationIntegrationChecks.Wait(player.LoadAsync(source)); Check(player.Duration.TotalSeconds >= 7, "Plugin stream decode duration"); player.Volume = 0; player.Play(); player.Position = TimeSpan.FromSeconds(4); Check(Math.Abs(player.Position.TotalSeconds - 4) < .3, "Plugin stream seek"); player.Stop();
        }
        // 关闭再启用后，新进程不能依赖上一个进程的曲目缓存；直接解析保存的歌曲也必须成功。
        vm.Plugins.SetEnabled(manifest, false); vm.Plugins.SetEnabled(manifest, true);
        var restartedSource = ApplicationIntegrationChecks.Wait(vm.Plugins.ResolveAsync(track));
        using (var restartedStream = new LoopbackRangeStream(new Uri(restartedSource))) { var bytes = new byte[4]; restartedStream.ReadExactly(bytes); Check(System.Text.Encoding.ASCII.GetString(bytes) == "RIFF", "New process primes catalog before resolving an existing track"); }
        ApplicationIntegrationChecks.Wait(vm.PlayTrackAsync(track)); Check(vm.CurrentTrack?.ProviderId == manifest.Id, "VM plays plugin source");
        vm.DisablePlugin(manifest); Check(!vm.IsPlaying && !vm.VisibleTracks.Any(t => t.ProviderId == manifest.Id), "Disabling stops provider and hides tracks"); vm.Plugins.Uninstall(manifest);
        if (manifest.LifecycleMethods.Count > 0)
        {
            var retained = Directory.EnumerateDirectories(Path.Combine(vm.Storage.PluginsFolder, "Retained"), manifest.Id + "-*").Single();
            var recorded = File.ReadAllText(Path.Combine(retained, "lifecycle.events.jsonl"));
            Check(recorded.Contains("lifecycle.disable") && recorded.Contains("lifecycle.uninstall"), "Provider callbacks execute before retained uninstall");
            var lifecycleStorage = new AppStorage(Path.Combine(vm.Storage.Root, "lifecycle-fixture"));
            using (var manager = new NonetMusicPlayer.Desktop.Plugins.PluginManager(lifecycleStorage))
            {
                var copy = manager.Install(Path.GetFullPath(package)); manager.Configure(copy, JsonSerializer.Serialize(new Dictionary<string, string> { ["fixtureFolder"] = fixtureFolder }, AppStorage.Json)); manager.SetEnabled(copy, true);
                ApplicationIntegrationChecks.Wait(manager.LoadCatalogAsync(copy, new LyricsService(lifecycleStorage.DefaultLyricsFolder)));
            }
            Check(File.ReadAllText(Path.Combine(lifecycleStorage.PluginsFolder, manifest.Id, "lifecycle.events.jsonl")).Contains("lifecycle.shutdown"), "Shutdown callback executes on host disposal");
            using (var manager = new NonetMusicPlayer.Desktop.Plugins.PluginManager(lifecycleStorage)) { manager.Uninstall(manager.Installed.Single(), deleteFiles: true); }
            Check(!Directory.Exists(Path.Combine(lifecycleStorage.PluginsFolder, manifest.Id)), "Explicit delete-files removes owned plugin directory");
            Console.WriteLine("PASS PLUGIN LIFECYCLE: disable, stateless uninstall, shutdown, retain/delete files");
        }
        Console.WriteLine("PASS PLUGIN: package, secret redaction, JSON-RPC, catalog, HEAD / GET / 206 / 416, token, streaming decode / seek and disable");
    }
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}
