using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

/// <summary>覆盖歌词同帧交接、播放栏起点、浮层按钮开关；外部方案仅由显式参数传入。</summary>
internal static class SurfaceRevisionChecks
{
    public static void Run(string output, string? styleFile)
    {
        DropdownInteractionChecks.Run();
        var storage = new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer());
        vm.Settings.Theme = "Dark"; vm.ApplySettings();
        var tracks = Enumerable.Range(0, 80).Select(i => new TrackItem("surface-" + i, "浮层与转场回归 " + i, "Nonet", "Fixture", "unused.wav", ".wav", 100)).ToArray();
        vm.State.Tracks.AddRange(tracks); var playlist = vm.CreatePlaylist("背景标题保持不变"); vm.AddToPlaylist(playlist, tracks);
        vm.Navigate("playlist:" + playlist.Id); Wait(vm.PlayTrackAsync(tracks[0]));
        vm.Lyrics.Save(tracks[0].Id, string.Join('\n', Enumerable.Range(0, 30).Select(i => $"[00:{i:00}.00]歌词从整个播放栏展开 {i}"))); vm.ReloadLyrics();
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump(window, 100);
        if (styleFile is not null)
        {
            using var input = File.OpenRead(styleFile); var sheet = StyleSheetContract.Read(input);
            window.ApplyInterfaceLayers("surface-fixture", [sheet]); Pump(window, 150);
            var queue = window.FindControl<Button>("PlayerQueue")!; var desktop = window.FindControl<Button>("PlayerDesktopLyrics")!;
            Require(queue.IsEffectivelyVisible && ReferenceEquals(queue.Parent, desktop.Parent), "Updated style includes queue alongside desktop lyrics");
        }
        var maximize = window.FindControl<Button>("MaximizeWindowButton")!;
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(window.WindowState == WindowState.Maximized && !Overlays(window).Any(), "Maximize changes state synchronously without snapshot animation");
        maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(window.WindowState == WindowState.Normal && !Overlays(window).Any(), "Restore changes state synchronously without snapshot animation");
        Pump(window, 80);
        var title = vm.PageTitle; var player = window.FindControl<Border>("PlayerSurface")!;
        vm.Navigate("lyrics");
        Require(vm.PageTitle == title, "Opening lyrics preserves background page title");
        var overlay = Overlays(window).OfType<Border>().Single();
        window.UpdateLayout();
        var image = (Image)overlay.Child!;
        var shift = (Avalonia.Media.TranslateTransform)image.RenderTransform!;
        var edge = player.TranslatePoint(default, window)!.Value.Y;
        Require(Math.Abs(overlay.TranslatePoint(default, window)!.Value.Y + shift.Y - edge) < 1, "Lyrics first visible edge starts exactly at player top edge");

        // 首帧断言在推进时钟前执行，避免机器繁忙时把已经完成的动画误判为首帧。
        Require(overlay.Opacity == 1 && window.FindControl<ContentControl>("FullLyricsHost")!.Opacity == 0, "First frame hides live lyrics behind opaque transition snapshot");
        Pump(window, 70);
        Require(image.Width == overlay.Width && image.Height == overlay.Height && image.RenderTransform is Avalonia.Media.TranslateTransform, "Lyrics keeps original size and only translates vertically");
        for (var i = 0; i < 22; i++)
        {
            Pump(window, 20);
            Require(Overlays(window).Any() || !window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "LyricsTransitionBackdrop"), "Backdrop cannot outlive final lyrics overlay and flash through");
        }
        Require(!Overlays(window).Any() && window.FindControl<ContentControl>("FullLyricsHost")!.Opacity == 1, $"Completed entrance atomically reveals live lyrics (overlays={Overlays(window).Count()}, opacity={window.FindControl<ContentControl>("FullLyricsHost")!.Opacity}, y={shift.Y})");
        vm.Navigate("playlist:" + playlist.Id); Pump(window, 60); vm.Navigate("lyrics"); Pump(window, 60); vm.Navigate("playlist:" + playlist.Id); Pump(window, 450);
        Require(!Overlays(window).Any() && !window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "LyricsTransitionBackdrop"), "Interrupted lyrics releases snapshot and backdrop");

        var queueButton = window.FindControl<Button>("PlayerQueue")!;
        for (var cycle = 0; cycle < 6; cycle++)
        {
            Click(window, queueButton); Pump(window, 380);
            var queuePanel = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "PlaybackQueuePopover");
            Require(window.IsPlaybackQueueOpen && queuePanel.IsVisible && queuePanel.Opacity > .99 && queuePanel.IsHitTestVisible && queueButton.Classes.Contains("popup-open"), "Actual queue button opens visible, interactive and selected");
            vm.Navigate("settings"); Pump(window, 50); vm.Navigate("playlist:" + playlist.Id); Pump(window, 80);
            Require(queuePanel.IsAttachedToVisualTree() && ReferenceEquals(queuePanel.Parent, window.Content), "Queue survives shell recomposition and navigation");
            Click(window, queueButton); Pump(window, 270);
            Require(!window.IsPlaybackQueueOpen && !queuePanel.IsVisible && !queueButton.Classes.Contains("popup-open"), "Queue closes and clears selection after navigation");
        }
        for (var cycle = 0; cycle < 20; cycle++) { window.SetPlaybackQueueOpen(true); Pump(window, 3); window.SetPlaybackQueueOpen(false); Pump(window, 3); }
        window.SetPlaybackQueueOpen(true); Pump(window, 380);
        Require(window.IsPlaybackQueueOpen && window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "PlaybackQueuePopover").Opacity > .99, "Rapid queue interruptions cannot let stale close callbacks hide reopening");
        window.SetPlaybackQueueOpen(false); Pump(window, 260);
        var volume = window.FindControl<Button>("PlayerVolume")!; var flyout = (PopupFlyoutBase)volume.Flyout!;
        var openingOpacity = -1d;
        flyout.Opened += (_, _) => openingOpacity = flyout.Popup.Child?.Opacity ?? -1;
        Click(window, volume); Pump(window, 50);
        Require(volume.Classes.Contains("popup-open") && flyout.IsOpen && openingOpacity is >= 0 and < 1, "Volume floating surface starts animated before its first rendered frame");
        Pump(window, 250); Click(window, volume); Pump(window, 250);
        Require(!volume.Classes.Contains("popup-open") && !flyout.IsOpen, "Second volume click closes without reopening");
        Click(window, volume); Pump(window, 250); ClickPoint(window, new Point(350, 100)); Pump(window, 250);
        Require(!flyout.IsOpen, "Outside click dismisses volume");
        Click(window, volume); Pump(window, 250); Click(window, queueButton); Pump(window, 380);
        Require(window.IsPlaybackQueueOpen && !flyout.IsOpen && !volume.Classes.Contains("popup-open"), "Clicking queue while volume is open reaches queue on the first click");
        Click(window, queueButton); Pump(window, 260);
        foreach (var name in new[] { "PlayerMode", "PlayerMore" })
        {
            var button = window.FindControl<Button>(name)!; Click(window, button);
            Require(button.Classes.Contains("popup-open") && button.ContextMenu?.IsOpen == true && button.ContextMenu.Opacity < 1, name + " animates on open");
            Pump(window, 250); Click(window, button); Pump(window, 250);
            Require(!button.Classes.Contains("popup-open") && button.ContextMenu?.IsOpen != true, name + " toggles closed");
            Click(window, button); Pump(window, 250); ClickPoint(window, new Point(350, 100)); Pump(window, 250);
            Require(button.ContextMenu?.IsOpen != true, name + " dismisses outside");
            Click(window, button); Pump(window, 250); Click(window, queueButton); Pump(window, 380);
            Require(window.IsPlaybackQueueOpen && !button.Classes.Contains("popup-open"), name + " transfers outside click to queue without swallowing release");
            Click(window, queueButton); Pump(window, 260);
        }

        using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "surface-revision.png"), PngBitmapEncoderOptions.Default);

        // 日历使用独立窗口，验证同一按钮再次点击与外部关闭，并保留选中日期。
        var date = new StatisticsDatePicker { Width = 260, SelectedDate = new DateTime(2026, 11, 3) };
        var calendarWindow = new Window { Width = 500, Height = 500, Content = date }; calendarWindow.Show(); Pump(calendarWindow, 60);
        var dateButton = date.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StatisticsDateButton");
        var calendarFlyout = (PopupFlyoutBase)typeof(StatisticsDatePicker).GetField("_flyout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(date)!;
        var dateClicks = 0; dateButton.Click += (_, _) => dateClicks++;
        Click(calendarWindow, dateButton); Pump(calendarWindow, 250);
        Require(calendarFlyout.IsOpen, $"First calendar click opens (clicks={dateClicks})");
        Click(calendarWindow, dateButton); Pump(calendarWindow, 250);
        Require(!calendarFlyout.IsOpen && date.SelectedDate == new DateTime(2026, 11, 3), "Calendar toggles closed without altering date");
        Click(calendarWindow, dateButton); Pump(calendarWindow, 250); ClickPoint(calendarWindow, new Point(5, 5)); Pump(calendarWindow, 250);
        Require(!calendarFlyout.IsOpen && date.SelectedDate == new DateTime(2026, 11, 3), "Outside click closes calendar without changing date");
        calendarWindow.Close(); window.Close();
        Console.WriteLine("PASS surfaces: immediate window state, stable background title, vertical player-edge lyrics slide, queue recomposition/reopen and atomic handoff, animated/toggling/outside-dismissed volume and menus, calendar toggle" + (styleFile is null ? "" : ", updated external style"));
    }
    private static IEnumerable<Control> Overlays(Window window) => window.GetVisualDescendants().OfType<Control>().Where(c => c.Name is "NonetMorphOverlay" or "LyricsTransitionOverlay");
    private static void Click(Window window, Control control) => ClickPoint(window, control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value);
    private static void ClickPoint(Window window, Point point) { window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } task.GetAwaiter().GetResult(); }
    private static void Pump(Window window, int ms) { var until = Environment.TickCount64 + ms; do { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); Thread.Sleep(5); } while (Environment.TickCount64 < until); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
