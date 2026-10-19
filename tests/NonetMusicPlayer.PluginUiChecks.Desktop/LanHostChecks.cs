using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        var catalog = new LanCatalog(id, "Phone fixture", "one", [new("one", "Phone song", "Artist", "Album", 60000, false, false), new("two", "Unretained", "Artist", "Album", 60000, false, false)], [new("list", "Phone list", "Description", ["one", "two"])], 2, "mobile");
        using var cert = LanLibraryServer.Certificate(Path.Combine(root, "phone.pfx"));
        using var phone = new LanLibraryServer(cert, id, "Phone fixture", () => catalog, (_, _) => Task.FromResult<LanFile?>(null), (_, _) => Task.FromResult(true));
        LanLibraryClient? reverse = null;
        phone.AcceptReverse = async (access, token) => { reverse = await LanLibraryClient.FromApprovedAccessAsync(access, token); return true; }; phone.Start();
        var storage = new AppStorage(Path.Combine(root, "data")); using var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump();
        // 设备互听属于“我的音乐”，与统计相邻；折叠该分组及插件导航扩展仍沿用原机制。
        var navigation=window.FindControl<StackPanel>("MyMusicNavigation")!.Children.OfType<Button>().ToArray();
        Check(navigation.Select(b=>b.Tag as string).SequenceEqual(["library","history","statistics","devices"]), "Devices must follow statistics inside My Music navigation.");
        vm.Navigate("devices"); Pump(); Check(!vm.LanSharing, "Opening devices must not start sharing.");
        vm.Navigate("terminal"); Wait(vm.ConnectLanAsync(phone.Invitation("127.0.0.1"))); Pump();
        Check(vm.Page == "terminal" && vm.LanPeers.Count == 1, "Pairing preserves the current page.");
        Check(reverse is { Connected: true } && reverse.Catalog.DeviceKind == "desktop", "One connection establishes reverse access without a second invitation.");
        var status = window.Commands.ExecuteAsync("network status"); Wait(status); Check(status.Result.Success && status.Result.Data?["approvalWaitSeconds"]?.GetValue<int>() == 30, "Terminal shares live network settings and status.");
        var query = window.Commands.ExecuteAsync("nonet network playlists " + id); Wait(query); Check(query.Result.Data?.AsArray().Count == 1, "Terminal can query remote playlists.");
        Check(vm.LanPlaylistNavigation.Single().Source.Contains("Phone fixture"), "Sidebar projection includes device identity.");
        Check(window.FindControl<StackPanel>("PlaylistNavigation")!.Children.OfType<Button>().Any(b => b.Tag as string == "lan:" + id + ":list"), "Remote playlist is in the playlist sidebar, not the devices page.");
        vm.Navigate("lan:" + id + ":list", "Phone list"); Pump(); Check(vm.VisibleTracks.Count == 2, "Device playlists use the normal virtualized song page.");
        var first = vm.VisibleTracks[0]; var second = vm.VisibleTracks[1];
        Check(first.SourceText != second.Title && first.ProviderId == "lan:" + id, "Device track is distinguished from local/cloud sources.");
        Wait(vm.PlayTrackAsync(first, vm.VisibleTracks)); Check(audio.Source.StartsWith("nonet-lan://", StringComparison.Ordinal) && vm.IsPlaying, "Device playback resolves through the shared audio pipeline.");
        vm.AddToPlaylist(vm.Playlists.First(p => p.IsSystem), [first]);
        vm.Navigate("terminal"); var stop = window.Commands.ExecuteAsync("network disconnect " + id); Wait(stop); Pump();
        Check(vm.Page == "terminal" && !vm.IsPlaying && vm.LanPeers.Count == 0, "Disconnect pauses locally and preserves navigation.");
        Check(!vm.LanPlaylistNavigation.Any() && !window.FindControl<StackPanel>("PlaylistNavigation")!.Children.OfType<Button>().Any(b => (b.Tag as string)?.StartsWith("lan:")==true), "Disconnect removes transient sidebar playlists.");
        Check(vm.State.Tracks.Any(t => t.Id == first.Id) && !vm.State.Tracks.Any(t => t.Id == second.Id), "Only explicitly retained device references persist.");
        // 使用真实主窗口的接入确认框，未点击前连接不能完成；拒绝后下一次必须重新审批。
        vm.SetLanSharing(true);
        var invitation = vm.LanInvitations.First();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            window.Activate(); Pump();
            var pairing = LanLibraryClient.ConnectAsync(invitation, Guid.NewGuid().ToString("N"), "Incoming phone");
            var deadline = Environment.TickCount64 + 8000;
            while (!window.OwnedWindows.Any(w => w.Name == "LanPairConfirmation")) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Receiver dialog not shown"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            Check(!pairing.IsCompleted, "Receiver consent must precede access.");
            var dialog = window.OwnedWindows.Single(w => w.Name == "LanPairConfirmation");
            dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == (attempt == 0 ? "RejectLanPair" : "AcceptLanPair")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (attempt == 0) { try { Wait(pairing); throw new InvalidOperationException("Rejected request accepted"); } catch (HttpRequestException) { } }
            else { Wait(pairing); using var connection = pairing.Result; Check(connection.Catalog.DeviceKind == "desktop", "Accepted peer reports desktop source type."); }
        }
        window.Hide();
        var background = LanLibraryClient.ConnectAsync(invitation, Guid.NewGuid().ToString("N"), "Background request");
        var pendingDeadline = Environment.TickCount64 + 5000;
        while (!vm.LanPendingRequests.Any()) { if (Environment.TickCount64 > pendingDeadline) throw new TimeoutException("Background pending request missing"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Check(!background.IsCompleted && !window.IsVisible, "Background pairing neither fails nor steals focus.");
        var pendingId = vm.LanPendingRequests.Single().Id;
        var acceptCommand = window.Commands.ExecuteAsync("network accept " + pendingId); Wait(acceptCommand); Wait(background); background.Result.Dispose(); Pump();
        var expired = window.Commands.ExecuteAsync("network accept " + pendingId); Wait(expired); Check(!expired.Result.Success, "Terminal cannot replay an expired/completed approval.");
        Check(!vm.LanPendingRequests.Any(), "UI and terminal drain the same request.");
        window.Show(); reverse?.Dispose();
        vm.SetLanSharing(false);
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
