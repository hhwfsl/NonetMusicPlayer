using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>在真实发行宿主中验证未补全分类和最近播放；只使用隔离夹具，不连接私人音源。</summary>
    public static int RunCatalogNavigation(string styleFile, string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root); MainWindow? window = null;
        try
        {
            var storage = new AppStorage(Path.Combine(root, "data")); Environment.SetEnvironmentVariable("NONET_DATA_DIR", storage.Root);
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            using var vm = new MainViewModel(new MusicLibraryScanner(storage), new CatalogVerificationAudio());
            vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
            var playlist = vm.CreatePlaylist("Pending metadata");
            // 空名称同样可来自旧数据或未补全音源，不能依赖界面方案替宿主校验数据。
            vm.AddToPlaylist(playlist, Enumerable.Range(0, 200).Select(i => new TrackItem("pending-" + i, "Pending " + i, i % 2 == 0 ? "" : "  ", "", "fixture-" + i, ".wav", 0)));
            var local = new[] { new TrackItem("history-1", "First", "Artist", "Album", "first", ".wav", 0), new TrackItem("history-2", "Second", "Artist", "Album", "second", ".wav", 0) };
            vm.AddToPlaylist(playlist, local);
            window = new MainWindow { DataContext = vm, Width = 1180, Height = 780 }; window.Show(); Pump();
            using (var input = File.OpenRead(styleFile)) window.ApplyInterfaceLayers("release.fixture", [StyleSheetContract.Read(input)]);
            Pump();
            foreach (var category in new[] { "artists", "albums" })
            {
                vm.Navigate(category); Pump();
                var cards = window.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single();
                if (cards.RealizedCount == 0) throw new InvalidDataException("Pending category cards missing.");
                vm.Navigate("playlists"); Pump(); cards.Refresh();
                if (cards.RealizedCount != 0) throw new InvalidDataException("Disposed category still refreshed.");
                vm.Navigate("playlist:" + playlist.Id); Pump();
            }
            vm.Navigate("history"); Pump();
            Wait(vm.PlayTrackAsync(local[0], local)); Wait(vm.PlayRelativeAsync(1));
            if (vm.Page != "history" || vm.VisibleTracks.Count != 2 || vm.VisibleTracks[0].Id != local[1].Id) throw new InvalidDataException("Recent playback did not refresh.");
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nActualPublishedHost=True\nPendingCategoryMetadata=True\nDisposedGrid=True\nLiveRecentPlayback=True\n");
            return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + AppLog.Redact(error.ToString())); return 1; }
        finally { window?.Close(); AppLog.Flush(); AppLog.Shutdown(); }
        void Pump() { Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Task task) { var end = Environment.TickCount64 + 10000; while (!task.IsCompleted && Environment.TickCount64 < end) { Pump(); Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); Pump(); }
    }
    private sealed class CatalogVerificationAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(120); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() => IsPlaying = false; public void Dispose() { }
    }
}