using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>验证实际播放顺序、跨页面选歌及可打断浮层，使用独立数据和无声后端。</summary>
internal static class QueueAndMotionChecks
{
    public static void Run(string output)
    {
        var storage = new AppStorage(Path.Combine(output, "queue-data-" + Guid.NewGuid().ToString("N")));
        using var audio = new ApplicationIntegrationChecks.SilentAudioPlayer();
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        var tracks = Enumerable.Range(0, 16).Select(i => new TrackItem("queue-" + i, "夜航 · " + (i + 1), "Nonet 测试艺术家", "星空", "fixture-" + i, ".wav", 100) { DurationSeconds = 263 }).ToArray();
        vm.State.Tracks.AddRange(tracks);
        var playlist = vm.CreatePlaylist("夜航播放列表"); vm.AddToPlaylist(playlist, tracks);
        vm.Navigate("playlist:" + playlist.Id);
        vm.SetPlayMode(PlayMode.Shuffle);
        Wait(vm.PlayTrackAsync(tracks[4]));
        var order = vm.PlaybackQueue.Select(t => t.Id).ToArray();
        Require(order[0] == tracks[4].Id && order.Distinct().Count() == tracks.Length, "Shuffle contains each song once and starts at manual selection");
        for (var i = 1; i < order.Length; i++)
        {
            Wait(vm.PlayRelativeAsync(1, true));
            Require(vm.CurrentTrack?.Id == order[i], "Automatic next follows visible randomized queue");
            Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(order), "Automatic advance does not reshuffle");
        }
        Wait(vm.PlayRelativeAsync(1)); Require(vm.CurrentTrack?.Id == order[0], "Queue wraps in its existing order");
        Wait(vm.PlayRelativeAsync(-1)); Require(vm.CurrentTrack?.Id == order[^1], "Previous walks backwards in shuffle queue");
        vm.Navigate("settings");
        Wait(vm.PlayQueueTrackAsync(tracks[6]));
        Require(vm.Page == "settings" && vm.PlayingSourcePage == "playlist:" + playlist.Id && vm.PlaybackQueue[0] == tracks[6], "Floating selection preserves browsing page and playback source");
        var prior = vm.PlaybackQueue.Select(t => t.Id).ToArray();
        Wait(vm.PlayQueueTrackAsync(tracks[6]));
        Require(vm.PlaybackQueue[0] == tracks[6] && !vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(prior), "Reselecting current song creates a new shuffle");
        var retained = vm.PlaybackQueue.Where(t => t.Id != tracks[9].Id).Select(t => t.Id).ToArray();
        vm.RemovePlaylistTracks(playlist, [tracks[9]]);
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(retained), "Removing a song retains remaining shuffle order");
        vm.SetPlayMode(PlayMode.RepeatOne); var same = vm.CurrentTrack;
        Wait(vm.PlayRelativeAsync(1, true)); Require(vm.CurrentTrack == same, "Repeat one is preserved");
        vm.SetPlayMode(PlayMode.RepeatAll);
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(playlist.TrackIds), "Leaving shuffle restores source order");

        vm.Settings.Theme = "Dark"; vm.ApplySettings();
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump(window, 80);
        window.SetPlaybackQueueOpen(true); Pump(window, 400);
        var popover = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "PlaybackQueuePopover");
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "PlaybackQueueList");
        Require(window.IsPlaybackQueueOpen && popover.IsVisible && list.ItemCount == 15, "Queue floats above current page with actual tracks");
        Require(popover.Bounds.Width <= 420 && popover.Bounds.Bottom <= window.Bounds.Height, "Popover remains inside window");
        using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "playback-queue-dark.png"), PngBitmapEncoderOptions.Default);
        var page = vm.Page;
        var outside = window.FindControl<Button>("MaximizeWindowButton")!.TranslatePoint(new Point(5, 5), window)!.Value;
        // 按下即关闭，外部操作仍能继续接收输入；此处不释放，避免触发最大化。
        window.MouseDown(outside, MouseButton.Left); Pump(window, 20);
        Require(!window.IsPlaybackQueueOpen && vm.Page == page, "Outside click dismisses without navigation");
        window.MouseUp(new Point(1, 1), MouseButton.Left);
        window.SetPlaybackQueueOpen(true); Pump(window, 70);
        var firstOpacity = popover.Opacity;
        window.SetPlaybackQueueOpen(false); Pump(window, 25);
        var secondOpacity = popover.Opacity;
        Require(firstOpacity > 0 && secondOpacity <= firstOpacity + .05, "Interrupted close starts from presentation value");
        window.SetPlaybackQueueOpen(true); Pump(window, 400);
        Require(window.IsPlaybackQueueOpen && Math.Abs(popover.Opacity - 1) < .001, "Close interrupted by reopen finishes visible");
        var button = window.FindControl<Button>("PlayerQueue")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window, 300);
        Require(!window.IsPlaybackQueueOpen && !popover.IsVisible, "Same button closes and releases floating hit area");
        window.SetPlaybackQueueOpen(true); Pump(window, 400); window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Pump(window, 300);
        Require(!window.IsPlaybackQueueOpen, "Escape closes queue");
        vm.Settings.Theme = "Light"; vm.ApplySettings(); window.SetPlaybackQueueOpen(true); Pump(window, 400);
        using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "playback-queue-light.png"), PngBitmapEncoderOptions.Default);
        window.SetPlaybackQueueOpen(false); Pump(window, 300);
        vm.Navigate("lyrics"); Pump(window, 100); vm.Navigate("songs"); Pump(window, 80); vm.Navigate("lyrics"); Pump(window, 500);
        Require(!window.GetVisualDescendants().OfType<Control>().Any(c => c.Name is "NonetMorphOverlay" or "LyricsTransitionBackdrop"), "Rapid lyrics reversal releases transition surfaces");
        window.Width = 760; window.Height = 600; Pump(window, 100); window.SetPlaybackQueueOpen(true); Pump(window, 400);
        Require(popover.Bounds.Top >= 52 && popover.Bounds.Right <= window.Bounds.Width, "Compact queue fits viewport");
        using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "playback-queue-compact.png"), PngBitmapEncoderOptions.Default);
        window.SetPlaybackQueueOpen(false); Pump(window, 300);
        vm.Settings.PlayerTop = true; vm.ApplySettings(); Pump(window, 80);
        window.SetPlaybackQueueOpen(true); Pump(window, 400);
        var player = window.FindControl<Border>("PlayerSurface")!;
        Require(popover.TranslatePoint(default, window)!.Value.Y >= player.TranslatePoint(default, window)!.Value.Y + player.Bounds.Height, "Top player opens its queue below the bar");
        window.SetPlaybackQueueOpen(false); Pump(window, 300);
        vm.Settings.PlayerTop = false; vm.ApplySettings();
        vm.Lyrics.Save(vm.CurrentTrack!.Id, string.Join('\n', Enumerable.Range(0, 40).Select(i => $"[00:{i:00}.00]流畅滚动与逐字歌词测试 {i + 1}")));
        vm.ReloadLyrics(); vm.Seek(1); vm.Navigate("songs"); vm.Navigate("lyrics"); Pump(window, 500);
        var lyricList = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "LyricLines");
        var scroll = lyricList.GetVisualDescendants().OfType<ScrollViewer>().First();
        var origin = scroll.Offset.Y;
        vm.Seek(6.05); Pump(window, 70); var intermediate = scroll.Offset.Y;
        Pump(window, 90); var advanced = scroll.Offset.Y;
        Require(intermediate > origin && advanced > intermediate, "Lyrics move continuously between playback samples using rendered frames");
        Pump(window, 500); Require(lyricList.SelectedIndex == 6, "Smooth following settles on actual active lyric");
        Wait(vm.SetPlayingAsync(false)); var stopped = scroll.Offset.Y; Pump(window, 100);
        Require(Math.Abs(scroll.Offset.Y - stopped) < .5, "Paused lyrics remain stable without idle frame animation");
        using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "lyrics-smooth-follow.png"), PngBitmapEncoderOptions.Default);
        var maximize = window.FindControl<Button>("MaximizeWindowButton")!;
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window, 100);
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window, 100);
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window, 500);
        Require(window.WindowState == WindowState.Maximized, "Window expansion reverses twice and settles maximized");
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window, 500);
        Require(window.WindowState == WindowState.Normal && !window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "NonetMorphOverlay"), "Window contraction restores native state and clears snapshots");
        var from = new Rect(20, 400, 200, 70); var to = new Rect(0, 0, 1000, 700);
        var start = StyleMorphMotion.Sample(from, to, 24, 0, 24, 0); var end = StyleMorphMotion.Sample(from, to, 24, 0, 24, 1);
        Require(start.Bounds == from && start.Radius == 24 && end.Bounds == to && end.Radius == 0, "Morph endpoints have continuous rectangle and corner radius");
        window.Close();
        PlaybackRecoveryChecks.Run(output);
        Console.WriteLine("PASS queue and motion: shuffle cycle, previous, manual reselect, source scope, edits, repeat, outside/button/Escape close, interruption, themes, compact layout and lyrics cleanup.");
    }
    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } task.GetAwaiter().GetResult(); }
    private static void Pump(Window window, int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        do { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); Thread.Sleep(5); } while (Environment.TickCount64 < until);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
