using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>用真实页面和高分辨率图片检查缓存回收、滚动连续性及重复导航；不读取用户数据。</summary>
internal static class MemoryNavigationChecks
{
    public static void Run(string output)
    {
        var root = Path.Combine(output, "memory-navigation-" + Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(root); var cover = Path.Combine(storage.ArtworkFolder, "original.png");
        Directory.CreateDirectory(storage.ArtworkFolder);
        using (var image = new WriteableBitmap(new PixelSize(3200, 2400), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
        {
            // 清晰像素用于恢复后渲染验证；不是只检查数字计数器。
            using (var target = image.Lock())
            {
                var row = new int[target.Size.Width]; Array.Fill(row, unchecked((int)0xff6688aa));
                for (var y = 0; y < target.Size.Height; y++) System.Runtime.InteropServices.Marshal.Copy(row, 0, target.Address + y * target.RowBytes, row.Length);
            }
            image.Save(cover, PngBitmapEncoderOptions.Default);
        }
        var source = Path.Combine(root, "tone.wav"); AudioChecks.WriteTone(source, 44100, 2, 3, 440);
        var state = new AppState();
        for (var i = 0; i < 400; i++) state.Tracks.Add(new TrackItem("fixture-" + i, "Track " + i, "Artist", "Album", source, ".wav", new FileInfo(source).Length) { DurationSeconds = 263, CoverPath = cover });
        state.Playlists.Add(new Playlist { Id = Playlist.LikedId, Name = "我喜欢", TrackIds = state.Tracks.Select(t => t.Id).ToList(), CoverPath = cover });
        state.Settings.BackgroundImagePath = AppBackgroundService.Import(storage, cover); state.Settings.OptimizeMemoryWhenMinimized = true; state.Settings.CloseToTray = false;
        storage.Save(state);
        using var audio = new ApplicationIntegrationChecks.SilentAudioPlayer();
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show(); Pump(window);
        try
        {
            using (var statistics = new StatisticsView(vm))
            {
                var refresh = typeof(StatisticsView).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
                refresh.Invoke(statistics, null);
                var controls = statistics.GetVisualDescendants().ToArray();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 50; i++) refresh.Invoke(statistics, null);
                Console.WriteLine($"STATISTICS 50 refreshes allocated: {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576d:0.00} MiB");
                Require(controls.SequenceEqual(statistics.GetVisualDescendants()), "Statistics updates retain the same cards and text controls");
            }
            // 当前页导航不得触发刷新、清空搜索或重新执行定位；别名也必须按实际目标判断。
            var changes = 0; var navigations = 0; vm.ViewChanged += (_, _) => changes++; vm.NavigationRequested += (_, _) => navigations++;
            var home = window.FindControl<ContentControl>("AlternatePage")!.Content;
            for (var i = 0; i < 100; i++) vm.Navigate(i % 2 == 0 ? "library" : "temporary");
            Require(changes == 0 && navigations == 0 && ReferenceEquals(home, window.FindControl<ContentControl>("AlternatePage")!.Content), "Current home/alias navigation preserves the same page and emits no navigation event");
            vm.Navigate("favorites"); Pump(window);
            vm.SearchText = "Track"; var list = window.FindControl<ListBox>("TracksList")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First(); scroll.Offset = new(0, 1800); Pump(window);
            var beforeOffset = scroll.Offset; var beforeChanges = changes; var beforeNavigations = navigations;
            for (var i = 0; i < 100; i++) vm.Navigate(i % 2 == 0 ? "favorites" : "playlist:" + Playlist.LikedId);
            Pump(window);
            Require(vm.SearchText == "Track" && changes == beforeChanges && navigations == beforeNavigations && scroll.Offset == beforeOffset, "Repeated playlist navigation retains filtering and exact scroll offset");
            var original = window.GetVisualDescendants().OfType<OriginalArtworkImage>().Single();
            Until(window, () => OriginalBytes(original) > 20 * 1024 * 1024);
            Wait(window, vm.PlayTrackAsync(vm.State.Tracks[0])); audio.Position = TimeSpan.FromSeconds(37); vm.RefreshPlaybackState();
            scroll.Offset = beforeOffset; Pump(window); beforeOffset = scroll.Offset;
            for (var i = 0; i < 16; i++)
            {
                var unused = Path.Combine(storage.ArtworkFolder, "unused-" + i + ".png"); File.Copy(cover, unused);
                Require(ArtworkCache.Shared.GetImage(unused) is not null, "Populate previously visited thumbnail cache");
            }
            var position = vm.PlaybackPosition; var before = Snapshot(original);
            // 短暂最小化应取消延迟回收，不能反复解码封面。
            var originalSource = original.Source;
            window.WindowState = WindowState.Minimized; Pump(window); window.WindowState = WindowState.Normal; Pump(window);
            var quick = MemoryTask(window); Wait(window, quick);
            Require(ReferenceEquals(original.Source, originalSource), "Fast minimize/restore does not evict visible original artwork");
            window.WindowState = WindowState.Minimized;
            var cleanup = MemoryTask(window); Wait(window, cleanup, render: false);
            var minimized = Snapshot(original);
            Console.WriteLine($"MEMORY diagnostic: state={window.WindowState}, visible={window.IsVisible}, before={before}, minimized={minimized}");
            Require(OriginalBytes(original) == 0 && AppBackgroundService.DecodedBytes == 0 && ArtworkCache.Shared.Snapshot.Count <= 1, "Sustained minimize releases originals, backgrounds and unused thumbnails; a currently drawn thumbnail may reload");
            Require(audio.IsPlaying && vm.IsPlaying && Math.Abs(vm.PlaybackPosition - position) < .1, "Cache cleanup never resets or pauses playback");
            window.WindowState = WindowState.Normal; Pump(window);
            Until(window, () => OriginalBytes(original) > 20 * 1024 * 1024);
            Require(ReferenceEquals(original, window.GetVisualDescendants().OfType<OriginalArtworkImage>().Single()) && scroll.Offset == beforeOffset, "Restore keeps the same controls and scroll position");
            Require(AppBackgroundService.DecodedBytes > 0 && vm.CurrentTrack?.Id == "fixture-0", "Background proxy reloads and playing track is retained");
            var restoredState = Snapshot(original);
            using (var restored = window.CaptureRenderedFrame()!) restored.Save(Path.Combine(root, "restored.png"), PngBitmapEncoderOptions.Default);
            // 关闭设置开关时，长期最小化也不执行这套清理。
            vm.Settings.OptimizeMemoryWhenMinimized = false; window.WindowState = WindowState.Minimized;
            PumpFor(window, TimeSpan.FromMilliseconds(950), render: false);
            Require(OriginalBytes(original) > 20 * 1024 * 1024, "Disabled optimization retains original pixels");
            window.WindowState = WindowState.Normal; Pump(window);
            vm.Navigate("settings"); Pump(window); var settings = window.GetVisualDescendants().OfType<SettingsView>().Single();
            var settingsScroll = settings.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height); settingsScroll.Offset = new(0, 600); Pump(window);
            var settingsOffset = settingsScroll.Offset; vm.Settings.OptimizeMemoryWhenMinimized = true;
            vm.Navigate("settings"); window.WindowState = WindowState.Minimized; Wait(window, MemoryTask(window), render: false); window.WindowState = WindowState.Normal; Pump(window);
            Require(ReferenceEquals(settings, window.GetVisualDescendants().OfType<SettingsView>().Single()) && settingsScroll.Offset == settingsOffset, "Settings input tree and scroll survive minimize and repeat navigation");
            var report = new { Before = before, Minimized = minimized, Restored = restoredState, NoOpHomeNavigations = 100, NoOpPlaylistNavigations = 100 };
            File.WriteAllText(Path.Combine(root, "report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"MEMORY before: {before}; minimized: {minimized}");
            Console.WriteLine("PASS same-route and alias navigation, retained controls/filter/scroll/playback, delayed cache reclamation, restored artwork and disabled setting.");
        }
        finally { window.ExitApplication(); ArtworkCache.Shared.Clear(); AppBackgroundService.Clear(); }
    }
    private static long OriginalBytes(OriginalArtworkImage image) => (long)typeof(OriginalArtworkImage).GetProperty("DecodedBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(image)!;
    private static Task MemoryTask(MainWindow window) => (Task)typeof(MainWindow).GetField("_memoryOptimizationTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object Snapshot(OriginalArtworkImage image)
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        return new { WorkingSetMiB = process.WorkingSet64 / 1048576d, PrivateMiB = process.PrivateMemorySize64 / 1048576d, ManagedMiB = GC.GetTotalMemory(false) / 1048576d,
            OriginalBytes = OriginalBytes(image), BackgroundBytes = AppBackgroundService.DecodedBytes, ThumbnailsBytes = ArtworkCache.Shared.Snapshot.EstimatedBytes };
    }
    private static void Pump(MainWindow window, bool render = true)
    { Dispatcher.UIThread.RunJobs(); if (render) { window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); } }
    private static void PumpFor(MainWindow window, TimeSpan duration, bool render)
    { var clock = Stopwatch.StartNew(); while (clock.Elapsed < duration) { Pump(window, render); Thread.Sleep(5); } }
    private static void Until(MainWindow window, Func<bool> ready)
    { var clock = Stopwatch.StartNew(); while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(12)) { Pump(window); Thread.Sleep(5); } Require(ready(), "Asynchronous UI update timed out"); }
    private static void Wait(MainWindow window, Task task, bool render = true)
    { var clock = Stopwatch.StartNew(); while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(12)) { Pump(window, render); Thread.Sleep(5); } Require(task.IsCompleted, "Operation timed out"); task.GetAwaiter().GetResult(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
