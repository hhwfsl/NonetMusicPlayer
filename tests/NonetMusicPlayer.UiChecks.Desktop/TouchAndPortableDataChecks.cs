using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class TouchAndPortableDataChecks
{
    public static void Run(MainWindow original, MainViewModel originalVm, string output)
    {
        var storage = new AppStorage(Path.Combine(output, "beta8-data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudio());
        // Multiple main windows exist only in the test harness. Use the same
        // application-wide theme so the original window cannot undo its change.
        vm.Settings.Theme = originalVm.Settings.Theme;
        var playlist = vm.CreatePlaylist("定位测试");
        for (var i = 0; i < 120; i++)
        {
            var song = new TrackItem("beta8-" + i, "曲目 " + i, "艺术家", "专辑", "unused.wav", ".wav", 300);
            vm.State.Tracks.Add(song); playlist.TrackIds.Add(song.Id);
        }
        vm.Navigate("playlist:" + playlist.Id);
        var window = new MainWindow { DataContext = vm, Width = 1360, Height = 860 }; window.Show(); Pump(window);
        try
        {
            Navigation(window, vm, playlist);
            GripperAndTouch(window, vm);
            FontsAndAppearance(window, vm, output);
            LyricsFormats();
            DesktopLyrics(window, vm, output);
            Temporary(vm, storage, originalVm.State.Tracks.First(t => File.Exists(t.FilePath)).FilePath);
            foreach (var work in new[] { new Size(1024, 728), new Size(1280, 680), new Size(768, 984), new Size(3440, 1400), new Size(512, 643) })
            {
                var size = WindowPlacementService.InitialSize(work);
                Require(size.Width <= work.Width && size.Height <= work.Height && size.Width <= 1360 && size.Height <= 860, "Initial size fits landscape/portrait/ultrawide logical work areas");
            }
            foreach (var size in new[] { new Size(640, 480), new Size(720, 1000), new Size(980, 620), new Size(480, 600) })
            {
                window.MinWidth = 320; window.MinHeight = 320; window.Width = size.Width; window.Height = size.Height;
                vm.Navigate("playlist:" + playlist.Id); Pump(window); VirtualizationAndOutputChecks.Settle(window, 160);
                var player = window.FindControl<Border>("PlayerSurface")!;
                foreach (var name in new[] { "PlayerArtwork", "PlayerPlay", "PlayerVolume", "PlayerDesktopLyrics", "PlayerMode", "PlayerMore", "PlayerFavorite", "PlayerTime" })
                {
                    var control = window.FindControl<Control>(name)!; var p = control.TranslatePoint(default, player)!.Value;
                    Require(p.X >= 0 && p.Y >= 0 && p.X + control.Bounds.Width <= player.Bounds.Width + 1 && p.Y + control.Bounds.Height <= player.Bounds.Height + 1, name + " fits " + size);
                }
                using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, $"beta8-window-{size.Width:0}x{size.Height:0}.png"), PngBitmapEncoderOptions.Default);
            }
            Console.WriteLine("PASS BETA8: title touch/pen movement, isolated resize grip, source-scoped song location, external layout management, imported fonts, region opacity, desktop lyrics hover/drag, timestamp variants and history-only audio");
        }
        finally { window.Close(); L10n.SetLanguage(originalVm.Settings.Language); ThemeService.Apply(originalVm.Settings); originalVm.ApplySettings(); original.ShowPage(); Pump(original); }
    }
    private static void Navigation(MainWindow window, MainViewModel vm, Playlist playlist)
    {
        Wait(vm.PlayTrackAsync(vm.State.Tracks[100])); VirtualizationAndOutputChecks.Settle(window, 220);
        var tracks = window.FindControl<TrackListBox>("TracksList")!;
        ScrollViewer Scroll() => tracks.GetVisualDescendants().OfType<ScrollViewer>().First();
        Require(Scroll().Offset.Y > 1000, "Playing playlist locates active song");
        vm.Navigate("songs"); VirtualizationAndOutputChecks.Settle(window, 220); Require(Scroll().Offset.Y < 1, "Library starts at top even with a playing library song");
        Scroll().Offset = new Vector(0, 1000); vm.Navigate("history"); VirtualizationAndOutputChecks.Settle(window, 220); Require(Scroll().Offset.Y < 1, "History starts at top");
        var other = vm.CreatePlaylist("另一歌单"); other.TrackIds.AddRange(playlist.TrackIds);
        vm.Navigate("playlist:" + other.Id); VirtualizationAndOutputChecks.Settle(window, 220); Require(Scroll().Offset.Y < 1, "Same song in another playlist does not auto-locate");
        vm.Navigate("playlist:" + playlist.Id); VirtualizationAndOutputChecks.Settle(window, 220); Require(Scroll().Offset.Y > 1000, "Returning to the source playlist locates the playing song");
        vm.Navigate("songs"); Wait(vm.PlayTrackAsync(vm.State.Tracks[100])); VirtualizationAndOutputChecks.Settle(window, 220); Require(Scroll().Offset.Y < 1, "Track changes do not auto-locate library songs");
        Scroll().Offset = new Vector(0, 1000); vm.Navigate("songs"); VirtualizationAndOutputChecks.Settle(window, 220); Require(Scroll().Offset.Y < 1, "Reopening the same library page resets to top");
    }
    private static void GripperAndTouch(MainWindow window, MainViewModel vm)
    {
        vm.Navigate("songs"); Pump(window);
        var grip = window.FindControl<Border>("PlayerResizeGrip")!; var progress = window.FindControl<PlayerProgressSlider>("PlaybackSlider")!;
        var g = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
        var p = progress.TranslatePoint(new Point(progress.Bounds.Width * .6, progress.Bounds.Height / 2), window)!.Value;
        var play = window.FindControl<Button>("PlayerPlay")!; var b = play.TranslatePoint(default, window)!.Value;
        Require(grip.Bounds.Width < 120 && g.Y > p.Y && g.Y < b.Y, "Moderate grip is beneath the progress rail and above playback buttons");
        var gripTop = grip.TranslatePoint(default, window)!.Value.Y;
        var railBottom = progress.TranslatePoint(new Point(0, progress.Bounds.Height), window)!.Value.Y;
        Require(gripTop >= railBottom - .1 && gripTop + grip.Bounds.Height <= b.Y + .1, "Seek, resize and transport hit rectangles never overlap");
        var height = vm.Settings.PlayerHeight;
        window.MouseDown(p, MouseButton.Left); window.MouseMove(p + new Vector(0, -30), RawInputModifiers.LeftMouseButton); window.MouseUp(p + new Vector(0, -30), MouseButton.Left); Pump(window);
        Require(vm.Settings.PlayerHeight == height, "Seeking cannot resize player bar");
        window.MouseDown(g, MouseButton.Left); window.MouseMove(g - new Vector(0, 20), RawInputModifiers.LeftMouseButton); window.MouseUp(g - new Vector(0, 20), MouseButton.Left); Pump(window);
        Require(vm.Settings.PlayerHeight >= height + 18, "Only grip drag changes player height");
        vm.Settings.PlayerHeight = 96; vm.ApplySettings(); Pump(window);
        foreach (var type in new[] { PointerType.Touch, PointerType.Pen })
        {
            using var pointer = new Pointer(Pointer.GetNextFreeId(), type, true);
            var title = window.FindControl<Border>("TitleBarSurface")!; var start = window.Position;
            var pressed = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed);
            title.RaiseEvent(new PointerPressedEventArgs(title, pointer, window, new Point(500, 24), 10, pressed, KeyModifiers.None));
            window.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, window, pointer, window, new Point(540, 44), 20, pressed, KeyModifiers.None));
            var moved = window.Position;
            window.RaiseEvent(new PointerReleasedEventArgs(window, pointer, window, new Point(500, 24), 30, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Require(moved.X > start.X && moved.Y > start.Y && pointer.Captured is null, type + " title movement works without mouse-left state and releases capture");
            Pump(window);
            var touchGripTop = grip.TranslatePoint(default, window)!.Value.Y;
            var touchRailBottom = progress.TranslatePoint(new Point(0, progress.Bounds.Height), window)!.Value.Y;
            var touchPlayTop = play.TranslatePoint(default, window)!.Value.Y;
            Require(touchGripTop >= touchRailBottom - .1 && touchGripTop + grip.Bounds.Height <= touchPlayTop + .1, "Touch seek, resize and transport hit areas are separate");
        }
        vm.Settings.TouchMode = false; vm.ApplySettings(); Pump(window);
    }
    private static void FontsAndAppearance(MainWindow window, MainViewModel vm, string output)
    {
        vm.Navigate("settings"); Pump(window);
        Require(!window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "LayoutJsonEditor") && window.GetVisualDescendants().OfType<ComboBox>().Any(t => t.Name == "SystemFontSelector"), "No inline JSON editor; installed-font choice exists");
        Require(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "触摸优化"), "Manual touch optimization setting was removed");
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (File.Exists(font))
        {
            vm.Settings.FontFilePath = AppFontService.Import(vm.Storage, font); vm.ApplySettings(); Pump(window);
            var expectedFont = AppFontService.Resolve(vm.Storage, vm.Settings); var actualFont = new Typeface(window.FontFamily).GlyphTypeface.FamilyName;
            Require(window.FontFamily.Equals(expectedFont) && actualFont.Contains("Segoe", StringComparison.OrdinalIgnoreCase), $"Font file applies immediately without OS installation: actual={window.FontFamily}, glyph={actualFont}, expected={expectedFont}, file={vm.Settings.FontFilePath}");
            Require(File.Exists(AppFontService.FilePath(vm.Storage, vm.Settings.FontFilePath)), "Font is copied below the managed data root");
            var restored = vm.Storage.Load(); Require(restored.Settings.FontFilePath == vm.Settings.FontFilePath, "Font file preference survives reload");
            vm.Settings.FontFilePath = null; vm.Settings.FontFamily = ""; vm.ApplySettings();
        }
        var collectionFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
        if (File.Exists(collectionFont))
        {
            vm.Settings.FontFilePath = AppFontService.Import(vm.Storage, collectionFont); vm.ApplySettings(); Pump(window);
            Require(new Typeface(window.FontFamily).GlyphTypeface.GlyphCount > 1000, "TTC collection imports and resolves CJK glyphs");
            vm.Settings.FontFilePath = null; vm.ApplySettings();
        }
        var invalid = Path.Combine(vm.Storage.Root, "not-a-font.ttf"); File.WriteAllText(invalid, "not a font");
        var rejected = false; try { AppFontService.Import(vm.Storage, invalid); } catch (InvalidDataException) { rejected = true; } Require(rejected, "Invalid font file is rejected");
        vm.Settings.BackgroundImagePath = Path.Combine(output, "background-fixture.png"); vm.Settings.BackgroundImageOpacity = .5;
        vm.Settings.TitleBarOpacity = vm.Settings.NavigationOpacity = vm.Settings.ContentOpacity = vm.Settings.PlayerOpacity = .15; vm.ApplySettings(); Pump(window);
        byte Alpha(string key) => ((SolidColorBrush)Application.Current!.Resources[key]!).Color.A;
        Require(Alpha("TitleBarBackgroundBrush") == Alpha("ContentBackgroundBrush") && Alpha("NavigationBackgroundBrush") == Alpha("PlayerBackgroundBrush"), "All main regions reveal the background equally by default");
        vm.Settings.TitleBarOpacity = .2; vm.Settings.NavigationOpacity = .4; vm.Settings.ContentOpacity = .6; vm.Settings.PlayerOpacity = .8; vm.ApplySettings();
        Require(Alpha("TitleBarBackgroundBrush") == 51 && Alpha("NavigationBackgroundBrush") == 102 && Alpha("ContentBackgroundBrush") == 153 && Alpha("PlayerBackgroundBrush") == 204, "Four region opacities apply independently");
        Require(window.FindControl<Button>("PlayerPlay")!.Opacity == 1, "Regional opacity does not fade buttons/text");
        vm.Navigate("songs"); Pump(window); using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, "beta8-background-regions.png"), PngBitmapEncoderOptions.Default);
        vm.Settings.BackgroundImagePath = null; vm.ApplySettings();
    }
    private static void LyricsFormats()
    {
        var lines = LyricsService.Parse("[offset:100]\n[00:01.12]dot\n[00:02.345]milliseconds\n[00:03:45]colon\n[00:04;67]semicolon\n[00:05]whole\n[00:05]translation\n[00:06:100]<00:06:10>word<00:07>next");
        var expected = new[] { 1.22, 2.445, 3.55, 4.77, 5.1, 6.2 };
        Require(lines.Count == expected.Length && lines.Select((line, i) => Math.Abs(line.Seconds - expected[i]) < .0001).All(x => x), "Dot/colon/semicolon fractions and whole seconds are parsed with offsets");
        Require(lines[4].Translation == "translation" && lines[5].Words.Count == 2 && Math.Abs(lines[5].Words[0].Seconds - 6.2) < .0001, "Translation and enhanced word timing preserve timestamp semantics");
        Require(LyricsService.Parse("[00:60:12]invalid\n[00:01]valid").Count == 1, "Malformed seconds are not treated as valid timestamps");
    }
    private static void DesktopLyrics(MainWindow owner, MainViewModel vm, string output)
    {
        vm.LyricLines.Clear(); vm.LyricLines.Add(new LyricLine(0, "悬浮歌词区域", "Translation"));
        using var lyrics = new DesktopLyricsWindow(owner, vm); lyrics.Show(owner); Pump(lyrics);
        using (var frame = lyrics.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "beta8-desktop-lyrics-idle.png"), PngBitmapEncoderOptions.Default);
        lyrics.MouseMove(new Point(4, 4)); Require(!lyrics.FrameVisible, "Empty part of hidden lyrics window does not reveal the frame");
        var line = lyrics.GetVisualDescendants().OfType<KaraokeLine>().Single(l => l.Name == "DesktopCurrentLyric");
        var hover = line.TranslatePoint(line.TextBounds.Center, lyrics)!.Value; lyrics.MouseMove(hover); Require(lyrics.FrameVisible, "Hovering actual lyric text shows full controls/frame");
        lyrics.MouseMove(new Point(12, 25)); Require(lyrics.FrameVisible, "Frame remains visible while moving within it");
        using var touch = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
        var surface = lyrics.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DesktopLyricsSurface"); var start = lyrics.Position;
        var pressed = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed);
        surface.RaiseEvent(new PointerPressedEventArgs(surface, touch, lyrics, new Point(16, 25), 10, pressed, KeyModifiers.None));
        lyrics.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, lyrics, touch, lyrics, new Point(46, 45), 20, pressed, KeyModifiers.None));
        Require(lyrics.Position.X > start.X && lyrics.Position.Y > start.Y, "Visible frame blank area supports dragging (outside buttons)");
        lyrics.RaiseEvent(new PointerReleasedEventArgs(lyrics, touch, lyrics, new Point(16, 25), 30, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        using (var frame = lyrics.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "beta8-desktop-lyrics-hover.png"), PngBitmapEncoderOptions.Default);
        lyrics.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, lyrics, touch, lyrics, new Point(-5, -5), 40, default, KeyModifiers.None)); Require(!lyrics.FrameVisible, "Frame hides again on exit");
    }
    private static void Temporary(MainViewModel vm, AppStorage storage, string source)
    {
        var copy = Path.Combine(storage.Root, "temporary" + Path.GetExtension(source)); File.Copy(source, copy);
        vm.Navigate("songs"); Wait(vm.PlayTemporaryFilesAsync([copy])); var id = vm.CurrentTrack!.Id;
        Require(vm.Page == "songs" && vm.State.History[0] == id && vm.State.Tracks.All(t => t.Id != id), "Temporary audio stays on the current page and enters history only");
        vm.Navigate("history"); Require(vm.VisibleTracks.Any(t => t.Id == id), "History resolves temporary metadata including cover");
        vm.Save(); var restored = storage.Load(); Require(restored.RecentTemporaryTracks.Any(t => t.Id == id), "History-only audio survives restart");
        using var restarted = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudio());
        Require(restarted.Page != "temporary", "Restart never opens a temporary playback page");
        restarted.Navigate("history"); Require(restarted.VisibleTracks.Any(t => t.Id == id), "Temporary history is playable after reload");
        restarted.Settings.HistoryLimit = 0; restarted.ApplySettings(); Require(restarted.State.RecentTemporaryTracks.Count == 0, "Temporary metadata is pruned with the history limit");
        vm.Navigate("temporary"); Require(vm.Page == "library", "Legacy temporary navigation is normalized to library");
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Wait(Task task) { var start = Environment.TickCount64; while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); if (Environment.TickCount64 - start > 10000) throw new TimeoutException(); } task.GetAwaiter().GetResult(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class SilentAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(300);
        public float Volume { get; set; } public long PlaybackGeneration { get; private set; }
        public string DeviceName { get; set; } = "系统默认"; public IReadOnlyList<string> Devices => ["系统默认"];
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() { IsPlaying = true; PlaybackGeneration++; } public void Pause() { IsPlaying = false; PlaybackGeneration++; }
        public void Stop() { Pause(); Position = TimeSpan.Zero; } public void Dispose() => Stop();
    }
}
