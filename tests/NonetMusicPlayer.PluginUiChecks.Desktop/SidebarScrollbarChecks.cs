using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>在长侧栏、左右停靠及触摸滚动条下检查真实按钮边界，不依赖固定的内边距数值。</summary>
internal static class SidebarScrollbarChecks
{
    public static void Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("NONET_DATA_DIR", Path.Combine(output, "data"));
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var storage = new AppStorage(Path.Combine(output, "data"));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        for (var i = 0; i < 45; i++) vm.CreatePlaylist("Sidebar playlist " + i);
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 650 };
        window.Show(); Pump();
        try
        {
            foreach (var right in new[] { false, true })
            foreach (var touch in new[] { false, true })
            {
                vm.Settings.NavigationRight = right; vm.Settings.TouchMode = touch; vm.ApplySettings(); Pump();
                var scroll = window.FindControl<ScrollViewer>("SidebarScrollViewer")!;
                scroll.AllowAutoHide = false; Pump();
                var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
                if (!bar.IsVisible || bar.Bounds.Width < 1 || scroll.Extent.Height <= scroll.Viewport.Height)
                    throw new Exception("Sidebar fixture did not produce a vertical scrollbar.");
                var left = bar.TranslatePoint(default, scroll)!.Value.X;
                var buttons = window.FindControl<StackPanel>("SidebarScrollContent")!.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("nav")).ToArray();
                foreach (var button in buttons)
                {
                    var edge = button.TranslatePoint(new Point(button.Bounds.Width, 0), scroll)!.Value.X;
                    if (edge > left - 1) throw new Exception($"Sidebar highlight overlaps scrollbar: right={right}, touch={touch}, buttonRight={edge}, barLeft={left}.");
                }
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(output, $"sidebar-{(right ? "right" : "left")}-{(touch ? "touch" : "mouse")}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            Console.WriteLine("PASS sidebar button/highlight bounds avoid expanded scrollbars on both sides with mouse and touch sizes.");
        }
        finally { window.Close(); Pump(); }
    }
    private static void Pump() { for (var i = 0; i < 12; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(3); } }
    private sealed class SilentAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
