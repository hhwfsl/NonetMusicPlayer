using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>模拟云端引用尚未补全的阶段，验证分类导航和最近播放的实时投影。</summary>
internal static class CatalogNavigationChecks
{
    public static void Run(string output, string? styleFile)
    {
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Verify(output, null);
        if (styleFile is not null) Verify(output, styleFile);
    }
    private static void Verify(string output, string? styleFile)
    {
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root, "data"))), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        // 只登记通用音源身份，使测试引用可见；不启动私人音源或发出网络请求。
        vm.Plugins.Installed.Add(new() { Id = "fixture.pending-cloud", Name = "Pending cloud source", Version = "1.0.0", Type = "provider", Enabled = true });
        var playlist = vm.CreatePlaylist("Pending cloud playlist");
        var pending = Enumerable.Range(0, 400).Select(i => new TrackItem("pending-" + i, "Pending " + i, i % 2 == 0 ? "" : "  ", i % 2 == 0 ? " " : "", "", ".audio", 0) { ProviderId = "fixture.pending-cloud", ProviderTrackId = i.ToString() }).ToArray();
        vm.AddToPlaylist(playlist, pending);
        var local = Enumerable.Range(0, 150).Select(i => new TrackItem("local-" + i, i == 149 ? "special marker" : "Song " + i, "Fixture artist", "Fixture album", "fixture-" + i, ".wav", 0) { DurationSeconds = 120 }).ToArray();
        vm.AddToPlaylist(vm.CreatePlaylist("Local fixture"), local);
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 780 }; window.Show(); Pump(window);
        if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("fixture.presentation", [StyleSheetContract.Read(input)]); Pump(window); }
        foreach (var category in new[] { "artists", "albums" })
        {
            vm.Navigate(category); Pump(window);
            var grid = window.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single();
            Require(grid.RealizedCount > 0 && grid.RealizedCount < 100, "Virtual category cards render pending metadata");
            var unknown = L10n.T(category == "artists" ? "Library.UnknownArtist" : "Library.UnknownAlbum");
            var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "OpenMusicGroup" && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == unknown));
            button.RaiseEvent(new(Button.ClickEvent)); Pump(window);
            Require(vm.VisibleTracks.Count == pending.Length, "Unknown category opens its pending tracks");
            vm.Navigate("playlists"); Pump(window); grid.Refresh();
            Require(grid.RealizedCount == 0, "Disposed category grid cannot realize stale cards");
            window.Width = 920; window.Height = 660; Pump(window); window.Width = 1180; window.Height = 780; Pump(window);
            vm.Navigate("playlist:" + playlist.Id); Pump(window);
            Require(vm.VisibleTracks.Count == pending.Length, "Playlist remains usable after category navigation and resizing");
        }
        Require(vm.ResolveGroupCover("artist", "", pending) is null && vm.ResolveGroupCover("album", "  ", pending) is null, "Read-only cover lookup tolerates pending names");
        vm.State.History.AddRange(local.Take(120).Select(t => t.Id)); vm.Navigate("history"); Pump(window);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(b => b.Name == "TracksList");
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First(); scroll.Offset = new(0, 600); Pump(window); var offset = scroll.Offset;
        var resets = 0; vm.VisibleTracks.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        Wait(vm.PlayTrackAsync(local[149], local), window);
        Require(vm.Page == "history" && vm.VisibleTracks[0] == local[149] && vm.VisibleTracks.Count == 121, "New playback appears immediately without navigation");
        Wait(vm.PlayTrackAsync(local[30], local), window);
        Require(vm.VisibleTracks[0] == local[30] && vm.VisibleTracks.Count == 121 && resets == 0, "Replay moves one existing entry without a collection reset");
        Require(scroll.Offset == offset, "Playback does not move the history viewport");
        vm.Settings.HistoryLimit = 3; Wait(vm.PlayTrackAsync(local[148], local), window);
        Require(vm.VisibleTracks.Count == 3 && vm.VisibleTracks[0] == local[148], "History limit is immediately reflected");
        Wait(vm.PlayRelativeAsync(1), window); Require(vm.VisibleTracks[0] == local[149], "Next action updates history immediately");
        vm.SearchText = "special marker"; resets = 0; Wait(vm.PlayTrackAsync(local[147], local), window);
        Require(vm.VisibleTracks.Count == 1 && vm.VisibleTracks.All(t => t.Title.Contains("special marker")) && resets == 0, "Live history respects current search");
        if (styleFile is not null) window.ApplyInterfaceLayers("fixture.presentation", []);
        window.Close(); Pump(window);
        Console.WriteLine("PASS " + (styleFile is null ? "default" : "interface preset") + ": pending cloud category covers/navigation, disposed grid, live history ordering/limit/filter/scroll, no resets.");
    }
    private static void Wait(Task task, MainWindow window) { var end = Environment.TickCount64 + 10000; while (!task.IsCompleted && Environment.TickCount64 < end) { Pump(window); Thread.Sleep(5); } task.GetAwaiter().GetResult(); Pump(window); }
    private static void Pump(MainWindow window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(120); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() => IsPlaying = false; public void Dispose() { }
    }
}