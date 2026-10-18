using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>覆盖歌词双向切换、快速重复导航、最小化清理和设置中的相对路径，不使用真实音乐或用户配置。</summary>
internal static class LyricsTransitionChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CheckSnapshotScale();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new AppStorage(Path.Combine(root, "data"));
        NonetMusicPlayer.Core.Diagnostics.AppLog.Initialize(storage.Root);
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (File.Exists(font)) vm.Settings.FontFilePath = AppFontService.Import(storage, font);
        var background = Path.Combine(root, "background.png");
        using (var image = new RenderTargetBitmap(new PixelSize(64, 64))) image.Save(background, PngBitmapEncoderOptions.Default);
        vm.Settings.BackgroundImagePath = AppBackgroundService.Import(storage, background);
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760 }; window.Show(); Pump();
        vm.Navigate("settings"); Pump();
        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
        Check(!labels.Any(t => t == L10n.T("Extensions.ProjectFolder")), "No plugin project directory setting.");
        var backgroundLabel = labels.Single(t => t.StartsWith(L10n.T("Settings.BackgroundImage") + ": "));
        Check(backgroundLabel.Contains("Backgrounds") && !backgroundLabel.Contains(storage.Root), "Background path is relative to data root.");
        if (vm.Settings.FontFilePath is not null)
        {
            var fontLabel = labels.Single(t => t.StartsWith(L10n.T("Settings.FontFile") + ": "));
            Check(fontLabel.Contains("Fonts") && !fontLabel.Contains(storage.Root), "Font path is relative to data root.");
        }
        var motionEnabled = InterfaceMotion.IsEnabled;
        for (var n = 0; n < 120; n++) vm.LyricLines.Add(new(n, $"第 {n} 行歌词" + (n % 9 == 0 ? new string('长', 80) : ""), n % 3 == 0 ? "Translation " + new string('x', 70) : ""));
        vm.PlaybackDuration = 180; vm.PlaybackPosition = 73.25;
        vm.Navigate("lyrics");
        CenteredBeforeFrame(73);
        NonetMusicPlayer.Core.Diagnostics.AppLog.Flush();
        Check(motionEnabled == Overlay(), "Opening uses one finite overlay when animations are enabled.");
        if (motionEnabled)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "FullLyricsHost");
            var opening = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay");
            Check(opening.Child is Image { RenderTransform: TranslateTransform { Y: > 0 } } && opening.Bounds.Top >= 50 && opening.ZIndex > 20, "Bottom player expands only the lyrics viewport above the workspace.");
            Check(host.IsVisible && host.Opacity == 0 && !host.IsHitTestVisible, "Live lyrics clock remains attached while transition input is suppressed.");
            Capture("lyrics-opening.png"); Pump(80);
            Check(!Overlay() && host.Opacity == 1 && host.IsHitTestVisible, "Opening releases bitmap and restores original appearance/input.");
            CenteredBeforeFrame(73);
        }
        vm.Navigate("settings");
        Check(motionEnabled == Overlay(), "Closing keeps outgoing lyrics only as a noninteractive snapshot.");
        Capture("lyrics-closing.png"); Pump(80); Check(!Overlay(), "Closing releases overlay.");
        vm.PlaybackPosition = 91.25; vm.Navigate("lyrics"); CenteredBeforeFrame(91); vm.Navigate("settings"); vm.Navigate("lyrics"); CenteredBeforeFrame(91); Pump(80);
        Check(!Overlay() && vm.Page == "lyrics", "Rapid toggles leave the latest page and no stale overlay.");
        vm.Navigate("settings"); Pump(80); vm.Settings.PlayerTop = true; vm.ApplySettings(); Pump();
        vm.PlaybackPosition = 51.25; vm.Navigate("lyrics"); CenteredBeforeFrame(51);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        if (motionEnabled) Check(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay").Child is Image { RenderTransform: TranslateTransform { Y: < 0 } }, "Top player expands lyrics downward.");
        window.WindowState = WindowState.Minimized; Pump(); Check(!Overlay(), "Minimize cancels transition immediately.");
        window.WindowState = WindowState.Normal; Pump();
        var previous = Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION");
        try
        {
            Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
            vm.Navigate("settings"); vm.PlaybackPosition = 40.25; vm.Navigate("lyrics"); CenteredBeforeFrame(40); Check(!Overlay(), "Reduced motion navigates without snapshots.");
            foreach (var index in new[] { 0, 119, 36, 73 })
            {
                vm.Navigate("settings"); vm.PlaybackPosition = index + .25; vm.Navigate("lyrics"); CenteredBeforeFrame(index); Pump(); CenteredBeforeFrame(index);
            }
        }
        finally { Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", previous); }
        window.Close(); Pump();
        Console.WriteLine("PASS lyrics pre-centered before the first animation frame, stable after transition, variable-height rows, first/last/deep line, top/bottom player, cancellation, rapid navigation, reduced motion and snapshot scale.");
        bool Overlay() => window.GetVisualDescendants().OfType<Border>().Count(b => b.Name == "LyricsTransitionOverlay") == 1;
        void Capture(string name) { using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default); }
        // 不推进 Dispatcher 或动画时钟，确保快照捕获前已经完成真实虚拟化列表的定位。
        void CenteredBeforeFrame(int index)
        {
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "LyricLines");
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Check(list.SelectedIndex == index, "The selected lyric is sampled from the current playback position.");
            var row = list.ContainerFromIndex(index);
            Check(row is not null, $"Current lyric {index} is realized before the first transition frame.");
            var center = row!.TranslatePoint(new Point(0, row!.Bounds.Height / 2), scroll)!.Value.Y;
            Check(Math.Abs(center - scroll.Viewport.Height / 2) < 2, $"Current lyric is centered before the snapshot, not after animation: delta {center - scroll.Viewport.Height / 2}.");
        }
    }
    private static void Pump(int count = 32) { for (var i = 0; i < count; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }
    // 在高 DPI 和快照像素限额触发时比较标记的实际绘制尺寸，避免只检查动画类型而遗漏画面缩放。
    private static void CheckSnapshotScale()
    {
        foreach (var dimensions in new[] { new Size(800, 500), new Size(1800, 1400) })
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var source = new Canvas { Width = dimensions.Width, Height = dimensions.Height, Background = Brushes.Black };
            var marker = new Border { Width = 60, Height = 40, Background = Brushes.Red };
            Canvas.SetLeft(marker, 180); Canvas.SetTop(marker, 150); source.Children.Add(marker);
            source.Measure(dimensions); source.Arrange(new Rect(dimensions));
            using var snapshot = LyricsPageMotion.Capture(source, new Rect(100, 100, 400, 300), scale)!;
            using var rendered = new RenderTargetBitmap(new PixelSize(400, 300));
            using (var drawing = rendered.CreateDrawingContext()) drawing.DrawImage(snapshot, new Rect(0, 0, 400, 300));
            using var bytes = new MemoryStream(); rendered.Save(bytes, PngBitmapEncoderOptions.Default); bytes.Position = 0;
            using var pixels = SkiaSharp.SKBitmap.Decode(bytes);
            var points = Enumerable.Range(0, 400).Where(x => pixels.GetPixel(x, 70).Red > 200).ToArray();
            Check(points.Length is >= 58 and <= 62 && Math.Abs(points[0] - 80) < 2, $"Snapshot retains logical dimensions at {scale}x; observed {points.Length} pixels at {points.FirstOrDefault()}.");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
