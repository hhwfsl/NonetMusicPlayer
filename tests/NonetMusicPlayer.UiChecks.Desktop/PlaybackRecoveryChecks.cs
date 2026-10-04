using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class PlaybackRecoveryChecks
{
    internal static void Run(string output)
    {
        using (var fixture = new Fixture(output, ["a", "b"]))
        {
            var vm = fixture.Vm; vm.SetPlayMode(PlayMode.RepeatOne);
            Wait(vm.PlayTrackAsync(vm.State.Tracks[0]));
            Require(vm.CurrentTrack?.Id == "c" && vm.IsPlaying, "Load errors skip distinct songs even in repeat-one");
            Require(fixture.Audio.Loads.SequenceEqual(["a", "b", "c"]), "Each unavailable song tried only once");
            Require(fixture.Messages.Count(item => !item.IsWarning) == 2, "Individual skipped tracks notify users");
        }
        using (var fixture = new Fixture(output, ["a", "b", "c"]))
        {
            var playlist = fixture.Vm.CreatePlaylist("One source"); fixture.Vm.AddToPlaylist(playlist, fixture.Vm.State.Tracks.Take(2));
            fixture.Vm.Navigate("playlist:" + playlist.Id); Wait(fixture.Vm.PlayTrackAsync(fixture.Vm.VisibleTracks[0]));
            Require(!fixture.Vm.IsPlaying && fixture.Audio.Loads.SequenceEqual(["a", "b"]), "All-bad queue stops and does not escape its playlist");
            Require(fixture.Messages.Any(item => item.IsWarning && item.Message.Contains("已停止播放")), "All-unavailable queue reports stopped state");
        }
        using (var fixture = new Fixture(output, []))
        {
            Wait(fixture.Vm.PlayTrackAsync(fixture.Vm.State.Tracks[0]));
            fixture.Audio.Fail(); Dispatcher.UIThread.RunJobs();
            Require(fixture.Vm.CurrentTrack?.Id == "b" && fixture.Vm.IsPlaying, "Runtime decoder error advances immediately");
            fixture.Audio.Fail(); Dispatcher.UIThread.RunJobs(); fixture.Audio.Fail(); Dispatcher.UIThread.RunJobs();
            Require(!fixture.Vm.IsPlaying && fixture.Audio.Loads.Count == 3, "Repeated immediate runtime failures stop after each distinct song");
            Wait(fixture.Vm.PlayTrackAsync(fixture.Vm.State.Tracks[0]));
            Require(fixture.Vm.IsPlaying && fixture.Audio.Loads.Count == 4, "Explicit user play starts a fresh recovery attempt");
            fixture.Audio.EmitStaleFailure(fixture.Audio.PlaybackGeneration - 1); Dispatcher.UIThread.RunJobs();
            Require(fixture.Vm.CurrentTrack?.Id == "a" && fixture.Vm.IsPlaying, "Stale decoder generation cannot skip newer playback");
        }
        using (var fixture = new Fixture(output, ["b"]))
        {
            Wait(fixture.Vm.PlayTrackAsync(fixture.Vm.State.Tracks[0]));
            fixture.Audio.Complete(); Dispatcher.UIThread.RunJobs();
            Require(fixture.Vm.CurrentTrack?.Id == "c" && fixture.Vm.IsPlaying, "Natural completion skips an unreadable next track");
        }
        Console.WriteLine("Playback recovery checks passed: load and runtime errors, repeat-one, source scoping, all-bad stop, stale generations.");
    }

    internal static void RunLyrics(MainWindow window, MainViewModel vm)
    {
        var current = vm.State.Tracks.First(); Wait(vm.PlayTrackAsync(current));
        vm.Lyrics.Save(current.Id, string.Join('\n', Enumerable.Range(0, 30).Select(index => $"[00:{index:00}.00]Lyric line {index + 1}")));
        vm.ReloadLyrics(); vm.Navigate("lyrics"); Pump(window);
        var view = window.GetVisualDescendants().OfType<LyricsView>().Single();
        var seek = view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "LyricPreviewSeek");
        var lines = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "LyricLines");
        Require(!seek.IsVisible && !view.GetVisualDescendants().OfType<CheckBox>().Any(), "Lyrics follows playback by default without a follow checkbox");
        var initial = vm.PlaybackPosition;
        var point = lines.TranslatePoint(new Point(lines.Bounds.Width / 2, lines.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(window);
        Require(vm.PlaybackPosition == initial, "Double-tapping lyrics cannot seek");
        window.MouseWheel(point, new Vector(0, -8)); Pump(window);
        VirtualizationAndOutputChecks.Settle(window, 750);
        Require(view.IsPreviewing && seek.IsVisible, "User lyric preview exposes a centered line seek button");
        seek.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(!view.IsPreviewing && !seek.IsVisible && vm.PlaybackPosition > initial, "Preview play seeks and resumes automatic following");
        var scroll = lines.GetVisualDescendants().OfType<ScrollViewer>().First(); var beforeTouch = scroll.Offset.Y; var beforeTouchPosition = vm.PlaybackPosition;
        using (var touch = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true))
        {
            var touchStart = lines.TranslatePoint(new Point(lines.Bounds.Width / 2, lines.Bounds.Height * .7), window)!.Value;
            var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            lines.RaiseEvent(new PointerPressedEventArgs(lines, touch, window, touchStart, 10, pressed, KeyModifiers.None));
            lines.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, lines, touch, window, touchStart - new Vector(0, 80), 20, pressed, KeyModifiers.None));
            lines.RaiseEvent(new PointerReleasedEventArgs(lines, touch, window, touchStart - new Vector(0, 80), 30, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Pump(window);
        }
        Require(view.IsPreviewing && scroll.Offset.Y > beforeTouch && vm.PlaybackPosition == beforeTouchPosition, "Touch lyric scroll previews without seeking");
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(vm.Storage.Root, "lyric-touch-preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        using var desktop = new DesktopLyricsWindow(window, vm);
        desktop.Toggle(); Pump(window); Require(desktop.IsVisible && vm.Settings.DesktopLyricsVisible, "Desktop lyrics opens and persists visibility");
        desktop.Close(); Pump(window); Require(!desktop.IsVisible && !vm.Settings.DesktopLyricsVisible, "Desktop lyrics close hides without destroying the reusable window");
        desktop.Toggle(); Pump(window); Require(desktop.IsVisible, "Desktop lyrics can be reopened after close");
        desktop.HideLyrics(); vm.Navigate("songs"); Pump(window);
        Console.WriteLine("Lyrics checks passed: default following, no double-tap seek, preview seek, reusable floating lyrics.");
    }
    private static void Pump(Window window) { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        public MainViewModel Vm { get; }
        public FixtureAudio Audio { get; }
        public List<UserNotificationEventArgs> Messages { get; } = [];
        public Fixture(string output, IEnumerable<string> failLoads)
        {
            var storage = new AppStorage(Path.Combine(output, "playback-recovery-" + Guid.NewGuid().ToString("N")));
            storage.Save(new AppState { Tracks = [Track("a"), Track("b"), Track("c")], Playlists = [new Playlist { Id = "fixture", Name = "Fixture", TrackIds = ["a", "b", "c"] }] });
            Audio = new(failLoads); Vm = new(new MusicLibraryScanner(storage), Audio); Vm.UserNotification += (_, e) => Messages.Add(e);
            TrackItem Track(string id) => new(id, id, "Artist", "Album", Path.Combine(storage.Root, id + ".wav"), ".wav", 30) { DurationSeconds = 30 };
        }
        public void Dispose() => Vm.Dispose();
    }
    private sealed class FixtureAudio(IEnumerable<string> failLoads) : IAudioPlayer
    {
        private readonly HashSet<string> _failLoads = failLoads.ToHashSet();
        public List<string> Loads { get; } = [];
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(30);
        public float Volume { get; set; }
        public long PlaybackGeneration { get; private set; }
        public event EventHandler? PlaybackStopped;
        public event EventHandler<AudioPlaybackErrorEventArgs>? PlaybackFailed;
        public Task LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            var id = Path.GetFileNameWithoutExtension(path); Loads.Add(id); PlaybackGeneration++; IsPlaying = false; Position = TimeSpan.Zero;
            return _failLoads.Contains(id) ? Task.FromException(new InvalidDataException("Fixture decode failed")) : Task.CompletedTask;
        }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; }
        public void Fail() { IsPlaying = false; PlaybackFailed?.Invoke(this, new("fixture.failed", "Fixture decoder failed", PlaybackGeneration)); }
        public void EmitStaleFailure(long generation) => PlaybackFailed?.Invoke(this, new("fixture.stale", "Fixture stale callback", generation));
        public void Complete() { IsPlaying = false; Position = Duration; PlaybackStopped?.Invoke(this, new AudioPlaybackEndedEventArgs(PlaybackGeneration)); }
        public void Dispose() => Stop();
    }
}
