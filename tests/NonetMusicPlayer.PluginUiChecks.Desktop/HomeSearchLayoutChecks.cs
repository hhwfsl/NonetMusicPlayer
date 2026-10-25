using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>以真实启动顺序复现零 Bounds 的头部创建，比较首次显示、导航返回与原位缩放。</summary>
internal static class HomeSearchLayoutChecks
{
    public static void Run(string output, string? preset)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Directory.CreateDirectory(output);
        foreach (var styled in preset is null ? new[] { false } : new[] { false, true })
        foreach (var size in new[] { new Size(640, 480), new Size(900, 620), new Size(1280, 800), new Size(1200, 800) })
        {
            var storage = new AppStorage(Path.Combine(Path.GetFullPath(output), "data-" + Guid.NewGuid().ToString("N")));
            using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio()); vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
            var window = new MainWindow { DataContext = vm }; window.Width = size.Width; window.Height = size.Height;
            if (styled) { using var file = File.OpenRead(preset!); window.ApplyInterfaceLayers("fixture.startup", [StyleSheetContract.Read(file)]); }
            window.PrepareInitialAppearance(); window.Show(); Pump();
            TextBox Editor() => window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "HomeMusicSearch");
            var first = Editor().Bounds.Width;
            vm.Navigate("history"); Pump(); vm.Navigate("library"); Pump();
            Check(Math.Abs(first - Editor().Bounds.Width) < .1, $"Startup width {first} differs from navigation-return width {Editor().Bounds.Width} at {size}, styled={styled}.");
            var editor = Editor(); editor.Text = "search draft"; editor.Focus(); editor.CaretIndex = 4; Pump();
            window.Width = size.Width == 640 ? 1280 : 640; Pump();
            Check(ReferenceEquals(editor, Editor()) && editor.Text == "search draft" && editor.CaretIndex == 4, "Resize must retain editor identity, input and caret.");
            Check(Math.Abs(editor.Bounds.Width - first) > 1, "Existing search field reacts to window resizing without navigation.");
            window.Width = size.Width; Pump();
            Check(Math.Abs(first - editor.Bounds.Width) < .1, "Restoring window size restores the same search width.");
            using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(output, (styled ? "preset-" : "default-") + size.Width + "x" + size.Height + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Close(); Pump();
        }
        Console.WriteLine("PASS first-show/search-return sizing, hidden startup, baseline/preset, narrow/3:2 layouts and resize without input/caret loss.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump() { for (var i = 0; i < 25; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(2); } }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(180); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } } public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
