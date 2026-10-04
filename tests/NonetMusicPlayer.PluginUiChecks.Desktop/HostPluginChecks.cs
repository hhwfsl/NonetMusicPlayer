using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class HostPluginChecks
{
    public static void Run(string output)
    {
        output = Path.GetFullPath(output);
        var folder = Path.Combine(output, "plugin-host-" + Guid.NewGuid().ToString("N"));
        using var audio = new FakeAudio();
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(folder)), audio);
        vm.Settings.ConfirmClose = false;
        var package = Path.GetFullPath("artifacts/plugins/snake-game-test.impp");
        var manifest = vm.Plugins.Install(package);
        var window = new MainWindow { DataContext = vm }; window.Show(); Render(window);
        var navigation = window.FindControl<StackPanel>("PluginNavigation")!;
        Check(navigation.Children.Count == 0, "Disabled UI plugin absent from navigation");
        vm.Plugins.SetEnabled(manifest, true); vm.ApplySettings(); window.ShowPage(); Render(window);
        var button = navigation.Children.OfType<Button>().Single();
        Check(button.Tag?.ToString() == "plugin:" + manifest.Id && button.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "贪吃蛇小游戏测试插件"), "Actual sidebar uses full manifest label and generic id");
        var track = new TrackItem("host-music", "音乐保持播放", "测试", "测试", Path.Combine(folder, "fixture.wav"), ".wav", 0) { DurationSeconds = 100 };
        vm.State.Tracks.Add(track); vm.ApplyFilter(); vm.PlayTrackAsync(track).GetAwaiter().GetResult();
        Check(audio.IsPlaying && vm.IsPlaying, "Fake music begins before game");
        Click(window, button); Render(window);
        Check(vm.Page == "plugin:" + manifest.Id && window.FindControl<ContentControl>("AlternatePage")!.Content is PluginPageView, "Sidebar opens generic native plugin page");
        var game = window.GetVisualDescendants().OfType<SnakeGameControl>().Single();
        Click(window, game.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SnakeToggle"));
        Check(game.Game.Status == SnakeGameStatus.Running && vm.IsPlaying && audio.PauseCount == 0, "Actual start button starts game without pausing player");
        Press(window, Key.Space, PhysicalKey.Space);
        Check(game.Game.Status == SnakeGameStatus.Paused && vm.IsPlaying && audio.PauseCount == 0, "Game Space pauses only game");
        Press(window, Key.R, PhysicalKey.R); Check(game.Game.Status == SnakeGameStatus.Running, "Host R restarts game");
        Press(window, Key.Left, PhysicalKey.ArrowLeft); Check(audio.Position == TimeSpan.Zero, "Game direction does not seek music");
        using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No plugin host frame"))
            frame.Save(Path.Combine(output, "snake-plugin-host.png"), PngBitmapEncoderOptions.Default);
        window.Width = 980; window.Height = 660; Render(window); game.KeyboardTarget.BringIntoView(); Render(window);
        var viewport = game.KeyboardTarget.GetVisualAncestors().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().First();
        var position = game.KeyboardTarget.TranslatePoint(default, viewport)!.Value;
        Check(position.Y >= -.5 && position.Y + game.KeyboardTarget.Bounds.Height <= viewport.Bounds.Height + .5, "Complete game board can enter minimum-window viewport");
        vm.Navigate("songs"); Render(window);
        Check(game.IsDisposed && !game.IsTimerRunning && vm.IsPlaying, "Leaving plugin page disposes game while preserving music");
        vm.Navigate("plugin:" + manifest.Id); Render(window);
        var secondGame = window.GetVisualDescendants().OfType<SnakeGameControl>().Single(); secondGame.Restart();
        vm.DisablePlugin(manifest); window.ShowPage(); Render(window);
        Check(secondGame.IsDisposed && !secondGame.IsTimerRunning && navigation.Children.Count == 0 && vm.IsPlaying, "Disable removes navigation and disposes active native game");
        vm.Plugins.Uninstall(manifest); window.Close();
        Console.WriteLine("PASS UI PLUGIN HOST: actual sidebar label, generic page, player shortcut isolation, page exit and disable disposal");
    }
    private static void Render(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Press(Window window, Key key, PhysicalKey physical)
    { window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public int PauseCount { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(100);
        public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() => IsPlaying = true;
        public void Pause() { IsPlaying = false; PauseCount++; }
        public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; }
        public void Dispose() { }
    }
}
