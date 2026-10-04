using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class InputTrayAndPluginConfigurationChecks
{
    public static void Run(MainWindow window, MainViewModel vm, string output)
    {
        Console.WriteLine("BETA9 inputs"); Inputs(window, vm);
        Console.WriteLine("BETA9 song info"); SongInfo(window, vm);
        Console.WriteLine("BETA9 lyrics"); Lyrics(window, vm, output);
        Console.WriteLine("BETA9 statistics"); Statistics(window, vm, output);
        Console.WriteLine("BETA9 plugin configuration"); Plugins(window, vm, output);
        Console.WriteLine("BETA9 remote mock"); Remote(vm.Storage, output);
        Console.WriteLine("BETA9 minimize"); Minimize(window, vm);
        Console.WriteLine("PASS BETA9: selectable metadata/path, deferred text/numeric/search, centered title, independent/locked/resizable/static desktop lyrics, blank filtering, calendar-linked day/month/year summaries, minimize viewport retention, schema overlay and mocked GitHub release downloads");
    }
    private static void Inputs(MainWindow window, MainViewModel vm)
    {
        Require(new AppSettings().CloseToTray && new AppSettings().OptimizeMemoryWhenMinimized, "Tray close and minimize optimization default on");
        vm.Navigate("songs"); Pump(window);
        var search = window.FindControl<TextBox>("LibrarySearch")!; search.Focus(); search.Text = "unmatched-draft"; Pump(window);
        Require(vm.SearchText == "" && vm.VisibleTracks.Count > 0, "Typing search does not apply before commit");
        Enter(window); Require(vm.SearchText == "unmatched-draft" && vm.VisibleTracks.Count == 0 && !search.IsFocused, "Enter searches and removes focus");
        search.Focus(); search.Text = ""; var player = window.FindControl<Button>("PlayerMore")!; Click(window, player); Pump(window);
        Require(vm.SearchText == "" && !search.IsFocused && vm.VisibleTracks.Count > 0, "Clicking elsewhere commits search and removes focus");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); vm.Navigate("settings"); Pump(window);
        // Earlier groups intentionally leave the cached settings page filtered/scrolled.
        var resetSearch = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SettingsSearch");
        resetSearch.Focus(); resetSearch.Text = ""; Enter(window); Pump(window);
        var accent = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AccentHex"); var original = vm.Settings.Accent;
        accent.BringIntoView(); Pump(window);
        accent.Focus(); accent.Text = "#2299CC"; Pump(window); Require(vm.Settings.Accent == original, "Accent is draft while typing"); Enter(window);
        Require(vm.Settings.Accent == "#2299CC" && !accent.IsFocused, $"Valid accent commits on Enter (actual={vm.Settings.Accent}, focused={accent.IsFocused})");
        accent.Focus(); accent.Text = "invalid"; Enter(window); Require(vm.Settings.Accent == "#2299CC" && !accent.IsFocused, "Invalid accent rejected but unfocused");
        var settingsSearch = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SettingsSearch"); settingsSearch.Focus(); settingsSearch.Text = "最近播放"; Enter(window); Pump(window);
        var limit = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "HistoryLimit");
        var input = limit.GetVisualDescendants().OfType<TextBox>().First(); Require(input.TextAlignment == TextAlignment.Center && input.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Center, "History numeric text centered in both axes");
        input.Focus(); input.Text = "42"; Pump(window); Require(vm.Settings.HistoryLimit == 1000, "History numeric value remains draft"); Enter(window); Require(vm.Settings.HistoryLimit == 42 && !input.IsFocused, "History value applies on Enter");
        input.Focus(); input.Text = "-5"; Enter(window); Require(vm.Settings.HistoryLimit == 42 && !input.IsFocused, "Invalid history value rejected without keeping focus");
        settingsSearch.Focus(); settingsSearch.Text = ""; Enter(window); Pump(window);
        Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "背景图片"), "Background setting has heading");
        vm.Settings.TitleButtonsOnLeft = true; vm.ApplySettings(); Pump(window);
        var brand = window.FindControl<StackPanel>("TitleBrand")!; var point = brand.TranslatePoint(default, window)!.Value;
        Require(Math.Abs(point.X + brand.Bounds.Width / 2 - window.Bounds.Width / 2) < 1, "Brand centered when window buttons on left");
        var buttons = window.FindControl<StackPanel>("TitleButtons")!; Require(buttons.Children[0].Name == "CloseWindowButton" && buttons.Children[2].Name == "MaximizeWindowButton", "Mac button order retains color actions");
        vm.Settings.TitleButtonsOnLeft = false; vm.Settings.HistoryLimit = 1000; vm.ApplySettings();
    }
    private static void SongInfo(MainWindow owner, MainViewModel vm)
    {
        var track = vm.State.Tracks.First(); var dialog = new SongInfoDialog(owner, track); dialog.Show(); Pump(dialog);
        try
        {
            Require(dialog.GetVisualDescendants().OfType<SelectableTextBlock>().Count() >= 7, "Metadata is selectable read-only text");
            var path = Find<SelectableTextBlock>(dialog, "SongFilePathText"); Require(path.Text == track.FilePath, "Path is separately selectable and copyable");
            path.SelectionStart = 0; path.SelectionEnd = path.Text!.Length; Require(path.SelectedText == track.FilePath, "Path full selection includes original characters");
            Require(Find<Button>(dialog, "SongFilePath").IsEnabled, "Separate reveal-path action retained");
        }
        finally { dialog.Close(); }
    }
    private static void Lyrics(MainWindow owner, MainViewModel vm, string output)
    {
        var parsed = LyricsService.Parse("[00:00]\n[00:01.00]原文\n[00:01:00]译文\n[00:02]  \n[00:03.000]第二句\n[00:04]\n");
        Require(parsed.Count == 2 && parsed.All(l => !string.IsNullOrWhiteSpace(l.Text)) && parsed[0].Translation == "译文", "Blank timed rows filtered and translation grouped");
        vm.LyricLines.Clear(); foreach (var line in parsed) vm.LyricLines.Add(line);
        using var lyrics = new DesktopLyricsWindow(owner, vm); lyrics.Toggle(); Pump(lyrics);
        Require(lyrics.Owner is null && lyrics.IsVisible, "Desktop lyrics shown as independent non-owned window");
        owner.WindowState = WindowState.Minimized; Pump(owner); Require(lyrics.IsVisible && lyrics.WindowState != WindowState.Minimized, "Main minimize does not minimize lyrics"); owner.WindowState = WindowState.Normal; Pump(owner);
        var current = lyrics.GetVisualDescendants().OfType<KaraokeLine>().First(l => l.Name == "DesktopCurrentLyric");
        Require(current.Progress == 0 && current.Text == "原文", "No progressive karaoke fill");
        var location = current.TranslatePoint(current.TextBounds.Center, lyrics)!.Value; lyrics.MouseMove(location); Pump(lyrics); Require(lyrics.FrameVisible, "Only lyric hover reveals frame");
        var w = lyrics.Width; var h = lyrics.Height;
        foreach (var (name, delta) in new[] { ("Right", new Vector(70, 0)), ("Bottom", new Vector(0, 40)) })
        {
            var edge = Find<Border>(lyrics, "DesktopLyricsResize" + name); var p = edge.TranslatePoint(new Point(edge.Bounds.Width / 2, edge.Bounds.Height / 2), lyrics)!.Value;
            lyrics.MouseDown(p, MouseButton.Left); lyrics.MouseMove(p + delta, RawInputModifiers.LeftMouseButton); lyrics.MouseUp(p + delta, MouseButton.Left); Pump(lyrics);
        }
        Require(lyrics.Width >= w + 60 && lyrics.Height >= h + 30, "Lyrics width and height resize together with pointer");
        lyrics.LockLyrics(); var position = lyrics.Position; Pump(lyrics); Require(lyrics.IsLocked && !lyrics.FrameVisible, "Lock hides frame");
        lyrics.HideLyrics(); Require(lyrics.IsVisible, "Locked lyrics cannot close");
        lyrics.MouseDown(location, MouseButton.Left); lyrics.MouseMove(location + new Vector(50, 20), RawInputModifiers.LeftMouseButton); lyrics.MouseUp(location, MouseButton.Left); Require(lyrics.Position == position, "Locked lyrics do not move");
        lyrics.Toggle(); Require(!lyrics.IsLocked && lyrics.IsVisible, "Main toggle unlocks without hiding"); lyrics.HideLyrics(); Require(!lyrics.IsVisible, "Unlocked lyrics can hide");
        Require(vm.Settings.DesktopLyricsHeight >= h + 30, "Lyrics height persisted");
    }
    private static void Statistics(MainWindow window, MainViewModel vm, string output)
    {
        vm.State.ListeningEntries.Clear();
        vm.State.ListeningEntries.AddRange([
            new() { Date = "2026-10-02", TrackId = "a", Title = "时间最长", Artist = "甲", Seconds = 100, PlayCount = 1 },
            new() { Date = "2026-10-02", TrackId = "b", Title = "次数最多", Artist = "乙", Seconds = 20, PlayCount = 4 },
            new() { Date = "2026-10-01", TrackId = "a", Title = "时间最长", Artist = "甲", Seconds = 60, PlayCount = 2 },
            new() { Date = "2026-09-01", TrackId = "c", Title = "上月", Artist = "丙", Seconds = 200, PlayCount = 5 },
            new() { Date = "2025-10-02", TrackId = "d", Title = "去年", Artist = "丁", Seconds = 500, PlayCount = 6 }
        ]);
        var date = new DateOnly(2026, 10, 2); Require(vm.GetStatistics(date, "day").TotalSeconds == 120 && vm.GetStatistics(date, "month").TotalSeconds == 180 && vm.GetStatistics(date, "year").TotalSeconds == 380, "Day/month/year period bounds");
        vm.Navigate("statistics"); Pump(window); var view = window.GetVisualDescendants().OfType<StatisticsView>().Single(); view.DateSelector.SelectedDate = date.ToDateTime(TimeOnly.MinValue); Pump(window);
        Require(view.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("Statistics_") == true) == 3, "Three periods share calendar control");
        Require(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("次数最多") == true), "Most played differs from longest listened");
        view.DateSelector.SelectedDate = new DateTime(2025, 10, 2); Pump(window); Require(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "去年 — 丁"), "Calendar selection updates all periods");
        using var screenshot = window.CaptureRenderedFrame()!; screenshot.Save(Path.Combine(output, "beta9-statistics.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    private static void Plugins(MainWindow window, MainViewModel vm, string output)
    {
        var stage = Path.Combine(output, "schema-plugin"); Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, "manifest.json"), "{\"id\":\"beta9.config-test\",\"name\":\"配置测试\",\"type\":\"widget\",\"version\":\"1.0.0\",\"contractVersion\":1}");
        var schema = """{"enabled":{"description":"启用功能","type":"bool","default":true},"mode":{"description":"模式","type":"string","enum":[{"value":"a","label":"模式 A"},{"value":"b","label":"模式 B"}],"default":"a"},"count":{"description":"次数","type":"int","default":60,"min":0,"max":200},"names":{"description":"名字列表","type":"list","items":{"type":"string"},"default":[]},"extra":{"description":"字典","type":"object","default":{},"items":{}},"password":{"description":"会话密码","type":"string","default":"","ui:widget":"password"}}""";
        File.WriteAllText(Path.Combine(stage, PluginConfigSchema.FileName), schema); var package = Path.Combine(output, "schema-test.impp"); if (!File.Exists(package)) ZipFile.CreateFromDirectory(stage, package);
        var manifest = vm.Plugins.Install(package); vm.Navigate("plugins"); Pump(window);
        var task = window.OpenPluginConfigurationAsync(manifest); Pump(window); Require(window.HasPluginConfigurationOverlay, "Configuration is an in-app overlay");
        var view = window.GetVisualDescendants().OfType<PluginConfigView>().Single();
        Require(view.GetVisualDescendants().OfType<ToggleSwitch>().Count() == 1 && view.GetVisualDescendants().OfType<ComboBox>().Count() == 1, "Schema generates toggle and enum controls");
        var count = Find<TextBox>(view, "PluginConfig_count"); count.Focus(); count.Text = "75"; Enter(window); Require(view.Draft["count"]!.GetValue<int>() == 75 && manifest.Configuration == "{}", "Editing updates draft only");
        var password = Find<TextBox>(view, "PluginConfig_password"); password.Focus(); password.Text = "test-secret"; Enter(window); view.Save(); Pump(window);
        Require(task.IsCompletedSuccessfully && task.Result && !window.HasPluginConfigurationOverlay && manifest.Configuration.Contains("75") && !manifest.Configuration.Contains("test-secret"), "Save persists ordinary config and scrubs session secrets");
        window.OpenPluginConfigurationAsync(manifest); Pump(window); view = window.GetVisualDescendants().OfType<PluginConfigView>().Single(); var close = Find<Button>(view, "ClosePluginConfiguration"); close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window); Require(!window.HasPluginConfigurationOverlay, "Close discards overlay draft");
        window.OpenPluginConfigurationAsync(manifest); Pump(window); using(var screenshot = window.CaptureRenderedFrame()!) screenshot.Save(Path.Combine(output, "beta9-plugin-config.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Find<Button>(window.GetVisualDescendants().OfType<PluginConfigView>().Single(), "ClosePluginConfiguration").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // 可选外部夹具由运行环境提供，公开源码不包含开发者个人目录。
        var supplied = Environment.GetEnvironmentVariable("NONET_TEST_PLUGIN_SCHEMA");
        if (File.Exists(supplied)) { using var stream = File.OpenRead(supplied); var originalSchema = PluginConfigSchema.Read(stream); Require(originalSchema.Count > 20, "Provided AstrBot sample shape accepted without executing its content"); PluginConfigSchema.Validate(originalSchema, PluginConfigSchema.Defaults(originalSchema)); }
        vm.Plugins.Uninstall(manifest);
    }
    private static void Remote(AppStorage storage, string output)
    {
        var bytes = File.ReadAllBytes(Path.Combine(output, "schema-test.impp")); var handler = new FakeGitHub(bytes); using var downloader = new PluginReleaseDownloader(handler);
        var assets = downloader.ResolveAsync("https://github.com/test/repo/releases/tag/v1.0.0").GetAwaiter().GetResult(); Require(assets.Count == 1 && handler.Requests == 1, "Mock API resolves .impp and ignores source assets");
        string path; using (var package = downloader.DownloadAsync(assets[0], storage.PluginsFolder).GetAwaiter().GetResult()) { path = package.Path; Require(PluginManager.Inspect(path).Id == "beta9.config-test", "Mock downloaded package inspected safely"); }
        Require(!File.Exists(path), "Download staging removed after use");
        Throws(() => downloader.ResolveAsync("https://example.com/release").GetAwaiter().GetResult(), "Non-GitHub links blocked");
        Throws(() => downloader.DownloadAsync(assets[0] with { Digest = "sha256:" + new string('0', 64) }, storage.PluginsFolder).GetAwaiter().GetResult(), "Wrong hash rejected");
        Throws(() => downloader.DownloadAsync(assets[0] with { DownloadUrl = new Uri("https://127.0.0.1/a.impp") }, storage.PluginsFolder).GetAwaiter().GetResult(), "Loopback download rejected");
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); Throws(() => downloader.DownloadAsync(assets[0], storage.PluginsFolder, cancellationToken: cancel.Token).GetAwaiter().GetResult(), "Canceled download rejected");
        Require(Directory.GetFiles(Path.Combine(storage.PluginsFolder, ".downloads")).Length == 0, "Failed and canceled downloads leave no partial package");
    }
    private static void Minimize(MainWindow window, MainViewModel vm)
    {
        vm.Navigate("settings"); Pump(window); vm.Settings.OptimizeMemoryWhenMinimized = true;
        var page = (SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!;
        var viewer = page.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
        viewer.Offset = new Vector(0, 400); Pump(window); var offset = viewer.Offset;
        // 内存整理不得卸载活动页面；旧的缓存清空预期会重新引入滚动位置丢失。
        var artworkCount = ArtworkCache.Shared.Snapshot.Count;
        window.WindowState = WindowState.Minimized; Pump(window);
        Require(ReferenceEquals(window.FindControl<ContentControl>("AlternatePage")!.Content, page) && ArtworkCache.Shared.Snapshot.Count == artworkCount, "Minimize retains active page and artwork");
        window.WindowState = WindowState.Normal; Pump(window);
        Require(ReferenceEquals(window.FindControl<ContentControl>("AlternatePage")!.Content, page) && viewer.Offset == offset, "Restore retains settings instance and viewport");
    }
    private sealed class FakeGitHub(byte[] bytes) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests++;
            if (request.RequestUri!.Host == "api.github.com")
            {
                var metadata = JsonSerializer.Serialize(new { assets = new[] { new { name = "test.impp", browser_download_url = "https://github.com/test/repo/releases/download/v1.0.0/test.impp", size = bytes.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)) }, new { name = "source.zip", browser_download_url = "https://github.com/test/repo/archive/source.zip", size = 50, digest = "" } } });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadata) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    private static void Click(Window window, Control control) { var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value; window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); }
    private static void Enter(Window window) { window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Beta9 check: " + message); }
    private static void Throws(Action action, string message) { try { action(); } catch { return; } throw new InvalidOperationException("Expected rejection: " + message); }
}
