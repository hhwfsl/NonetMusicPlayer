using System.Diagnostics;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class ResponsivePlaybackAndLyricsChecks
{
    internal static void Run(MainWindow window, MainViewModel vm, string output)
    {
        var originalPage = vm.Page; var originalWidth = window.Width; var originalHeight = window.Height;
        var originalOpacity = vm.Settings.UiOpacity; var originalBackground = vm.Settings.BackgroundImagePath;
        window.Width = 1360; window.Height = 860;
        try
        {
            var parsed = LyricsService.Parse("[ar:artist]\n[offset:100]\n[00:01.25]原文 [注記]\n[00:01.25]Translation\n[00:01.25]Translation\n[00:03.500][00:05.50]次の行\n[00:99.00]invalid\n[00:07]<00:07.1>Hello <00:07.7>world");
            Require(parsed.Count == 4 && parsed[0].Text == "原文 [注記]" && parsed[0].Translation == "Translation", "Equal timestamps group bilingual lines without removing lyric brackets");
            Require(Math.Abs(parsed[0].Seconds - 1.35) < .001 && parsed[3].Words.Count == 2 && parsed[3].Text == "Hello world", "Strict seconds, offsets, multi-tags and enhanced word timestamps");
            var negative = LyricsService.Parse("[offset:-4000]\n[00:02.00]Before zero\n[00:06.00]After zero");
            Require(negative[0].Timed && negative[0].Seconds == -2 && !LyricsService.Parse("untimed")[0].Timed, "Negative offsets remain timestamped, not confused with text or spacer sentinels");
            var collection = new BulkObservableCollection<int>(); var notifications = 0;
            collection.CollectionChanged += (_, e) => { Require(e.Action == NotifyCollectionChangedAction.Reset, "One bulk reset"); notifications++; };
            collection.ReplaceAll(Enumerable.Range(0, 25000)); Require(notifications == 1 && collection.Count == 25000, "Large snapshot publishes one collection event");
            var playlist = vm.CreatePlaylist("Beta6 cover fixture");
            var withCover = vm.State.Tracks.First(t => t.Artwork is not null);
            var withoutCover = vm.State.Tracks.First(t => t.Artwork is null);
            vm.AddToPlaylist(playlist, [withCover, withoutCover]);
            Require(vm.GetPlaylistArtwork(playlist) is not null, "Custom playlist uses first song cover");
            vm.ReorderPlaylistTracks(playlist, [withoutCover.Id], withCover.Id, true);
            Require(vm.GetPlaylistArtwork(playlist) is null, "First song without art uses default, not another song's art");
            vm.Navigate("playlist:" + playlist.Id); Pump(window);
            var toolbar = window.FindControl<Grid>("LibraryActions")!;
            Require(toolbar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).All(b => b.Content is VectorIcon || b.Name == "BatchEntryButton"), "Toolbar contains icon-only actions except explicit batch-exit state");
            Require(!toolbar.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "播放全部"), "Duplicate Play All removed");
            var search = window.FindControl<Grid>("SearchContainer")!; var add = window.FindControl<Button>("LibraryAddFiles")!;
            Require(search.TranslatePoint(default, window)!.Value.X < add.TranslatePoint(default, window)!.Value.X && add.IsVisible, "Playlist search appears to the left of right-aligned actions");
            var surface = window.FindControl<Border>("PlayerSurface")!; var progress = window.FindControl<Slider>("PlaybackSlider")!;
            var columns = window.FindControl<Grid>("PlayerColumns")!;
            var top = progress.TranslatePoint(new Point(0, progress.Bounds.Height / 2), surface)!.Value.Y;
            Require(Math.Abs(top) < 1 && progress.TranslatePoint(new Point(0, progress.Bounds.Height), surface)!.Value.Y <= columns.TranslatePoint(default, surface)!.Value.Y + 4, "Progress overlays player boundary, above all player content");
            Require(surface.Bounds.Height <= 110, "Default player is compact");
            Require(window.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent), "Native transparent surface requested for true window corners");
            Capture(window, output, "beta6-playlist");
            var workspace = window.FindControl<WorkspacePanel>("Workspace")!;
            var navigation = workspace.Children.OfType<LayoutWidget>().Single(w => w.Key == "Navigation");
            var edge = navigation.TranslatePoint(new Point(navigation.Bounds.Width - 1, navigation.Bounds.Height / 2), window)!.Value;
            var priorWidth = vm.Settings.SidebarWidth;
            window.MouseDown(edge, MouseButton.Left); window.MouseMove(edge + new Vector(25, 0), RawInputModifiers.LeftMouseButton); window.MouseUp(edge + new Vector(25, 0), MouseButton.Left); Pump(window);
            Require(vm.Settings.SidebarWidth > priorWidth + 20, "Mouse edge drag adjusts and persists navigation width");
            using (var touch = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true))
            {
                var grip = window.FindControl<Border>("PlayerResizeGrip")!;
                var start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
                var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
                var priorHeight = vm.Settings.PlayerHeight;
                grip.RaiseEvent(new PointerPressedEventArgs(grip, touch, window, start, 10, pressed, KeyModifiers.None));
                workspace.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, workspace, touch, window, start - new Vector(0, 18), 20, pressed, KeyModifiers.None));
                workspace.RaiseEvent(new PointerReleasedEventArgs(workspace, touch, window, start - new Vector(0, 18), 30, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
                Pump(window); Require(vm.Settings.PlayerHeight >= priorHeight + 15, "Touch grip drag adjusts and persists player height");
            }
            vm.Settings.SidebarWidth = priorWidth; vm.Settings.PlayerHeight = 96; vm.ApplySettings();
            foreach (var source in new[] { "album:" + withCover.Album, "artist:" + withCover.Artist })
            {
                vm.Navigate(source); Pump(window);
                Require(!add.IsVisible && !window.FindControl<Button>("LibraryAddFolder")!.IsVisible && window.FindControl<ContentControl>("PlaylistHeader")!.IsVisible, "Classifications have covers but no import actions");
            }
            vm.Navigate("settings"); Pump(window);
            var settings = (SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!;
            var settingLabels = settings.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Require(!settingLabels.Contains("播放速度") && !settingLabels.Contains("默认导航宽度") && !settingLabels.Contains("默认播放栏高度"), "Geometry is edge-controlled and speed is in player menu");
            Require(settingLabels.Contains("恢复默认设置") || settings.GetLogicalDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "恢复默认设置"), "Settings reset entry exists");
            var scroll = settings.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is StackPanel stack && stack.Children.OfType<Border>().Count() >= 6);
            window.MouseMove(settings.TranslatePoint(new Point(settings.Bounds.Width - 24, settings.Bounds.Height - 50), window)!.Value);
            window.MouseWheel(settings.TranslatePoint(new Point(settings.Bounds.Width - 24, settings.Bounds.Height - 50), window)!.Value, new Vector(0, -3)); Pump(window);
            Require(scroll.Offset.Y > 0, "Wheel in blank settings area scrolls");
            vm.Settings.UiOpacity = .75; vm.ApplySettings(); Require(Math.Abs(window.Opacity - .75) < .001, "UI opacity applies to the entire window");
            var background = Path.Combine(output, "background-fixture.png");
            if (File.Exists(background))
            {
                vm.Settings.BackgroundImagePath = AppBackgroundService.Import(vm.Storage, background); vm.ApplySettings(); Pump(window);
                var image = window.FindControl<Image>("AppBackgroundImage")!;
                Require(image.Bounds.Height >= window.Bounds.Height - 2 && image.Bounds.Width >= window.Bounds.Width - 2 && ((ISolidColorBrush)Application.Current!.Resources["SurfaceBrush"]!).Color.A < 255, "Whole-app background is visible through navigation/title/player panels");
                Capture(window, output, "beta6-whole-background");
            }
            var pluginPath = PluginTestFixtures.Snake(output);
            if (File.Exists(pluginPath))
            {
                var plugin = vm.Plugins.Install(pluginPath); vm.Navigate("plugins"); Pump(window);
                var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "PluginEnabled");
                toggle.IsChecked = true; Pump(window);
                Require(vm.Page == "plugins" && window.FindControl<StackPanel>("PluginNavigation")!.Children.Count > 0, "Enabling immediately updates sidebar while plugin center remains open");
                toggle.IsChecked = false; Pump(window);
                Require(!window.FindControl<StackPanel>("PluginSection")!.IsVisible, "Disabling immediately hides empty plugin section");
                vm.DisablePlugin(plugin); vm.Plugins.Uninstall(plugin);
            }
            vm.DeletePlaylist(playlist);
            Console.WriteLine("PASS BETA6: strict bilingual/word LRC, compact unobscured player, icon/search toolbar, classification-only groups, live plugins, settings wheel/opacity and full background");
        }
        finally
        {
            vm.Settings.UiOpacity = originalOpacity; vm.Settings.BackgroundImagePath = originalBackground;
            window.Width = originalWidth; window.Height = originalHeight; vm.ApplySettings(); vm.Navigate(originalPage); Pump(window);
        }
        LargePlaylist(output);
        DesktopLyricsPreview(output);
        TemporaryAudio(output);
        var plan = DefaultAudioAppService.RegistrationPlan(Path.Combine(output, "Nonet.exe"));
        Require(plan.All(item => !item.Key.Contains("UserChoice")) && plan.Any(item => item.Name == ".flac") && plan.Any(item => item.Value.EndsWith("\"%1\"")), "Default app registration includes quoted command/capabilities, never overrides UserChoice");
        Require(DefaultAudioAppService.LinuxDesktopEntry("/opt/music $player", "/opt/icon.png").Contains("\\\\$player"), "Linux Exec preserves two-level quoted argument escaping");
    }
    private static void LargePlaylist(string output)
    {
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(output, "large-library-" + Guid.NewGuid().ToString("N")))), new SilentAudioPlayer());
        var playlist = new Playlist { Name = "25,000 songs" }; vm.State.Playlists.Add(playlist); vm.Playlists.Add(playlist);
        for (var i = 0; i < 25000; i++) { var song = new TrackItem("large-" + i, "Track " + i, "Artist " + i / 10, "Album " + i / 20, "unused.wav", ".wav", 100); vm.State.Tracks.Add(song); playlist.TrackIds.Add(song.Id); }
        vm.Navigate("songs");
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump(window);
        var watch = Stopwatch.StartNew(); vm.Navigate("playlist:" + playlist.Id); Pump(window); watch.Stop();
        var tracks = window.FindControl<TrackListBox>("TracksList")!;
        var realized = tracks.GetRealizedContainers().Count();
        Require(tracks.GetVisualDescendants().OfType<VirtualizingStackPanel>().Any() && realized < 80 && realized > 0, "Large song list realizes only viewport containers");
        Require(watch.ElapsedMilliseconds < 5000, "Large playlist navigation must not have quadratic subscription work");
        var elapsed = watch.ElapsedMilliseconds; watch.Restart(); vm.Navigate("artists"); Pump(window); watch.Stop();
        var cards = window.FindControl<ContentControl>("AlternatePage")!.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single();
        Require(cards.RealizedCount is > 0 and < 80, "2,500 artist cards are independently viewport-virtualized");
        var realizedCards = cards.RealizedCount;
        Require(((Control)cards.Content!).TranslatePoint(default, cards)!.Value.Y < 2, "Card canvas begins at the top of its viewport");
        var point = cards.TranslatePoint(new Point(cards.Bounds.Width / 2, cards.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(point, new Vector(0, -5)); Pump(window);
        Require(cards.Offset.Y > 0 && cards.RealizedCount is > 0 and < 80, "Artist page scrolls while recycling independent cards");
        vm.Navigate("albums"); Pump(window);
        var albums = window.FindControl<ContentControl>("AlternatePage")!.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single();
        window.MouseWheel(point, new Vector(0, -5)); Pump(window);
        Require(albums.Offset.Y > 0 && albums.RealizedCount is > 0 and < 80, "Album page scrolls while recycling independent cards");
        Console.WriteLine($"PASS PERFORMANCE: 25,000 songs opened in {elapsed} ms, {realized} song containers; 2,500 artists opened in {watch.ElapsedMilliseconds} ms, {realizedCards} cards; both grids scroll");
        Capture(window, output, "beta6-large-artists"); window.Close();
    }
    private static void DesktopLyricsPreview(string output)
    {
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(output, "desktop-lyrics-" + Guid.NewGuid().ToString("N")))), new SilentAudioPlayer());
        var owner = new MainWindow { DataContext = vm }; owner.Show(); Pump(owner);
        using var lyrics = new DesktopLyricsWindow(owner, vm);
        vm.Settings.LyricOffset = 2;
        foreach (var line in LyricsService.Parse("[00:00]夜空中最亮的星\n[00:00]The brightest star in the night sky\n[00:04]请照亮我前行")) vm.LyricLines.Add(line);
        lyrics.Show(owner); Pump(lyrics);
        var text = lyrics.GetVisualDescendants().OfType<KaraokeLine>().ToArray();
        Require(text.Length == 2 && text[0].Text == "夜空中最亮的星" && text[0].Progress == 0 && text[1].Text == "The brightest star in the night sky", "Desktop lyrics draw separate bilingual lines without progressive fill");
        Capture(lyrics, output, "beta6-desktop-lyrics");
        lyrics.MouseMove(new Point(lyrics.Width / 2, 20)); Pump(lyrics); Capture(lyrics, output, "beta6-desktop-lyrics-hover");
        lyrics.HideLyrics(); owner.Close();
        Console.WriteLine("PASS DESKTOP LYRICS: transparent original/translation and hover controls");
    }
    private static void TemporaryAudio(string output)
    {
        var data = Path.Combine(output, "temporary-audio-" + Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(data); var wav = Path.Combine(data, "temporary.wav");
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            const int samples = 44100; writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2); for (var i = 0; i < samples; i++) writer.Write((short)0);
        }
        using (var vm = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudioPlayer()))
        {
            Wait(vm.PlayTemporaryFilesAsync([wav]));
            Require(vm.CurrentTrack?.FilePath == wav && vm.PlayingSourcePage == "temporary" && vm.State.Tracks.Count == 0 && vm.LikedPlaylist.TrackIds.Count == 0, "External audio plays without importing library/playlist references");
            vm.Seek(.5); vm.Save();
        }
        using (var vm = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudioPlayer()))
        {
            Require(vm.CurrentTrack?.FilePath == wav && !vm.IsPlaying && vm.State.Tracks.Count == 0, "Temporary last track restores without autoplay or playlist insertion");
            vm.ToggleFavorite(vm.CurrentTrack); Require(vm.State.Tracks.Count == 1 && vm.LikedPlaylist.TrackIds.Count == 1, "Favoriting temporary playback explicitly adds it to Favorites");
            var root = vm.Storage.Root; var likedCount = vm.LikedPlaylist.TrackIds.Count; vm.Settings.Accent = "#FF0000"; vm.Settings.KeyBindings["playPause"] = "Ctrl+P"; vm.ResetSettings();
            Require(vm.Settings.Theme == "System" && vm.Settings.Accent == new AppSettings().Accent && vm.Settings.KeyBindings.Count == 0 && vm.Storage.Root == root && vm.LikedPlaylist.TrackIds.Count == likedCount, "Settings reset retains music/data and restores preferences");
        }
        Console.WriteLine("PASS TEMPORARY/DEFAULTS: file activation playback, resume without autoplay, explicit Favorites import and non-destructive settings reset");
    }
    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Capture(Window window, string output, string name) { using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class SilentAudioPlayer : IAudioPlayer
    {
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(30);
        public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; }
        public void Dispose() => Stop();
    }
}
