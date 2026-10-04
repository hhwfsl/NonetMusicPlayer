using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class VirtualizationAndOutputChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        vm.Navigate("songs"); Pump(owner);
        var progress = owner.FindControl<PlayerProgressSlider>("PlaybackSlider")!;
        vm.Seek(0); Pump(owner); Require(Math.Abs(progress.ThumbCenter.X - progress.ThumbRadius) < .01, "Zero thumb left edge stays at rail left edge");
        vm.Seek(vm.PlaybackDuration); Pump(owner); Require(Math.Abs(progress.ThumbCenter.X + progress.ThumbRadius - progress.Bounds.Width) < .01, "Full thumb right edge stays at rail right edge"); vm.Seek(0);
        var surface = owner.FindControl<Border>("PlayerSurface")!; Require(surface.BorderThickness.Top == 0, "No extra divider above progress rail");
        var desktop = owner.FindControl<Button>("PlayerDesktopLyrics")!; var volume = owner.FindControl<Button>("PlayerVolume")!;
        Require(Math.Abs(desktop.TranslatePoint(new Point(0, desktop.Bounds.Height / 2), owner)!.Value.Y - volume.TranslatePoint(new Point(0, volume.Bounds.Height / 2), owner)!.Value.Y) < .5, "Desktop lyrics aligns with other player buttons");
        var watch = Stopwatch.StartNew(); vm.Navigate("settings"); Pump(owner); watch.Stop(); var firstSettings = watch.ElapsedMilliseconds;
        var settings = owner.FindControl<ContentControl>("AlternatePage")!.Content;
        vm.Navigate("songs"); Pump(owner); watch.Restart(); vm.Navigate("settings"); Pump(owner); watch.Stop();
        Require(ReferenceEquals(settings, owner.FindControl<ContentControl>("AlternatePage")!.Content), "Settings page is reused, not rebuilt on each navigation");
        Require(watch.ElapsedMilliseconds < 1000, "Cached settings navigation is bounded");
        Console.WriteLine($"PASS SETTINGS CACHE: first {firstSettings} ms, repeat {watch.ElapsedMilliseconds} ms");
        var originalFont = vm.Settings.FontFamily; vm.Settings.FontFamily = "Microsoft YaHei UI"; vm.ApplySettings(); Require(owner.FontFamily.Name.Contains("Microsoft YaHei UI"), "Font preference applies immediately"); vm.Settings.FontFamily = originalFont; vm.ApplySettings();
        Lyrics(owner, vm, output);
        Crop(owner, vm, output);
        OutputAndTemporary(output);
        Instance(output);
        vm.Navigate("songs"); Pump(owner);
    }
    private static void Lyrics(MainWindow owner, MainViewModel vm, string output)
    {
        var originalPosition = vm.PlaybackPosition;
        vm.LyricLines.Clear(); for (var i = 0; i < 30; i++) vm.LyricLines.Add(new LyricLine(i * 4, "歌词行 " + i, "Translation " + i));
        vm.Seek(40); vm.Navigate("lyrics"); Pump(owner);
        var page = (LyricsView)owner.FindControl<ContentControl>("FullLyricsHost")!.Content!;
        var list = page.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "LyricLines"); var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        Require(scroll.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, "Lyric scrollbars are hidden without disabling scrolling");
        Require(list.SelectedIndex == 10, "Lyrics reopen at current timestamp");
        Require(Math.Abs(scroll.Offset.Y - 10 * 112) < 2, "Current lyric is immediately centered after first layout");
        var activeRow = list.GetRealizedContainers().Single(row => row.DataContext is LyricLine line && line.Seconds == 40);
        var activeY = activeRow.TranslatePoint(new Point(0, activeRow.Bounds.Height / 2), scroll)!.Value.Y;
        Require(Math.Abs(activeY - scroll.Viewport.Height / 2) < 2, "Active lyric is visually centered, not only at a calculated offset");
        var point = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), owner)!.Value;
        owner.MouseMove(point); owner.MouseWheel(point, new Vector(0, -1)); Settle(owner, 720);
        Require(page.IsPreviewing && Math.Abs(scroll.Offset.Y - 11 * 112) < 2, "One wheel step centers exactly one next lyric");
        owner.MouseDown(point, MouseButton.Left); owner.MouseMove(point + new Vector(0, -75), RawInputModifiers.LeftMouseButton); owner.MouseUp(point + new Vector(0, -75), MouseButton.Left); Settle(owner, 420);
        Require(Math.Abs(scroll.Offset.Y / 112 - Math.Round(scroll.Offset.Y / 112)) < .02, "Mouse drag settles on a line, never between lyrics");
        vm.Navigate("songs"); Pump(owner); vm.Navigate("lyrics"); Pump(owner);
        var reopened = (LyricsView)owner.FindControl<ContentControl>("FullLyricsHost")!.Content!;
        var again = reopened.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "LyricLines").GetVisualDescendants().OfType<ScrollViewer>().Single();
        Require(!reopened.IsPreviewing && Math.Abs(again.Offset.Y - 10 * 112) < 2, "Exit/reenter resets preview and correctly restores active line");
        var reopenedList = reopened.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "LyricLines");
        var reopenedRow = reopenedList.GetRealizedContainers().Single(row => row.DataContext is LyricLine line && line.Seconds == 40);
        Require(Math.Abs(reopenedRow.TranslatePoint(new Point(0, reopenedRow.Bounds.Height / 2), again)!.Value.Y - again.Viewport.Height / 2) < 2, "Reentered lyric is visually centered");
        using var frame = owner.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, "beta7-lyrics.png"), PngBitmapEncoderOptions.Default);
        vm.Navigate("songs"); vm.Seek(originalPosition); vm.ReloadLyrics();
        Console.WriteLine("PASS LYRICS SNAP: hidden scrollbars, first-layout/reentry alignment, per-line wheel and mouse drag snapping");
    }
    private static void Crop(MainWindow owner, MainViewModel vm, string output)
    {
        var source = Path.Combine(output, "background-fixture.png");
        using var image = CoverCropService.Load(source);
        var crop = new CoverCropControl(image) { Width = 360, Height = 300 };
        var window = new Window { Width = 400, Height = 350, Content = crop }; window.Show(); Pump(window);
        crop.Zoom(2); var before = crop.Crop;
        window.MouseDown(new Point(180, 150), MouseButton.Left); window.MouseMove(new Point(210, 170), RawInputModifiers.LeftMouseButton); window.MouseUp(new Point(210, 170), MouseButton.Left); Pump(window);
        Require(crop.Crop.X < before.X && crop.Crop.Y < before.Y, "Cover crop changes through actual pointer dragging");
        crop.SetCrop(0, 0, Math.Min(image.Original.Width, image.Original.Height) / 2d);
        var result = CoverCropService.Export(vm.Storage, image, crop.Crop); using var bitmap = new Bitmap(result);
        Require(bitmap.PixelSize == new PixelSize(320, 320) && crop.Crop.X == 0, "Precise crop exports a bounded square cover");
        using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, "beta7-crop.png"), PngBitmapEncoderOptions.Default); window.Close();
        var oversized = Path.Combine(output, "oversize-cover.png"); using (var file = File.Create(oversized)) file.SetLength(CoverCropService.MaximumFileBytes + 1);
        var rejected = false; try { using var invalid = CoverCropService.Load(oversized); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Oversized image is rejected before decoding");
        Console.WriteLine("PASS CROP: draggable picture, precise coordinates, 320px output and 20MiB pre-decode limit");
    }
    private static void OutputAndTemporary(string output)
    {
        var folder = Path.Combine(output, "beta7-output-" + Guid.NewGuid().ToString("N")); var storage = new AppStorage(folder);
        var wav = Path.Combine(folder, "temporary.wav");
        using (var writer = new BinaryWriter(File.Create(wav))) { const int count = 16000; writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2); for (var i = 0; i < count; i++) writer.Write((short)0); }
        var audio = new OutputAudio(); var clock = new ManualClock(); using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio, clock);
        Wait(vm.PlayTemporaryFilesAsync([wav])); audio.Position = TimeSpan.FromSeconds(42); var loads = audio.Loads;
        vm.Settings.DeviceName = "Other"; vm.ApplySettings(); Until(() => audio.DeviceName == "Other"); Dispatcher.UIThread.RunJobs();
        Require(!vm.IsPlaying && Math.Abs(vm.PlaybackPosition - 42) < .01 && audio.Loads == loads, "Manual output switch pauses without dropping source or resetting progress");
        vm.TogglePlayPauseCommand.Execute(null); Require(vm.IsPlaying && audio.Position.TotalSeconds == 42, "Continue resumes retained source after device switch");
        audio.Disconnect(); Dispatcher.UIThread.RunJobs(); Require(!vm.IsPlaying && vm.Settings.DeviceName == "系统默认" && vm.PlaybackPosition == 42, "Hotplug fallback pauses and preserves position");
        vm.TogglePlayPauseCommand.Execute(null); Sample(vm, audio, clock, 1);
        Require(vm.State.Tracks.Count == 0 && vm.State.History.Count == 1 && vm.GetStatistics().TotalSeconds == 0 && vm.GetStatistics().Artists.Count == 0, "Temporary audio is recorded in history but excluded from library and listening statistics");
        vm.AddToPlaylist(vm.CreatePlaylist("Persist"), [vm.CurrentTrack!]); Sample(vm, audio, clock, 1);
        Require(vm.State.Tracks.Count == 1 && vm.GetStatistics().TotalSeconds >= .99 && vm.State.History.Count == 1, "Explicit playlist insertion starts normal statistics from that point");
        vm.Save(); Require(storage.Load().LastTemporaryCounted, "Added temporary audio persists its normal statistics eligibility");
        var restoredAudio = new OutputAudio(); var restoredClock = new ManualClock();
        using (var restored = new MainViewModel(new MusicLibraryScanner(storage), restoredAudio, restoredClock))
        {
            restored.TogglePlayPauseCommand.Execute(null); Until(() => restored.IsPlaying);
            var seconds = restored.GetStatistics().TotalSeconds; Sample(restored, restoredAudio, restoredClock, 1);
            Require(restored.GetStatistics().TotalSeconds >= seconds + .99, "Added temporary audio remains countable after restart");
        }
        vm.State.History = Enumerable.Range(0, 1200).Select(i => "history-" + i).ToList(); vm.Settings.HistoryLimit = 1000; vm.ApplySettings(); Require(vm.State.History.Count == 1000 && vm.State.History[999] == "history-999", "Default history trims oldest entries at 1000");
        vm.Settings.HistoryLimit = 12; vm.ApplySettings(); Require(vm.State.History.Count == 12, "Custom history maximum applies immediately");
        Console.WriteLine("PASS OUTPUT/TEMPORARY: retained decoder/position, hotplug pause/fallback, resume, temporary exclusions and history limits");
    }
    private static void Instance(string output)
    {
        var id = "NonetMusicPlayer-test-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService(id); using var second = new SingleInstanceService(id); string[]? received = null;
        Require(first.IsPrimary && !second.IsPrimary, "Only one instance owns a user-scoped identity"); first.Receive(files => received = files);
        var file = Path.Combine(Path.GetFullPath(output), "file with spaces.wav"); Require(second.ForwardAsync([file]).GetAwaiter().GetResult(), "Second instance forwards and receives acknowledgement");
        Require(received is [var forwarded] && forwarded == file, "File activation reaches the first instance intact");
        Console.WriteLine("PASS SINGLE INSTANCE: exclusive ownership and acknowledged user-only local file activation");
    }
    private static void Sample(MainViewModel vm, OutputAudio audio, ManualClock clock, double seconds) { clock.Advance(seconds); audio.Position += TimeSpan.FromSeconds(seconds); typeof(MainViewModel).GetMethod("Tick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, [null, EventArgs.Empty]); }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> ready) { var watch = Stopwatch.StartNew(); while (!ready()) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); if (watch.ElapsedMilliseconds > 5000) throw new TimeoutException(); } Dispatcher.UIThread.RunJobs(); }
    internal static void Settle(Window window, int ms)
    {
        var frame = new DispatcherFrame();
        _ = Task.Delay(ms).ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
        Dispatcher.UIThread.PushFrame(frame); Pump(window);
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ManualClock : TimeProvider { private long _ticks; public override long TimestampFrequency => 1000; public override long GetTimestamp() => _ticks; public void Advance(double seconds) => _ticks += (long)(seconds * 1000); }
    private sealed class OutputAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(300); public float Volume { get; set; }
        public int Loads; public long PlaybackGeneration { get; private set; } public string DeviceName { get; set; } = "系统默认";
        public IReadOnlyList<string> Devices => ["系统默认", "Other"];
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public event EventHandler<AudioOutputChangedEventArgs>? OutputDeviceChanged;
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) { Loads++; Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() { PlaybackGeneration++; IsPlaying = true; } public void Pause() { PlaybackGeneration++; IsPlaying = false; }
        public void Stop() { Pause(); Position = TimeSpan.Zero; } public void Dispose() => Stop();
        public void Disconnect() { Pause(); DeviceName = "系统默认"; OutputDeviceChanged?.Invoke(this, new(DeviceName, Position.TotalSeconds, true, PlaybackGeneration)); }
    }
}
