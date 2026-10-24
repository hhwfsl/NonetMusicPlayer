using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>完整方案测试仅使用生成数据；外部方案由显式参数传入，不依赖私有项目。</summary>
internal static class InterfaceCompositionChecks
{
    public static void Run(string output, string? presetFile)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Directory.CreateDirectory(output);
        VerifyLeases();
        var storage = new AppStorage(Path.Combine(Path.GetFullPath(output), "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false; vm.Settings.NavigationRight = true; vm.Settings.PlayerTop = true; vm.Settings.SidebarWidth = 260; vm.Settings.PlayerHeight = 132;
        var window = new MainWindow { DataContext = vm, Width = 1360, Height = 860 }; window.Show(); Pump();
        var errors = new List<string>(); vm.UserNotification += (_, notice) => errors.Add(notice.Message);
        var original = JsonSerializer.Serialize(vm.Settings, AppStorage.Json);
        var simple = new StyleSheet { Id = "fixture.composition", Name = "Fixture", Layout = new() { Settings = new() { ["navigationRight"] = false, ["playerTop"] = false, ["sidebarWidth"] = 208d, ["playerHeight"] = 96d }, Regions = new() { ["SidebarPanel"] = new() { Kind = "stack", Children = [new() { Kind = "navigate", Page = "library", Label = "Home.Music", Icon = "Home" }] } } } };
        window.ApplyInterfaceLayers("fixture.owner", [simple]); Pump();
        Check(!window.InterfaceSettings.NavigationRight && !window.InterfaceSettings.PlayerTop && window.InterfaceSettings.SidebarWidth == 208, "Visual settings are overlaid at runtime.");
        var upper = new StyleSheet { Id = "fixture.upper", Name = "Upper", Layout = new() { Settings = new() { ["playerTop"] = true }, Motion = new() { PageDurationMs = 100 } } };
        simple.Layout!.Motion = new() { LyricsDurationMs = 300, LyricsCornerRadius = 20 };
        window.ApplyInterfaceLayers("fixture.owner", [simple, upper]); Pump();
        Check(window.InterfaceSettings.PlayerTop && !window.InterfaceSettings.NavigationRight && window.InterfaceMotionProfile is { LyricsDurationMs: 300, LyricsCornerRadius: 20, PageDurationMs: 100 }, "Upper layers override only supplied setting and motion fields.");
        window.ApplyInterfaceLayers("fixture.owner", [simple]); Pump();
        var forbidden = new StyleSheet { Id = "fixture.forbidden", Name = "Forbidden", Layout = new() { Settings = new() { ["closeToTray"] = false } } };
        try { window.ApplyInterfaceLayers("fixture.owner", [forbidden]); throw new Exception("Nonvisual setting accepted."); } catch (InvalidDataException) { }
        Check(!window.InterfaceSettings.NavigationRight, "A rejected scheme preserves prior visual ownership.");
        Check(JsonSerializer.Serialize(vm.Settings, AppStorage.Json) == original, "Applying a layout never modifies saved settings.");
        vm.Navigate("settings"); Pump();
        var navigation = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "NavigationPosition");
        Check(!navigation.IsEffectivelyEnabled, "An owned setting editor is disabled.");
        window.ApplyInterfaceLayers("fixture.owner", []); Pump();
        Check(window.InterfaceSettings.NavigationRight && window.InterfaceSettings.PlayerTop && JsonSerializer.Serialize(vm.Settings, AppStorage.Json) == original, "Disabling restores original preferences without writing them.");
        Check(window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "NavigationPosition").IsEffectivelyEnabled, "Disabling re-enables the original setting editor.");
        // 延迟构建页面的错误不能绕过插件故障边界并终止主程序。
        var broken = new StyleSheet { Id = "fixture.broken", Name = "Broken", Layout = new() { Pages = new() { ["broken"] = new() { Kind = "text", Label = "Invalid", Properties = new() { ["width"] = "invalid-number" } } } } };
        window.ApplyInterfaceLayers("fixture.broken-owner", [broken]); vm.Navigate("interface:broken"); Pump();
        Check(errors.Count == 1, "Invalid deferred page properties are reported without an unhandled dispatcher error.");
        errors.Clear(); window.FindControl<StackPanel>("NotificationHost")!.Children.Clear(); window.ApplyInterfaceLayers("fixture.broken-owner", []);
        if (presetFile is not null)
        {
            var cover = Path.Combine(Path.GetFullPath(output), "fixture-cover.png");
            var artwork = new Border { Width = 320, Height = 320, Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = [new(Color.Parse("#52B9C4"), 0), new(Color.Parse("#D588A6"), .6), new(Color.Parse("#E9D3A1"), 1)] }, Child = new TextBlock { Text = "♪", FontSize = 140, Foreground = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } };
            artwork.Measure(new Size(320, 320)); artwork.Arrange(new Rect(0, 0, 320, 320));
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(320, 320))) { bitmap.Render(artwork); bitmap.Save(cover, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            vm.CurrentTrack = new TrackItem("fixture.presentation", "风吹过，音乐仍在继续", "Nonet", "Presentation", "fixture.flac", ".flac", 0) { CoverPath = cover, DurationSeconds = 180 };
            for (var i = 0; i < 120; i++) vm.LyricLines.Add(new(i, $"第 {i} 行：让旋律继续，留住此刻的光", i % 2 == 0 ? "Let the music stay with us" : ""));
            vm.PlaybackDuration = 180; vm.PlaybackPosition = 73.25;
            using var input = File.OpenRead(presetFile); var preset = StyleSheetContract.Read(input);
            window.ApplyInterfaceLayers("fixture.owner", [preset]); Pump();
            Check(!window.GetVisualDescendants().OfType<Image>().Any(i => i.Effect is BlurEffect { Radius: 40 }), "The supplied scheme has no blurred cover background.");
            Check(window.IsInterfaceSettingOwned("titleButtonsOnLeft") && !window.InterfaceSettings.TitleButtonsOnLeft, "The scheme owns and fixes native button placement to the right.");
            Check(!window.StyleLayers.WindowMorphEnabled, "The supplied scheme disables window morph without disabling lyric transitions.");
            foreach (var page in new[] { "library", "interface:playlists", "settings", "lyrics", "songs", "library" })
            {
                vm.Navigate(page); Pump(65); Save(page.Replace(':', '-') + ".png");
                var sidebar = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SidebarPanel");
                Check(sidebar is Border { BorderThickness: var thickness } && thickness == new Thickness(0), "The scheme separates sidebar and content by color without an outline.");
                Check(!sidebar.GetVisualDescendants().OfType<ScrollViewer>().Any(), "The scheme sidebar has no scrolling region.");
                Check(!sidebar.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString()?.StartsWith("plugin:") == true || b.Tag?.ToString() == "playlist:" + Playlist.LikedId), "Plugin pages and a duplicate favorites entry are absent from the scheme sidebar.");
                var mainButtons = sidebar.GetVisualDescendants().OfType<Button>().Where(b => b.Tag?.ToString() is "library" or "songs" or "albums" or "artists").ToArray();
                if (sidebar.IsEffectivelyVisible && mainButtons.Length > 1)
                {
                    var first = mainButtons[0].TranslatePoint(default, sidebar)!.Value.Y;
                    var next = mainButtons[1].TranslatePoint(default, sidebar)!.Value.Y;
                    Check(Math.Abs(next - first - mainButtons[0].Bounds.Height - 4) < 1, "The sidebar retains the original four-DIP inter-button spacing.");
                }
                if (page == "interface:playlists")
                {
                    var gallery = window.GetVisualDescendants().OfType<Control>().Single(l => l.Name == "InterfacePlaylistGallery");
                    Check(gallery.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single().RealizedCount > 0 && gallery.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "InterfacePlaylistCard"), "The playlist page realizes independent virtualized cards.");
                    Check(gallery.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("placeholder")).All(b => b.Background is ISolidColorBrush { Color.A: 0 }), "Default playlist covers have no white frame.");
                }
                Check(window.GetVisualDescendants().OfType<Button>().Count(b => b.Name is "CloseWindowButton" or "MinimizeWindowButton" or "MaximizeWindowButton") == 3, "Native caption remains reachable.");
            }
            foreach (var size in new[] { new Size(640, 480), new Size(960, 600), new Size(1200, 800) })
            {
                window.Width = size.Width; window.Height = size.Height; vm.Navigate("lyrics"); Pump(65); Save($"lyrics-{size.Width}-{size.Height}.png");
                var art = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsArtwork");
                var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "FullLyricsHost");
                var location = art.TranslatePoint(default, window);
                Check(host.IsEffectivelyVisible && host.Opacity == 1 && art.Bounds.Width > 100 && art.Bounds.Height > 100 && location is { X: >= 0, Y: >= 0 } && location.Value.Y + art.Bounds.Height <= window.Bounds.Height, "Lyrics stay visible, opaque and inside the viewport while resizing.");
                Check(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "LyricsMore"), "Lyrics tools are available only from the player menu.");
                var player = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PlayerSurface");
                Check(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WindowSurface").BorderThickness == new Thickness(0), "Immersive lyrics cover the inner frame edge.");
                Check(host.TranslatePoint(default, window)!.Value.Y + host.Bounds.Height <= player.TranslatePoint(default, window)!.Value.Y + 1, "The lyric viewport ends above the player rather than behind it.");
            }
            Check(JsonSerializer.Serialize(vm.Settings, AppStorage.Json) == original, "Navigation and viewport changes do not persist preset values.");
            Check(errors.Count == 0, "The supplied layout does not rely on error fallback: " + string.Join(" / ", errors));
            window.ApplyInterfaceLayers("fixture.owner", []); vm.Navigate("settings"); Pump();
            Check(!window.IsInterfaceSettingOwned("navigationRight") && window.InterfaceSettings.NavigationRight, "All presentation ownership is released.");
        }
        window.Close(); Pump();
        Console.WriteLine("PASS composition reference rollback, retained handlers, settings overlay/no persistence, disabled editors, removal, full shell, navigation and responsive lyrics.");
        void Save(string name) { using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(output, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
    }
    private static void VerifyLeases()
    {
        var source = new StackPanel { Name = "Source" }; var button = new Button { Name = "Original", Content = "Original" }; source.Children.Add(button);
        var target = new Border { Name = "Target", Child = new TextBlock { Text = "Original content" } };
        var grid = new Grid { Children = { source, target } }; var root = new Window { Content = grid, Width = 400, Height = 300 }; root.Show(); Pump();
        var clicked = 0; button.Click += (_, _) => clicked++;
        using var composition = new InterfaceCompositionService(root, _ => null);
        var node = new InterfaceNode { Kind = "stack", Children = [new() { Kind = "ref", Target = "Original", Properties = new() { ["width"] = 80d } }] };
        composition.Apply(new Dictionary<string, InterfaceNode> { ["Target"] = node }); Pump();
        Check(button.Parent != source && button.Width == 80, "Existing controls can be relocated with reversible properties.");
        button.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent)); Check(clicked == 1, "The existing action is retained.");
        composition.Suspend(); Check(button.Parent == source && double.IsNaN(button.Width), "Original parent and local properties are restored.");
        node.Children.Add(new() { Kind = "ref", Target = "Original" });
        try { composition.Apply(new Dictionary<string, InterfaceNode> { ["Target"] = node }); throw new Exception("Duplicate reference accepted."); } catch (InvalidDataException) { }
        Check(button.Parent == source, "A malformed tree rolls back borrowed controls.");
        root.Close(); Pump();
    }
    private static void Pump(int count = 20) { for (var n = 0; n < count; n++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(6); } }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
