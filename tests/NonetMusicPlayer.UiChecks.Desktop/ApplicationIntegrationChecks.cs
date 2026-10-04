using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class ApplicationIntegrationChecks
{
    public static void Run(string[] args)
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        // 开关可以位于输出目录之前，避免将 --xxx 误建为工作区根目录下的测试文件夹。
        var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "artifacts/ui-checks");
        var data = Path.Combine(output, "test-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        if (args.Contains("--experience-revision-only")) { ExperienceRevisionChecks.Run(output); return; }
        if (args.Contains("--desktop-revision-only")) { DesktopRevisionChecks.Run(output); return; }
        if (args.Contains("--storage-compatibility-only")) { CoreChecks.Run(output); DataStatisticsChecks.Run(output); return; }
        if (args.Contains("--export-icon-only"))
        {
            using var input = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Nonet/Assets/icon.ico"));
            using var original = new Bitmap(input); using var scaled = original.CreateScaledBitmap(new PixelSize(256, 256), BitmapInterpolationMode.HighQuality);
            scaled.Save(Path.Combine(output, "app-icon-256.png"), PngBitmapEncoderOptions.Default); Console.WriteLine("Exported exact supplied icon for macOS packaging."); return;
        }
        LayoutChecks.Run(output);
        LogChecks.Run(output);
        PlaybackRecoveryChecks.Run(output);
        if (!args.Contains("--skip-core")) CoreChecks.Run(output);
        if (!args.Contains("--skip-core")) DataStatisticsChecks.Run(output);
        var storage = new AppStorage(data); var scanner = new MusicLibraryScanner(storage); var fake = new SilentAudioPlayer();
        using var vm = new MainViewModel(scanner, fake);
        var window = new MainWindow { DataContext = vm };
        window.Show(); Capture(window, output, "empty-1360");
        Require(window.WindowDecorations == WindowDecorations.None && window.Icon is not null, "Custom titlebar and icon");
        foreach (var name in new[] { "CloseWindowButton", "MinimizeWindowButton", "MaximizeWindowButton" }) Require(window.FindControl<Button>(name)!.Content is null, "Traffic lights must have no icons.");
        var fixtureFolder = Path.Combine(data, "Music"); Directory.CreateDirectory(fixtureFolder);
        var wav = Path.Combine(fixtureFolder, "Test.wav"); WriteWave(wav, 8);
        var cover = Path.Combine(data, "fixture-cover.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(320, 320))) { var art = new Border { Width = 320, Height = 320, Background = new LinearGradientBrush { GradientStops = { new GradientStop(Color.Parse("#8070F0"), 0), new GradientStop(Color.Parse("#275854"), 1) } } }; art.Measure(new Size(320,320)); art.Arrange(new Rect(0,0,320,320)); bitmap.Render(art); bitmap.Save(cover, PngBitmapEncoderOptions.Default); }
        using (var tag = TagLib.File.Create(wav))
        {
            tag.Tag.Title = "夜空中最亮的星"; tag.Tag.Performers = ["逃跑计划"]; tag.Tag.Album = "世界";
            tag.Tag.Pictures = [new TagLib.Picture(cover) { Type = TagLib.PictureType.FrontCover }]; tag.Save();
        }
        File.WriteAllText(Path.ChangeExtension(wav, ".lrc"), "[ar:逃跑计划]\n[offset:500]\n[00:01.00][00:03.00]夜空中最亮的星\n[00:05.00]请照亮我前行", Encoding.UTF8);
        var imported = Wait(vm.ImportAsync([fixtureFolder]));
        Require(imported.Count == 1, "Scan supported tracks"); var embedded = imported[0];
        Require(embedded.Title == "夜空中最亮的星" && embedded.Artist == "逃跑计划" && embedded.DurationSeconds >= 7, "Read tags and duration");
        Require(embedded.Artwork is not null, "Embedded song cover must decode");
        Require(File.Exists(vm.Lyrics.PathFor(embedded.Id)) && Path.GetDirectoryName(vm.Lyrics.PathFor(embedded.Id)) != fixtureFolder, "Separate lyric directory");
        var lyric = LyricsService.Parse(vm.Lyrics.Read(embedded.Id));
        Require(lyric.Count == 3 && Math.Abs(lyric[0].Seconds - 1.5) < .001 && Math.Abs(lyric[1].Seconds - 3.5) < .001, "LRC multiple timestamps and offset");
        if (args.Contains("--provider-only"))
        {
            var plugin = args.First(a => a.StartsWith("--plugin="))[9..];
            ProviderChecks.Run(vm, plugin, fixtureFolder, args.Contains("--audio")); window.Close(); return;
        }
        if (args.Contains("--current-only")) { WorkspaceContinuityAndCommandsChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--nonet-release-only")) { NonetReleaseChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--terminal-and-lyrics-only")) { TerminalAndLyricsTimingChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta15-only")) { ToolTipChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta14-only")) { CalendarArtworkLocalizationChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta13-only")) { TypographyAndIconChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta12-only")) { CalendarAndWindowLifecycleChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta11-only")) { AppearanceAndWorkflowChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta10-only")) { DesktopLyricsAndDownloadChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta9-only")) { InputTrayAndPluginConfigurationChecks.Run(window, vm, output); window.Close(); return; }
        if (args.Contains("--beta8-only"))
        {
            File.Copy(cover, Path.Combine(output, "background-fixture.png"), true);
            TouchAndPortableDataChecks.Run(window, vm, output); window.Close(); return;
        }
        Require(LyricsService.Parse("纯文本歌词\n第二行").Count == 2, "Plain lyrics");
        var tracks = new[]
        {
            embedded, Track("春泥", "庾澄庆", "海啸", wav),
            Track("A very long song title — live acoustic recording with an extended ending", "A long artist name / 合作艺术家", "A long album title — 特别纪念版", wav),
            Track("晴天", "周杰伦", "叶惠美", wav)
        };
        vm.State.Tracks.AddRange(tracks.Skip(1));
        for (var i = 0; i < 20; i++) vm.State.Tracks.Add(Track($"曲库测试歌曲 {i + 1}", "本地艺术家", "测试专辑", wav));
        // 浏览歌曲只统计歌单引用；测试曲库也必须按用户真实导入流程加入一个歌单。
        vm.AddToPlaylist(vm.CreatePlaylist("曲库夹具"), vm.State.Tracks.ToArray());
        vm.ApplyFilter(); vm.ToggleFavorite(embedded); Require(embedded.IsFavorite, "Favorite"); vm.UndoCommand.Execute(null); Require(!embedded.IsFavorite, "Undo favorite");
        var playlist = vm.CreatePlaylist("我的夜间歌单"); vm.AddToPlaylist(playlist, tracks.Take(3)); vm.Navigate("playlist:" + playlist.Id);
        vm.MoveInPlaylist(tracks[1], -1); Require(vm.VisibleTracks[0].Id == tracks[1].Id, "Playlist reorder");
        vm.RemoveTracks([tracks[1]]); Require(vm.VisibleTracks.Count == 2, "Remove playlist references"); vm.UndoCommand.Execute(null); Require(vm.VisibleTracks.Count == 3 && File.Exists(wav), "Undo removal without deleting file");
        Wait(vm.PlayTrackAsync(vm.VisibleTracks[0]));
        fake.End(); Dispatcher.UIThread.RunJobs(); PumpUntil(() => vm.CurrentTrack?.Id == tracks[0].Id, "Default advances following playlist");
        vm.SearchText = "晴天"; Require(vm.VisibleTracks.Count == 0, "Search scoped to playlist"); vm.Navigate("songs"); vm.SearchText = "晴天"; Require(vm.VisibleTracks.Count == 1, "Library search"); vm.SearchText = "";
        Wait(vm.PlayTrackAsync(embedded)); vm.PlaybackDuration = 263; vm.StatusText = "正在播放 · 列表循环";
        foreach (var size in new[] { new Size(1360, 860), new Size(980, 660), new Size(1920, 1080) })
        {
            window.Width = size.Width; window.Height = size.Height; Capture(window, output, $"library-{size.Width:0}x{size.Height:0}"); VerifyColumns(window);
            Require(!window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "QueuePanel"), "Queue sidebar removed");
            VerifySliders(window); Console.WriteLine($"PASS UI: aligned list, pure traffic lights, cover and search at {size.Width}x{size.Height}");
            var list = window.FindControl<ListBox>("TracksList")!; list.ScrollIntoView(vm.VisibleTracks.Last()); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); VerifyColumns(window); list.ScrollIntoView(vm.VisibleTracks.First()); Dispatcher.UIThread.RunJobs();
        }
        window.Width = 1360; window.Height = 860;
        foreach (var page in new[] { "settings", "plugins", "lyrics", "albums", "artists", "favorites", "statistics" }) { vm.Navigate(page); Capture(window, output, page); }
        vm.Navigate("songs"); vm.Settings.Theme = "Light"; vm.ApplySettings(); Capture(window, output, "library-light"); vm.Settings.Theme = "Dark"; vm.ApplySettings();
        PlaybackRecoveryChecks.RunLyrics(window, vm);
        TitleTaskbarChecks.Run(window, vm);
        VerifyConfigurationAndPlayer(window, vm, output);
        VerifyTrackInteractions(window, vm, fake, output);
        SelectionAndCustomizationChecks.Run(window, vm, output);
        SettingsThemeChecks.Run(window, vm, output);
        NavigationAndLocalizationChecks.Run(window, vm, output);
        ResponsivePlaybackAndLyricsChecks.Run(window, vm, output);
        VirtualizationAndOutputChecks.Run(window, vm, output);
        TouchAndPortableDataChecks.Run(window, vm, output);
        InputTrayAndPluginConfigurationChecks.Run(window, vm, output);
        DesktopLyricsAndDownloadChecks.Run(window, vm, output);
        AppearanceAndWorkflowChecks.Run(window, vm, output);
        CalendarAndWindowLifecycleChecks.Run(window, vm, output);
        TypographyAndIconChecks.Run(window, vm, output);
        CalendarArtworkLocalizationChecks.Run(window, vm, output);
        ToolTipChecks.Run(window, vm, output);
        vm.Save(); var restored = storage.Load(); Require(restored.Tracks.Count == vm.State.Tracks.Count && restored.Playlists.Count == vm.State.Playlists.Count, "State survives restart including fixed liked playlist");
        // 当前主存储为 SQLite；旧 JSON 损坏不能影响优先读取的有效数据库。
        storage.Save(restored); File.WriteAllText(Path.Combine(data, "state.json"), "{broken");
        Require(storage.Load().Tracks.Count == restored.Tracks.Count, "Legacy JSON cannot override healthy SQLite state");
        new LibraryDatabase(storage.DatabasePath).Backup(storage.DatabaseBackupPath);
        File.WriteAllText(storage.DatabasePath, "{broken"); var recovered = storage.Load();
        Require(recovered.Tracks.Count == restored.Tracks.Count && storage.RecoveryMessage is not null, "Corrupt SQLite state recovers consistent backup");
        storage.Save(recovered);
        Require(storage.ReadBackup().Tracks.Count == restored.Tracks.Count && Directory.GetFiles(data, "library.db.corrupt-*").Length > 0, "Recovery preserves valid database backup and damaged original");
        var m3u = Path.Combine(data, "Import.m3u8"); File.WriteAllText(m3u, "#EXTM3U\n" + new Uri(wav).AbsoluteUri + "\n"); Wait(vm.ImportM3uAsync(m3u)); Require(vm.CurrentPlaylist?.TrackIds.Count == 1, "M3U file URI import"); vm.ExportM3u(Path.Combine(data, "export.m3u8")); Require(File.ReadAllText(Path.Combine(data, "export.m3u8")).Contains("#EXTINF:"), "M3U export"); vm.Navigate("songs");
        var themePackage = Path.Combine(data, "theme.impp");
        CreatePackage(themePackage, "manifest.json", JsonSerializer.Serialize(new PluginManifest { Id = "test.theme", Name = "测试主题", Type = "theme", Tokens = new() { ["Accent"] = "#65B89E" } }, AppStorage.Json));
        var theme = vm.Plugins.Install(themePackage); Require(!theme.Enabled, "Plugins default disabled"); vm.Plugins.SetEnabled(theme, true); vm.ApplySettings(); Capture(window, output, "theme-plugin"); vm.DisablePlugin(theme); vm.Plugins.Uninstall(theme);
        var unsafePackage = Path.Combine(data, "unsafe.impp"); CreatePackage(unsafePackage, "../outside.txt", "bad");
        var rejected = false; try { PluginManager.Inspect(unsafePackage); } catch (InvalidDataException) { rejected = true; } Require(rejected, "Zip traversal rejected");
        var blocked = false; try { LoopbackRangeStream.Validate(new Uri("https://example.com/audio?token=" + new string('a', 32))); } catch (InvalidDataException) { blocked = true; } Require(blocked, "Remote provider URL rejected");
        if (args.Contains("--audio"))
        {
            AudioChecks.Run(output);
            if (args.Contains("--memory")) MemoryAudioChecks.Run(output);
            using var releaseTags = TagLib.File.Create(Path.Combine(output, "audio-fixtures", "tone-44100-1ch.wav"));
            releaseTags.Tag.Title = "44.1 kHz 单声道回归"; releaseTags.Tag.Performers = ["音频校验"];
            releaseTags.Tag.Pictures = [new TagLib.Picture(cover) { Type = TagLib.PictureType.FrontCover }]; releaseTags.Save();
        }
        var packageArgument = args.FirstOrDefault(a => a.StartsWith("--plugin="));
        if (packageArgument is not null) ProviderChecks.Run(vm, packageArgument[9..], fixtureFolder, args.Contains("--audio"));
        window.FindControl<Button>("MaximizeWindowButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(window.WindowState == WindowState.Maximized, "Maximize");
        window.FindControl<Button>("MaximizeWindowButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(window.WindowState == WindowState.Normal, "Restore");
        window.FindControl<Button>("MinimizeWindowButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(window.WindowState == WindowState.Minimized, "Minimize"); window.WindowState = WindowState.Normal;
        window.FindControl<Button>("CloseWindowButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(!window.IsVisible, "Close");
        Console.WriteLine("PASS: tags, embedded cover, lyrics, playlists, favorites, undo, persistence, backup, plugin installation and security");
        Console.WriteLine("Screenshots: " + output);
    }
    private static void CreatePackage(string path, string entry, string content) { using var zip = ZipFile.Open(path, ZipArchiveMode.Create); using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write(content); }
    private static TrackItem Track(string title, string artist, string album, string path) => new(Guid.NewGuid().ToString("N"), title, artist, album, path, ".mp3", 9000000) { DurationSeconds = 263 };
    private static void Capture(Window window, string output, string name)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame."); frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private static void VerifyColumns(MainWindow window)
    {
        var header = window.FindControl<Grid>("TrackHeader")!;
        var row = window.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("track-row"));
        for (var i = 1; i < 8; i++)
        {
            var label = (Control)header.Children[i]; var value = row.Children.First(c => Grid.GetColumn(c) == i);
            var a = label.TranslatePoint(new Point(), window)!.Value; var b = value.TranslatePoint(new Point(), window)!.Value;
            Require(Math.Abs(a.X - b.X) <= 1, $"Column {i} alignment {a.X} != {b.X}");
        }
        Require(row.ColumnDefinitions[3].ActualWidth >= 120, "Song title width");
        Require(!header.Children.OfType<Button>().Any(), "Column labels cannot sort");
    }
    private static void VerifySliders(MainWindow window)
    {
        foreach (var slider in window.GetVisualDescendants().OfType<Slider>())
        {
            if (slider is PlayerProgressSlider rail) { Require(rail.ThumbCenter.Y - rail.ThumbRadius >= 0 && rail.ThumbCenter.Y + rail.ThumbRadius <= rail.Bounds.Height, "Custom rail thumb is not clipped"); continue; }
            var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single(); var p = thumb.TranslatePoint(new Point(), slider)!.Value;
            Require(p.Y >= 0 && p.Y + thumb.Bounds.Height <= slider.Bounds.Height, "Slider thumb clipped");
        }
    }
    private static void VerifyConfigurationAndPlayer(MainWindow window, MainViewModel vm, string output)
    {
        vm.Settings.TouchMode = false; vm.ApplySettings(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Require(window.FindControl<Button>("EditLayoutButton") is null, "Layout entry must not be in titlebar");
        var player = window.FindControl<Grid>("PlayerColumns")!;
        var surface = window.FindControl<Border>("PlayerSurface")!;
        var progress = window.FindControl<Slider>("PlaybackSlider")!;
        var play = window.FindControl<Button>("PlayerPlay")!;
        var playCenter = play.TranslatePoint(new Point(play.Bounds.Width / 2, 0), surface)!.Value.X;
        // Fluent Slider reserves 12 px at each end for its thumb. The negative layout
        // margin brings the visible track to the player edge without clipping the thumb.
        var progressStart = progress.TranslatePoint(new Point(0, 0), surface)!.Value.X;
        var progressEnd = progress.TranslatePoint(new Point(progress.Bounds.Width, 0), surface)!.Value.X;
        Require(Math.Abs(progressStart) <= 1 && Math.Abs(progressEnd - surface.Bounds.Width) <= 1 && Math.Abs(playCenter - surface.Bounds.Width / 2) < 2,
            $"Full-width edge progress and centered transport: start={progressStart:0.##}, end={progressEnd:0.##}, surface={surface.Bounds.Width:0.##}, play={playCenter:0.##}");
        vm.ToggleFavorite(vm.CurrentTrack); Dispatcher.UIThread.RunJobs();
        var heart = window.FindControl<VectorIcon>("FavoriteActiveIcon")!;
        Require(heart.IsVisible && heart.Filled && ((ISolidColorBrush)heart.Brush!).Color == Color.Parse("#F0526C"), "Liked heart must be solid red");
        Capture(window, output, "player-liked"); vm.UndoCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Require(!heart.IsVisible, "Favorite undo updates heart");
        var modeButton = window.FindControl<Button>("PlayerMode")!;
        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var modeMenu = modeButton.ContextMenu!;
        Require(modeMenu.Items.Count == 3 && modeMenu.Items.OfType<MenuItem>().All(m => m.Icon is VectorIcon), "Three explicit play modes with icons"); Capture(window, output, "player-mode-menu");
        foreach (var mode in Enum.GetValues<PlayMode>())
        {
            var item = modeMenu.Items.OfType<MenuItem>().Single(m => (PlayMode)m.Tag! == mode);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Require(vm.Settings.PlayMode == mode, "Play mode menu " + mode);
        }
        modeMenu.Close(); vm.SetPlayMode(PlayMode.RepeatAll);
        Require(window.FindControl<Button>("PlayerLyrics") is null && window.FindControl<Button>("PlayerDesktopLyrics") is not null, "Main lyrics opens by cover; floating lyrics has a separate control");
        var volumeButton = window.FindControl<Button>("PlayerVolume")!;
        volumeButton.Flyout!.ShowAt(volumeButton); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var volume = window.FindControl<Slider>("VolumeSlider")!;
        Require(volume.Orientation == Avalonia.Layout.Orientation.Vertical && volume.Bounds.Height >= 100, "Vertical volume popup");
        volume.Value = 37; Dispatcher.UIThread.RunJobs(); Require(Math.Abs(vm.Volume - 37) < .01, "Volume popup binding");
        Capture(window, output, "player-volume-popup"); volumeButton.Flyout.Hide(); vm.Volume = 80;
        var moreButton = window.FindControl<Button>("PlayerMore")!;
        moreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(moreButton.ContextMenu!.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "添加到歌单").Items.Count >= 2, "Current track playlist and create actions"); moreButton.ContextMenu.Close();
        vm.Navigate("settings"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        ((SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!).FocusLayoutConfiguration(); Capture(window, output, "settings-json");
        Require(!window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "LayoutJsonEditor"), "Settings links to external configuration without displaying JSON");
        var originalSettings = window.FindControl<ContentControl>("AlternatePage")!.Content;
        window.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.E, KeyModifiers = Avalonia.Input.KeyModifiers.Control });
        Require(ReferenceEquals(originalSettings, window.FindControl<ContentControl>("AlternatePage")!.Content), "Ctrl+E focuses external layout management without recreating settings");
        vm.Navigate("songs"); vm.Navigate("settings"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        ((SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!).FocusLayoutConfiguration(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Require(!window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "LayoutJsonEditor"), "JSON stays external after reentering settings");
        vm.Navigate("songs");
        var original = File.ReadAllText(window.LayoutConfiguration.Path);
        var custom = original.Replace("\"id\": \"mode\"", "\"id\": \"temporary\"").Replace("\"id\": \"favorite\"", "\"id\": \"mode\"").Replace("\"id\": \"temporary\"", "\"id\": \"favorite\"");
        window.ApplyLayoutConfiguration(custom); Capture(window, output, "player-custom-config");
        Require(window.FindControl<Button>("PlayerFavorite")!.TranslatePoint(default, player)!.Value.X < modeButton.TranslatePoint(default, player)!.Value.X, "Handwritten config reorders individual controls");
        Require(new UiLayoutService(vm.Storage).Current.Player.Items.Count == 4, "Layout survives reload");
        custom = File.ReadAllText(window.LayoutConfiguration.Path);
        var bad = custom.Replace("\"210\"", "\"850\""); var rejected = false;
        try { window.ApplyLayoutConfiguration(bad); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && File.ReadAllText(window.LayoutConfiguration.Path) == custom, "Unusable layout rejected without overwriting saved config");
        rejected = false;
        var overlap = JsonNode.Parse(custom, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
        overlap["player"]!["items"]![2]!["grid"]!["columns"]![1] = "10";
        try { window.ApplyLayoutConfiguration(overlap.ToJsonString()); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && File.ReadAllText(window.LayoutConfiguration.Path) == custom, "Buttons cannot overflow their own grid cells and overlap");
        rejected = false;
        var largeSlider = JsonNode.Parse(custom, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
        largeSlider["player"]!["items"]![0]!["height"] = 512;
        try { window.ApplyLayoutConfiguration(largeSlider.ToJsonString()); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && File.ReadAllText(window.LayoutConfiguration.Path) == custom, "Progress hit area cannot hide necessary playback buttons");
        var compact = File.ReadAllText(Path.GetFullPath("samples/layouts/compact.layout.json"));
        vm.Settings.PlayerHeight = 124; vm.ApplySettings(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        window.ApplyLayoutConfiguration(compact); Capture(window, output, "player-compact-config");
        Require(progress.TranslatePoint(default, surface)!.Value.Y < play.TranslatePoint(default, surface)!.Value.Y && progress.Bounds.Width >= surface.Bounds.Width - 2, "Progress remains full-width at the player edge even with compact handwritten layout");
        window.ApplyLayoutConfiguration(custom);
        vm.Settings.PlayerHeight = 180; vm.ApplySettings(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var tall = JsonNode.Parse(custom, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
        tall["player"]!["items"]![1]!["grid"]!["items"]![0]!["height"] = 140;
        var tallJson = tall.ToJsonString(); window.ApplyLayoutConfiguration(tallJson); tallJson = File.ReadAllText(window.LayoutConfiguration.Path);
        vm.Settings.PlayerHeight = 108; vm.ApplySettings(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Require(window.FindControl<Button>("PlayerArtwork")!.Bounds.Height == 60 && File.ReadAllText(window.LayoutConfiguration.Path) == tallJson, "Settings resize temporarily falls back without destroying user config");
        vm.Settings.PlayerHeight = 180; vm.ApplySettings(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        Require(window.FindControl<Button>("PlayerArtwork")!.Bounds.Height == 140, $"Usable custom layout returns when available space is restored: cover={window.FindControl<Button>("PlayerArtwork")!.Bounds.Height}, player={surface.Bounds.Height}, columns={player.Bounds.Height}, status={vm.StatusText}");
        var count = vm.State.Tracks.Count; var savedVolume = vm.Settings.Volume; window.RestoreDefaultLayout();
        Require(vm.State.Tracks.Count == count && vm.Settings.Volume == savedVolume && window.LayoutConfiguration.HasBackup, "Restore layout preserves library/playback and backup");
        Console.WriteLine("PASS UI CONFIG: nested handwritten grid, validation, rollback, reload, red heart, play modes and vertical volume");
    }
    private static void VerifyTrackInteractions(MainWindow window, MainViewModel vm, SilentAudioPlayer fake, string output)
    {
        vm.Navigate("songs"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var slider = window.FindControl<Slider>("PlaybackSlider")!;
        vm.Seek(40); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var start = slider.TranslatePoint(((PlayerProgressSlider)slider).ThumbCenter, window)!.Value;
        var end = slider.TranslatePoint(new Point(slider.Bounds.Width * .65, slider.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, Avalonia.Input.MouseButton.Left); window.MouseMove(end, Avalonia.Input.RawInputModifiers.LeftMouseButton); window.MouseUp(end, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Require(!vm.IsSeeking && fake.Position.TotalSeconds > 150 && Math.Abs(vm.PlaybackPosition - fake.Position.TotalSeconds) < .01, "Real thumb drag commits backend seek");
        var position = fake.Position.TotalSeconds;
        slider.Focus(); window.KeyPress(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowRight, null); window.KeyRelease(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowRight, null); Dispatcher.UIThread.RunJobs();
        Require(!vm.IsSeeking && fake.Position.TotalSeconds > position, "Keyboard slider seek commits");
        var list = window.FindControl<ListBox>("TracksList")!; list.SelectedItems!.Clear();
        window.FindControl<Button>("BatchEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        list.ScrollIntoView(vm.VisibleTracks[0]); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var row = window.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("track-row") && g.DataContext == vm.VisibleTracks[0]);
        var check = row.Children.OfType<CheckBox>().Single();
        var point = check.TranslatePoint(new Point(10, check.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, Avalonia.Input.MouseButton.Left); window.MouseUp(point, Avalonia.Input.MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(list.SelectedItems.Contains(vm.VisibleTracks[0]) && check.IsChecked == true, "Row checkbox individually selects song");
        var wasPlaying = vm.IsPlaying; check.Focus();
        window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, null); window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, null); Dispatcher.UIThread.RunJobs();
        Require(check.IsChecked == false && list.SelectedItems.Count == 0 && vm.IsPlaying == wasPlaying, "Checkbox keyboard space selects without toggling playback");
        var all = window.FindControl<CheckBox>("SelectionToggle")!;
        all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(list.SelectedItems.Count == vm.VisibleTracks.Count && all.IsChecked == true, "Tri-state select all");
        all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(list.SelectedItems.Count == 0 && all.IsChecked == false, "Same checkbox cancels all selection");
        list.SelectedItems.Add(vm.VisibleTracks[0]); list.SelectedItems.Add(vm.VisibleTracks[1]);
        var batch = window.FindControl<Button>("BatchActionsButton")!; Require(batch.IsEnabled, "Batch enabled with selection"); batch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var batchMenu = batch.ContextMenu!;
        batchMenu.Items.OfType<MenuItem>().Single(m => m.Header?.ToString() == "添加到我喜欢").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(vm.LikedPlaylist.TrackIds.Contains(vm.VisibleTracks[0].Id) && vm.VisibleTracks[0].IsFavorite, "Batch favorite uses fixed playlist"); batchMenu.Close();
        var oldLiked = vm.LikedPlaylist.TrackIds.ToArray(); var dropped = vm.State.Tracks.Where(t => !t.IsFavorite).Take(2).Reverse().ToArray();
        typeof(MainWindow).GetMethod("AddDroppedTracks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, ["nonet-tracks:" + string.Join('\n', dropped.Select(t => t.Id)), true, vm.LikedPlaylist]);
        Require(vm.LikedPlaylist.TrackIds.Take(2).SequenceEqual(dropped.Select(t => t.Id)), "Internal drop preserves manual payload order");
        vm.UndoCommand.Execute(null); Require(vm.LikedPlaylist.TrackIds.SequenceEqual(oldLiked), "One drop onto liked requires exactly one undo");
        var playlist = vm.Playlists.First(p => !p.IsSystem); vm.UpdatePlaylistDetails(playlist, playlist.Name, "深夜听歌 · 自定义描述与封面", vm.CurrentTrack!.CoverPath);
        vm.AddToPlaylist(playlist, vm.State.Tracks); vm.Navigate("playlist:" + playlist.Id); Wait(vm.PlayTrackAsync(vm.VisibleTracks.Last()));
        WaitForPlayingRow(window, vm); Capture(window, output, "playlist-details-playing");
        var active = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("track-row") && g.DataContext is TrackItem t && t.IsPlayingHere);
        Require(active.Classes.Contains("playing"), "Source playlist visually highlights playing row");
        var activePoint = active.TranslatePoint(default, list)!.Value;
        Require(activePoint.Y >= 0 && activePoint.Y + active.Bounds.Height <= list.Bounds.Height + 2, "Track change automatically scrolls active song into viewport");
        vm.Navigate("favorites"); Dispatcher.UIThread.RunJobs(); Require(!vm.VisibleTracks.Any(t => t.IsPlayingHere), "Same song in other playlist not highlighted");
        vm.Navigate("playlist:" + playlist.Id); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        WaitForPlayingRow(window, vm);
        Require(vm.CurrentTrack.IsPlayingHere, "Entering source restores highlight");
        active = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("track-row") && g.DataContext is TrackItem t && t.IsPlayingHere);
        activePoint = active.TranslatePoint(default, list)!.Value;
        Require(activePoint.Y >= 0 && activePoint.Y + active.Bounds.Height <= list.Bounds.Height + 2, "Entering source automatically locates active song");
        var info = new SongInfoDialog(window, vm.State.Tracks.First(t => t.HasArtwork)); info.Show(window); Capture(info, output, "song-info");
        Require(info.GetVisualDescendants().OfType<Button>().Count(b => b.Content is string label && (label == "关闭" || label == "取消")) == 1, "Song info has only one close button");
        Require(info.GetVisualDescendants().OfType<Image>().Any(i => i.Source is not null) && info.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SongFilePath").IsEnabled, "Song cover and clickable path"); info.Close();
        var dismiss = window.FindControl<Border>("NotificationBar")!.GetVisualDescendants().OfType<Button>().Single(); dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 25 && window.FindControl<Border>("NotificationBar")!.IsVisible; i++) dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        vm.ReportWarning("回归测试警告：原始音乐文件均保留。"); Dispatcher.UIThread.RunJobs(); Require(window.FindControl<Border>("NotificationBar")!.IsVisible, "Warnings appear as dismissible popup"); Capture(window, output, "warning-popup");
        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!window.FindControl<Border>("NotificationBar")!.IsVisible, "Popup dismiss");
        vm.Navigate("songs"); Console.WriteLine("PASS INTERACTION: actual pointer/keyboard seek, checkboxes, batch favorite, source highlighting, playlist personalization, cover/path dialog and warning popup");
    }
    private static void WaitForPlayingRow(MainWindow window, MainViewModel vm)
    {
        var list = window.FindControl<ListBox>("TracksList")!; var elapsed = Stopwatch.StartNew();
        PumpUntil(() =>
        {
            window.UpdateLayout(); using var frame = window.CaptureRenderedFrame();
            var container = list.ContainerFromItem(vm.CurrentTrack!);
            var viewport = list.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().First();
            return elapsed.ElapsedMilliseconds >= 100 && container?.TranslatePoint(default, viewport) is { } p && p.Y >= 0 && p.Y + container.Bounds.Height <= viewport.Bounds.Height + 1;
        }, "Playing row remains inside actual viewport after multiple renders");
    }
    internal static T Wait<T>(Task<T> task) { PumpUntil(() => task.IsCompleted, "Async operation", 60); return task.GetAwaiter().GetResult(); }
    internal static void Wait(Task task) { PumpUntil(() => task.IsCompleted, "Async operation", 60); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> condition, string description, int seconds = 10)
    {
        var watch = Stopwatch.StartNew(); while (!condition()) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); if (watch.Elapsed.TotalSeconds > seconds) throw new TimeoutException(description); } Dispatcher.UIThread.RunJobs();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void WriteWave(string path, int seconds)
    {
        using var writer = new BinaryWriter(File.Create(path)); var size = 48000 * seconds * 4;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(size + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Write(new byte[size]);
    }
    internal sealed class SilentAudioPlayer : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; private set; } public float Volume { get; set; }
        public event EventHandler? PlaybackStopped;
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) { Duration = TimeSpan.FromSeconds(263); return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; }
        public void End() { IsPlaying = false; PlaybackStopped?.Invoke(this, EventArgs.Empty); } public void Dispose() { }
    }
}
