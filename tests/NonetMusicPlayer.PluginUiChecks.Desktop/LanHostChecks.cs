using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>宿主只读设备页、持久引用与通用命令的集成回归，不使用真实资料库。</summary>
internal static class LanHostChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var id = Guid.NewGuid().ToString("N");
        var catalog = new LanCatalog(id, "Phone fixture", "one", [new("one", "Phone song", "Artist", "Album", 60000, false, false), new("two", "Unretained", "Artist", "Album", 60000, false, false)], [new("list", "Phone list", "Description", ["one", "two"])], 2);
        using var cert = LanLibraryServer.Certificate(Path.Combine(root, "phone.pfx"));
        using var phone = new LanLibraryServer(cert, id, "Phone fixture", () => catalog, (_, _) => Task.FromResult<LanFile?>(null)); phone.Start();
        var storage = new AppStorage(Path.Combine(root, "data")); using var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump();
        vm.Navigate("devices"); Pump(); Check(!vm.LanSharing, "Opening devices must not start sharing.");
        vm.Navigate("terminal"); Wait(vm.ConnectLanAsync(phone.Invitation("127.0.0.1"))); Pump();
        Check(vm.Page == "terminal" && vm.LanPeers.Count == 1, "Pairing preserves the current page.");
        vm.Navigate("lan:" + id + ":list", "Phone list"); Pump(); Check(vm.VisibleTracks.Count == 2, "Device playlists use the normal virtualized song page.");
        var first = vm.VisibleTracks[0]; var second = vm.VisibleTracks[1];
        Check(first.SourceText != second.Title && first.ProviderId == "lan:" + id, "Device track is distinguished from local/cloud sources.");
        Wait(vm.PlayTrackAsync(first, vm.VisibleTracks)); Check(audio.Source.StartsWith("nonet-lan://", StringComparison.Ordinal) && vm.IsPlaying, "Device playback resolves through the shared audio pipeline.");
        vm.AddToPlaylist(vm.Playlists.First(p => p.IsSystem), [first]);
        vm.Navigate("terminal"); vm.DisconnectLan(vm.LanPeers.Single()); Pump();
        Check(vm.Page == "terminal" && !vm.IsPlaying && vm.LanPeers.Count == 0, "Disconnect pauses locally and preserves navigation.");
        Check(vm.State.Tracks.Any(t => t.Id == first.Id) && !vm.State.Tracks.Any(t => t.Id == second.Id), "Only explicitly retained device references persist.");
        vm.Navigate("devices"); Pump(); window.Close(); Pump();
        Console.WriteLine("PASS desktop device page, opt-in sharing, pairing, shared playback, saved reference retention, disconnect and navigation preservation.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump() { for (var i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); } }
    private static void Wait(Task task) { var limit = Environment.TickCount64 + 15000; while (!task.IsCompleted) { if (Environment.TickCount64 > limit) throw new TimeoutException(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } task.GetAwaiter().GetResult(); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public string Source { get; private set; } = "";
        public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(60); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) { Source = source; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() => IsPlaying = false; public void Dispose() { }
    }
}
