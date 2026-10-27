using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>固定合成资料库与歌词负载，记录同线程分配和耗时；性能数据仅报告，不作为易波动的断言。</summary>
internal static class OptimizationChecks
{
    public static void Run(string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root, "data"))), new SilentAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        vm.State.Tracks = Enumerable.Range(0, 20000).Select(i => new TrackItem("track-" + i, "Song " + i, "Artist " + i % 80, "Album " + i % 200, "", ".flac", 0)).ToList();
        var playlist = new Playlist { Id = "benchmark", Name = "Benchmark", TrackIds = vm.State.Tracks.Select(t => t.Id).ToList() };
        vm.State.Playlists.Add(playlist); vm.Page = "playlist:benchmark"; vm.ApplyFilter();
        var metrics = new Dictionary<string, object>();
        metrics["playlistFilters"] = Measure(20, vm.ApplyFilter);
        Check(vm.VisibleTracks.Count == 20000 && ReferenceEquals(vm.VisibleTracks[123], vm.State.Tracks[123]), "Filter preserves playlist order and identities.");
        vm.SearchText = "Album 17"; Check(vm.VisibleTracks.All(t => t.Album.Contains("Album 17")), "Search still matches album metadata."); vm.SearchText = "";
        var replacement = new TrackItem("track-123", "Replacement", "Artist", "Album", "", ".flac", 0);
        vm.State.Tracks[123] = replacement; vm.ApplyFilter(); Check(ReferenceEquals(vm.VisibleTracks[123], replacement), "Same-count replacement invalidates the ID index.");
        replacement.Id = "renamed"; playlist.TrackIds[123] = replacement.Id; vm.ApplyFilter(); Check(ReferenceEquals(vm.VisibleTracks[123], replacement), "In-place ID change keeps playlist references current.");
        replacement.ProviderId = "disabled.fixture"; vm.ApplyFilter(); Check(vm.VisibleTracks.Count == 19999 && vm.VisibleTracks[123].DisplayIndex == 125, "Unavailable sources retain original playlist ordinals.");
        replacement.ProviderId = null; vm.ApplyFilter();
        vm.Page = "album:Album 17"; vm.ApplyFilter(); Check(vm.VisibleTracks.Count > 0 && vm.VisibleTracks.All(t => t.Album == "Album 17"), "Album classification preserves exact matching.");
        vm.Page = "artist:Artist 17"; vm.ApplyFilter(); Check(vm.VisibleTracks.Count > 0 && vm.VisibleTracks.All(t => t.Artist == "Artist 17"), "Artist classification preserves exact matching.");
        vm.Page = "playlist:benchmark"; vm.ApplyFilter();
        var control = new KaraokeLine { TextSize = 23 };
        var text = string.Concat(Enumerable.Repeat("あいうえお春夏秋冬🙂e\u0301", 30));
        control.UpdateTimed(text, [], 0, true, text.Length - 5.5); control.Measure(new(900, double.PositiveInfinity)); control.Arrange(new Rect(0, 0, 900, 300));
        metrics["karaokeClips"] = Measure(600, () => { control.UpdateTimed(text, [], 0, true, text.Length - 5.5); _ = control.ActiveClip.Bounds; });
        control.UpdateTimed("A🙂e\u0301B", [], 0, true, 0); Check(control.ActiveClip.Bounds.Width == 0, "Zero karaoke progress has no highlighted area.");
        control.UpdateTimed("A🙂e\u0301B", [], 0, true, 6); Check(control.ActiveClip.Bounds.Width > 0, "Unicode glyphs remain highlightable.");
        control.UpdateTimed("AB", [], 0, true, .5); var half = control.ActiveClip.Bounds.Width;
        control.UpdateTimed("AB", [], 0, true, 1); Check(Math.Abs(control.ActiveClip.Bounds.Width - half * 2) < .1, "Partial glyph highlight still moves horizontally.");
        for (var i = 0; i < 400; i++) vm.LyricLines.Add(new(i, "歌詞 " + i, "Translation " + i) { Words = [new(i, "歌", i + .3), new(i + .3, "詞 " + i, i + .9)] });
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760 }; window.Show(); Dispatcher.UIThread.RunJobs();
        vm.PlaybackPosition = 120.5; vm.Navigate("lyrics"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var lyrics = window.GetVisualDescendants().OfType<LyricsView>().Single();
        var refresh = typeof(LyricsView).GetMethod("RefreshWordColors", BindingFlags.NonPublic | BindingFlags.Instance)!.CreateDelegate<Action>(lyrics);
        metrics["lyricRefreshes"] = Measure(600, refresh);
        var database = new LibraryDatabase(Path.Combine(root, "benchmark.db"));
        for (var i = 0; i < 100; i++) vm.State.ListeningEntries.Add(new() { Date = "2026-10-26", TrackId = "entry-" + i, Seconds = i });
        database.Save(vm.State);
        metrics["playbackSaves"] = Measure(20, () => { vm.State.LastPosition += .1; database.Save(vm.State, true); });
        Check(database.LastWrittenRows == 1, "Playback-only save changes only its state row.");
        vm.State.ListeningEntries.RemoveAt(0); database.Save(vm.State, true);
        var loaded = new LibraryDatabase(database.Path).Read(); Check(loaded.Tracks.Count == 20000 && loaded.ListeningEntries.Count == 99, "Playback saves preserve tracks and remove deleted statistics.");
        vm.State.Tracks[0].LyricsDisabled = true; database.Save(vm.State);
        Check(new LibraryDatabase(database.Path).Read().Tracks[0].LyricsDisabled, "Incremental fingerprints preserve the existing lyric-association state.");
        window.Close();
        File.WriteAllText(Path.Combine(root, "metrics.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(metrics)); Console.WriteLine("PASS fixed-load benchmarks and behavior checks.");
    }
    private static object Measure(int count, Action action)
    {
        action(); var bytes = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++) action(); watch.Stop();
        return new { Iterations = count, Milliseconds = watch.Elapsed.TotalMilliseconds, AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytes };
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class SilentAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
