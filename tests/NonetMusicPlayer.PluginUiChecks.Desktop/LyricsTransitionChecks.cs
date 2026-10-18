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
        vm.Navigate("lyrics");
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
        }
        vm.Navigate("settings");
        Check(motionEnabled == Overlay(), "Closing keeps outgoing lyrics only as a noninteractive snapshot.");
        Capture("lyrics-closing.png"); Pump(80); Check(!Overlay(), "Closing releases overlay.");
        vm.Navigate("lyrics"); vm.Navigate("settings"); vm.Navigate("lyrics"); Pump(80);
        Check(!Overlay() && vm.Page == "lyrics", "Rapid toggles leave the latest page and no stale overlay.");
        vm.Navigate("settings"); Pump(80); vm.Settings.PlayerTop = true; vm.ApplySettings(); Pump();
        vm.Navigate("lyrics");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        if (motionEnabled) Check(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay").Child is Image { RenderTransform: TranslateTransform { Y: < 0 } }, "Top player expands lyrics downward.");
        window.WindowState = WindowState.Minimized; Pump(); Check(!Overlay(), "Minimize cancels transition immediately.");
        window.WindowState = WindowState.Normal; Pump();
        var previous = Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION");
        try
        {
            Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
            vm.Navigate("settings"); vm.Navigate("lyrics"); Check(!Overlay(), "Reduced motion navigates without snapshots.");
        }
        finally { Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", previous); }
        window.Close(); Pump();
        Console.WriteLine("PASS relative asset paths, removed project setting, bidirectional lyrics transitions, top/bottom player, cancellation, rapid navigation and reduced motion.");
        bool Overlay() => window.GetVisualDescendants().OfType<Border>().Count(b => b.Name == "LyricsTransitionOverlay") == 1;
        void Capture(string name) { using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default); }
    }
    private static void Pump(int count = 32) { for (var i = 0; i < count; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
