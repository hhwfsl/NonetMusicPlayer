using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>用真实指针和进程/托管夹具复现配置重启、方案重挂接与页面操作，不访问模型接口。</summary>
internal static class DesktopInteractionChecks
{
    public static void Run(string output, string? preset)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new AppStorage(root); storage.Save(new AppState { LastSourcePage = "cloud-search" });
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        Check(vm.Page == "library", "A restored cloud queue must not choose the startup page.");
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        for (var i = 0; i < 120; i++) vm.State.Tracks.Add(new("track-" + i, "Music " + i, "Artist " + i % 8, "Album " + i % 6, "fixture-" + i + ".flac", ".flac", 0) { DurationSeconds = 185 });
        vm.LikedPlaylist.TrackIds.AddRange(vm.State.Tracks.Select(t => t.Id)); vm.State.History.AddRange(vm.State.Tracks.Take(5).Select(t => t.Id)); vm.CurrentTrack = vm.State.Tracks[0]; vm.ApplyFilter();
        foreach (var name in new[] { "Evening", "Morning", "Favourites mix", "Quiet piano", "Night drive" }) vm.CreatePlaylist(name).TrackIds.AddRange(vm.State.Tracks.Take(8).Select(t => t.Id));
        var currentHour = DateTimeOffset.Now.ToUnixTimeSeconds() / 3600;
        foreach (var (offset, count) in new[] { (6, 3), (5, 5), (2, 2) }) vm.State.HourlyListeningEntries.Add(new() { UtcHour = currentHour - offset, PlayCount = count, Seconds = count * 60 });
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = vm }; window.Show(); Pump();
        CaptureHome("default");
        CheckPlaylistOverview(false);
        if (preset is not null) { using var input = File.OpenRead(preset); window.ApplyInterfaceLayers("fixture.scheme", [StyleSheetContract.Read(input)]); Pump(); }
        CaptureHome("preset");
        CheckPlaylistOverview(preset is not null);
        foreach (var size in new[] { new Size(640, 480), new Size(900, 620), new Size(1280, 854), new Size(1600, 1000) }) { window.Width = size.Width; window.Height = size.Height; Pump(); CaptureHome($"home-{size.Width}x{size.Height}"); }
        window.Width = 1280; window.Height = 800; Pump();
        vm.Navigate("terminal"); Pump(); var terminal = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "TerminalOutput");
        var background = terminal.Background; window.MouseMove(terminal.TranslatePoint(new(40, 40), window)!.Value); Pump();
        Check(terminal.Background is ISolidColorBrush { Color.A: 0 } && background is ISolidColorBrush { Color.A: 0 }, $"Terminal pointer-over must not alter its background: {background} -> {terminal.Background}.");
        foreach (var managed in new[] { false, true }) CheckConfiguration(managed);
        CheckLegacyPage();
        CheckDropHints();
        window.Close(); Pump();
        Console.WriteLine("PASS startup home, bounded dashboard, terminal hover, live process/managed reconfiguration, fault/re-enable actions, legacy page hit testing and stable player geometry.");

        void CaptureHome(string name)
        {
            vm.Navigate("library"); Pump(); var home = window.GetVisualDescendants().OfType<Grid>().Single(c => c.Name == "MusicHome");
            Check(!home.GetVisualDescendants().OfType<ScrollViewer>().Any(), "The overview must not create a scrolling page.");
            Check(home.Bounds.Width > 0 && home.Bounds.Height > 0 && home.GetVisualDescendants().OfType<Border>().Where(c => c.Classes.Contains("overview-card") && c.IsEffectivelyVisible).All(c => c.Bounds.Height > 60), "Overview cards fit the current viewport.");
            var chart = home.GetVisualDescendants().OfType<ListeningBarChart>().Single(c => c.Name == "HomeHourChart");
            Check(chart.Duration && chart.Points.Count == 24 && chart.Points.Sum(p => p.Value) == 600, "Home displays actual seconds across 24 hourly buckets rather than play counts.");
            var covers = home.GetVisualDescendants().OfType<Border>().Where(c => c.Classes.Contains("overview-playlist-cover") && c.IsEffectivelyVisible).ToArray();
            Check(covers.Length is >= 1 and <= 4 && covers.All(c => c.Bounds.Width > 0 && Math.Abs(c.Bounds.Width - c.Bounds.Height) < .1), "Every home playlist cover is square at each viewport.");
            Check(home.GetVisualDescendants().OfType<Border>().Where(c => c.Classes.Contains("overview-card")).All(c => c.BorderThickness == new Thickness(0)), "Overview sections use filled surfaces without outlines.");
            Check(window.FindControl<Border>("WindowSurface")!.BorderThickness == new Thickness(0), "Window surfaces stay borderless before entering a lyrics page.");
            using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(Path.GetFullPath(output), name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        void CheckPlaylistOverview(bool styled)
        {
            var preview = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "HomePlaylists");
            var all = preview.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "HomeSectionMore");
            Click(all);
            Check(vm.Page == (styled ? "interface:playlists" : "playlists"), "View All navigates to the complete playlist page.");
            var gallery = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "InterfacePlaylistGallery");
            var scroll = gallery.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single();
            // 使用真实指针进入预览之外的歌单，验证完整集合与原有导航行为。
            scroll.Offset = new(0, 1000); Pump();
            var item = gallery.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "InterfacePlaylistCard" && c.Tag?.ToString() == "playlist:" + vm.Playlists.Last().Id);
            Click(item); Check(vm.Page == "playlist:" + vm.Playlists.Last().Id, "The complete gallery opens playlists beyond the home preview.");
            vm.Navigate("library"); Pump();
        }
        void CheckConfiguration(bool managed)
        {
            var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0"));
            var manifest = new PluginManifest { Id = "fixture.live-" + (managed ? "managed" : "process"), Name = "Configuration fixture " + (managed ? "managed" : "process"), Version = "1.0.0", Type = "extension", ContractVersion = 2, Runtime = managed ? "managed" : "process", ExtensionClass = "BaselineExtension", Permissions = [managed ? "in-process" : "process"], PageEntry = "page.json", EntryPoints = new() { [PluginPlatformPolicy.CurrentRid] = "bin/NonetMusicPlayer.ExtensionFixture." + (managed ? "dll" : "exe") } };
            var package = Path.Combine(root, manifest.Id + ".impp");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                Write(zip, "manifest.json", JsonSerializer.Serialize(manifest, AppStorage.Json));
                Write(zip, "page.json", """{"schemaVersion":2,"root":{"type":"grid","rows":"Auto,Auto","children":[{"type":"input","id":"FixtureDraft","input":"text"},{"type":"button","id":"FixtureIncrement","row":1,"text":"Increment","action":"increment"}]}}""");
                Write(zip, "plugin_config_schema.json", """{"label":{"type":"string","default":"initial","description":"Fixture value"}}""");
                foreach (var file in Directory.GetFiles(fixture).Where(f => !f.EndsWith(".pdb"))) zip.CreateEntryFromFile(file, "bin/" + Path.GetFileName(file));
            }
            var plugin = vm.Plugins.Install(package); plugin.ManagedExecutionConsent = true; vm.Plugins.SetEnabled(plugin, true);
            vm.Navigate("plugin:" + plugin.Id); Pump(); var session = vm.Plugins.Extension(plugin); Wait(session.StartAsync());
            var configure = session.InvokeAsync("configure", new(), true); Until(() => window.HasPluginConfigurationOverlay);
            var form = window.GetVisualDescendants().OfType<PluginConfigView>().Single(); form.Draft["label"] = "saved while open"; form.Save();
            Until(() => configure.IsCompleted); try { configure.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            Pump(); Check(plugin.Enabled && session.IsDisposed, "Configuration replaces the old session without fault-disabling the plugin.");
            var replacement = vm.Plugins.Extension(plugin); Wait(replacement.StartAsync());
            Check(!ReferenceEquals(session, replacement) && vm.Plugins.ConfigurationValues(plugin)["label"]!.GetValue<string>() == "saved while open", "Saved configuration survives session replacement.");
            var increment = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FixtureIncrement"); var count = replacement.Frame.State["count"]!.GetValue<int>(); Click(increment);
            Until(() => replacement.Frame.State["count"]!.GetValue<int>() == count + 1);
            try { Wait(replacement.InvokeAsync("crash", new())); } catch (Exception) { }
            Until(() => !plugin.Enabled); vm.Navigate("plugins"); Pump();
            var center = window.GetVisualDescendants().OfType<PluginsView>().Single();
            var toggle = center.GetVisualDescendants().OfType<ToggleSwitch>().Single(c => c.Name == "PluginEnabled" && c.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Parent is StackPanel)?.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == plugin.Name) == true);
            var actions = (Grid)toggle.Parent!; var open = actions.Children.OfType<Button>().Single(b => b.Name == "PluginOpenPage");
            Check(!open.IsEnabled, "Fault disables the page action."); toggle.IsChecked = true; Pump(); Check(open.IsEnabled, "Re-enable immediately restores the page action.");
            open.RaiseEvent(new(Button.ClickEvent)); Pump(); Check(vm.Page == "plugin:" + plugin.Id, "Re-enabled page opens without re-entering the manager."); vm.Plugins.SetEnabled(plugin, false);
        }
        void CheckLegacyPage()
        {
            var package = Path.Combine(root, "legacy-lyrics.impp");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                Write(zip, "manifest.json", """{"id":"fixture.legacy-lyrics","name":"Lyrics fixture","version":"1.0.0","type":"lyrics","permissions":["network","process","lyrics-search"],"pageEntry":"page.json","entryPoints":{"win-x64":"worker.exe"}}""");
                Write(zip, "page.json", """{"schemaVersion":1,"title":"Lyrics","widgets":[{"type":"lyrics-search"}]}"""); Write(zip, "worker.exe", "Never executed: interaction-only fixture.");
            }
            var plugin = vm.Plugins.Install(package); vm.Plugins.SetEnabled(plugin, true); vm.Navigate("plugin:" + plugin.Id); Pump();
            var page = window.GetVisualDescendants().OfType<PluginPageView>().Single(); Check(!page.IsDisposed && page.IsEffectivelyEnabled, "Interface composition must retain a live legacy page.");
            var query = page.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "LyricsSearchQuery"); Click(query);
            using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(Path.GetFullPath(output), "lyrics-plugin.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            var point = query.TranslatePoint(new(30, 16), window)!.Value;
            Check(query.IsKeyboardFocusWithin, $"A real pointer focuses the legacy query field: {query.Name} {query.Bounds} at {point}; hit {window.InputHitTest(point)}; focused {window.FocusManager?.GetFocusedElement()}.");
            var widgets = new StackPanel(); var snake = new SnakeGameControl(new()); var pet = new PluginPetControl(new()); var timing = new LyricsTimingControl(plugin.Id);
            var coverPath = Path.Combine(root, "original-cover.png"); using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(32, 32))) bitmap.Save(coverPath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            var original = new OriginalArtworkImage(coverPath, null); widgets.Children.Add(snake); widgets.Children.Add(pet); widgets.Children.Add(timing); widgets.Children.Add(original);
            var body = (Panel)((ScrollViewer)page.Content!).Content!; body.Children.Add(widgets); Until(() => original.Source is not null); var source = original.Source;
            for (var i = 0; i < 3; i++) { body.Children.Remove(widgets); body.Children.Add(widgets); Pump(); }
            timing.SelectTrack(vm.State.Tracks[7]);
            Check(snake.IsEffectivelyEnabled && !pet.IsDisposed && timing.SelectedTrack == vm.State.Tracks[7] && ReferenceEquals(original.Source, source), "Other legacy widgets and original artwork retain live state during reparenting.");
            body.Children.Remove(widgets); Pump(); Check(!snake.IsEnabled && pet.IsDisposed && original.Source is null, "True removal still releases timers and original pixels.");
            var player = window.FindControl<Border>("PlayerSurface")!; var before = player.Bounds.Size; var minimum = before.Height;
            var surface = window.FindControl<Border>("WindowSurface")!; var shell = (Control)surface.Child!; var shellSize = shell.Bounds.Size;
            var frameChanged = false;
            var controls = new[] { "PlayerColumns", "PlayerPrevious", "PlayerPlay", "PlayerNext", "PlayerArtwork", "PlayerVolume" }.Select(name => window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name)).ToDictionary(c => c, c => c.Bounds.Size);
            var shrunk = new HashSet<string>();
            void Layout(object? sender, EventArgs e)
            {
                if (player.Bounds.Height > 0) minimum = Math.Min(minimum, player.Bounds.Height);
                frameChanged |= surface.BorderThickness != new Thickness(0) || Math.Abs(shell.Bounds.Width - shellSize.Width) > .1 || Math.Abs(shell.Bounds.Height - shellSize.Height) > .1;
                foreach (var (control, size) in controls) if (control.IsAttachedToVisualTree() && control.Bounds.Width > 0 && (control.Bounds.Width < size.Width - 1 || control.Bounds.Height < size.Height - 1)) shrunk.Add(control.Name!);
            }
            window.LayoutUpdated += Layout; Click(window.FindControl<Button>("PlayerArtwork")!); Pump(100);
            Check(vm.Page == "lyrics" && Math.Abs(player.Bounds.Height - before.Height) < 1 && minimum >= before.Height - 1, $"Player geometry changed during lyrics transition: {before.Height} -> {minimum}/{player.Bounds.Height}.");
            Click(window.FindControl<Button>("PlayerArtwork")!); Pump(100); window.LayoutUpdated -= Layout;
            Check(shrunk.Count == 0, "Player controls shrink during lyrics navigation: " + string.Join(", ", shrunk));
            Check(!frameChanged, "Lyrics navigation changes the outer-frame inset or shell dimensions.");
            Check(!page.IsEffectivelyEnabled || !ReferenceEquals(page, window.GetVisualDescendants().OfType<PluginPageView>().FirstOrDefault()), "Leaving a legacy page disposes only the old page.");
            Check(window.GetVisualDescendants().OfType<PluginPageView>().Single().IsEffectivelyEnabled, "Returning from lyrics creates an interactive page.");
        }
        void CheckDropHints()
        {
            vm.Navigate("playlist:" + Playlist.LikedId); Pump(); var player = window.FindControl<Border>("PlayerSurface")!;
            var transfer = new DataTransfer(); transfer.Add(DataTransferItem.CreateText("nonet-tracks:track-1"));
            DragOver(player, transfer); Check(Hint().Contains("Music 1"), "Known track drag shows the exact play target: " + Hint());
            var file = Path.Combine(root, "dragged song.flac"); File.WriteAllBytes(file, [1, 2, 3]);
            var lookup = window.StorageProvider.TryGetFileFromPathAsync(file); Wait(lookup);
            using var storageFile = lookup.Result ?? throw new InvalidOperationException("Fixture file lookup failed.");
            var files = new DataTransfer(); files.Add(DataTransferItem.CreateFile(storageFile));
            DragOver(player, files); Check(Hint() == L10n.Format("Home.DropPlay", "dragged song.flac"), "External player drag explains temporary playback.");
            DragOver(window.FindControl<Control>("TracksList")!, files); Check(Hint().Contains(vm.LikedPlaylist.Name) || Hint().Contains(L10n.T("Playlists.LikedSongs")), "Playlist drag names the receiving playlist.");
            using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(Path.GetFullPath(output), "drop-preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            var leave = new DragEventArgs(DragDrop.DragLeaveEvent, files, window, new(-1, -1), KeyModifiers.None) { Source = window }; window.RaiseEvent(leave); Pump();
            Check(!window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DropPreviewSurface").IsVisible, "Leaving clears the hint without modifying music.");
            void DragOver(Control target, IDataTransfer data) { target.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, data, target, new(10, 10), KeyModifiers.None) { Source = target }); Pump(); }
            string Hint() => string.Join(" ", window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DropPreviewSurface").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        void Click(Control control) { var point = control.TranslatePoint(new(Math.Min(control.Bounds.Width / 2, 40), Math.Min(control.Bounds.Height / 2, 16)), window)!.Value; window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(); }
    }
    private static void Write(ZipArchive zip, string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> ready) { var end = Environment.TickCount64 + 10000; while (!ready() && Environment.TickCount64 < end) Pump(2); Check(ready(), "Timed out waiting for UI transaction."); }
    private static void Pump(int frames = 25) { for (var i = 0; i < frames; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(3); } }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } } public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
