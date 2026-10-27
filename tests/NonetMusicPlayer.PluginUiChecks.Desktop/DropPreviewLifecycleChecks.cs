using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>通过真实路由事件检查已处理投放、原地取消与目标切换，不触发音频导入。</summary>
internal static class DropPreviewLifecycleChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var storage = new AppStorage(Path.Combine(Path.GetFullPath(output), "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        vm.State.Tracks.Add(new("drop-fixture", "Drop fixture", "Artist", "Album", "fixture.flac", ".flac", 0));
        vm.LikedPlaylist.TrackIds.Add("drop-fixture"); vm.Navigate("favorites");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 }; window.Show(); Pump();
        try
        {
            var player = window.FindControl<Border>("PlayerSurface")!; var tracks = window.FindControl<Control>("TracksList")!;
            var data = new DataTransfer(); data.Add(DataTransferItem.CreateText("nonet-tracks:drop-fixture"));
            void Over(Control target) => target.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, data, target, new(10, 10), KeyModifiers.None) { Source = target });
            Border Hint() => window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DropPreviewSurface");
            void Show() { Over(player); Pump(); Require(Hint().IsVisible, "Drag over player shows a preview"); }
            Show();
            player.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, player, new(10, 10), KeyModifiers.None) { Source = player, Handled = true }); Pump();
            Require(!Hint().IsVisible, "A drop handled by a child still clears the preview");
            Show();
            window.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, window, new(10, 10), KeyModifiers.None) { Source = window }); Pump();
            Require(!Hint().IsVisible, "A window drop clears the preview");
            Show();
            player.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, data, player, new(10, 10), KeyModifiers.None) { Source = player }); Pump();
            Require(!Hint().IsVisible, "Cancel/leave clears the preview even when the last position was inside the window");
            Show();
            player.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, data, player, new(10, 10), KeyModifiers.None) { Source = player });
            Over(tracks); Pump();
            Require(Hint().IsVisible, "A newer target preview survives an earlier leave callback");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Require(!Hint().IsVisible, "Escape clears the preview");
            Show();
            window.MouseUp(player.TranslatePoint(new(10, 10), window)!.Value, MouseButton.Left); Pump();
            Require(!Hint().IsVisible, "Pointer release clears the preview");
            Show(); window.Hide(); Pump(); Require(!Hint().IsVisible, "Hiding the window clears the preview");
            Require(vm.State.Tracks.Count == 1 && vm.LikedPlaylist.TrackIds.Count == 1, "Preview cleanup never imports or removes tracks");
            Console.WriteLine("PASS drop preview: handled/unhandled drop, inside-coordinate leave, target handoff, Escape, pointer release and window hiding.");
        }
        finally { window.ExitApplication(); }
    }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class SilentAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
