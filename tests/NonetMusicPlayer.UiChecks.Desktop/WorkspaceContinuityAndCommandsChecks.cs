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
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class WorkspaceContinuityAndCommandsChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        var original = vm.State.Tracks.First();
        for (var i = 0; i < 300; i++) vm.State.Tracks.Add(new TrackItem("continuity-" + i, "Viewport song " + i, "Artist " + i % 12, "Album " + i % 16, original.FilePath, ".wav", original.FileSize));
        vm.Navigate("library"); Pump(owner);
        Require(owner.GetVisualDescendants().Any(c => c is Control { Name: "MusicHome" }), "Music route is a home page");
        Require(owner.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "总览"), "Overview heading on music home");
        Require(!owner.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "我的歌单"), "No duplicated playlist section on home");
        var navigation = owner.FindControl<StackPanel>("MyMusicNavigation")!;
        Require(!navigation.Children.OfType<Button>().Any(b => b.Tag?.ToString() is "artists" or "albums"), "Album/artist sidebar entries removed");
        Require(navigation.GetVisualDescendants().OfType<VectorIcon>().Any(i => i.Kind == IconKind.Home), "New home icon");
        Require(owner.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "terminal"), "Terminal sidebar entry");
        Capture(owner, output, "music-home");
        foreach (var route in new[] { "albums", "artists" })
        {
            vm.Navigate(route); Pump(owner);
            var back = owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GroupsBack");
            Require(back.GetVisualDescendants().OfType<VectorIcon>().Any(i => i.Kind == IconKind.Back), "Category back button has arrow icon");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner); Require(vm.Page == "library", "Category back returns to music home");
        }
        vm.Navigate("songs"); Pump(owner); var list = owner.FindControl<ListBox>("TracksList")!;
        var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().Single(); viewer.Offset = new Vector(0, 3000); Settle(owner);
        var offset = viewer.Offset;
        vm.Settings.OptimizeMemoryWhenMinimized = true; owner.WindowState = WindowState.Minimized; Pump(owner); owner.WindowState = WindowState.Normal; Settle(owner);
        Require(Math.Abs(viewer.Offset.Y - offset.Y) < 2, "Song scroll survives minimize/restore");
        vm.Navigate("settings"); Pump(owner); var settings = owner.GetVisualDescendants().OfType<SettingsView>().Single();
        var settingsScroll = settings.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
        settingsScroll.Offset = new Vector(0, 500); Settle(owner); var oldOffset = settingsScroll.Offset.Y;
        owner.WindowState = WindowState.Minimized; Pump(owner); owner.WindowState = WindowState.Normal; Settle(owner);
        var restored = owner.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
        Require(Math.Abs(restored.Offset.Y - oldOffset) < 2, "Rebuilt settings preserve scroll without retaining old controls");
        var created = Wait(owner, owner.Commands.ExecuteAsync("nonet playlist create \"Terminal fixture\"")); Require(created.Success, "Desktop command creates playlist");
        var id = created.Data!["id"]!.GetValue<string>(); Require(vm.Playlists.Any(p => p.Id == id), "Command mutates the same UI state");
        var confirm = Wait(owner, owner.Commands.ExecuteAsync("nonet playlist delete " + id)); Require(confirm.RequiresConfirmation && vm.Playlists.Any(p => p.Id == id), "No implicit destructive confirmation");
        var volumeBefore = vm.Volume; var invalid = Wait(owner, owner.Commands.ExecuteAsync("nonet settings set volume -2")); Require(!invalid.Success && vm.Volume == volumeBefore, "Invalid setting leaves state unchanged");
        var commandVolume = Wait(owner, owner.Commands.ExecuteAsync("nonet player volume 63")); vm.Volume = 64;
        Require(owner.Commands.Journal.Last().Operation == "player.volume" && owner.Commands.Journal.Last().Message == commandVolume.Message, "UI and command publish identical operation results");
        var playlist = vm.Playlists.First(p => p.Id == id); vm.AddToPlaylist(playlist, [original]); vm.Navigate("playlist:" + id); Wait(owner, vm.PlayTrackAsync(original));
        var playButton = owner.FindControl<Button>("PlayerPlay")!; var playPoint = playButton.TranslatePoint(new Point(playButton.Bounds.Width / 2, playButton.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(playPoint, MouseButton.Left); owner.MouseUp(playPoint, MouseButton.Left); Settle(owner);
        Require(owner.Commands.Journal.Any(r => r.Operation == "player.pause"), "Mouse pause appears in terminal journal");
        Require(!Wait(owner, owner.Commands.ExecuteAsync("cmd /c echo should-not-run")).Success, "System shell rejected by desktop terminal");
        vm.Navigate("terminal"); Pump(owner);
        var terminal = owner.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "TerminalOutput");
        AppLog.Flush(); Pump(owner);
        Require(terminal.Text?.Contains("nonet player pause") == true, "Terminal view shows retained UI output");
        Require(!terminal.IsReadOnly && !owner.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "TerminalCommandInput"), "One editable terminal surface, no detached input");
        Require(terminal.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(p => p.Text?.Contains("nonet $ ") == true), "Terminal text is actually rendered through native textbox theme");
        Require(ToolTip.GetTip(terminal) is null && !FullTextToolTips.GetEnabled(terminal), "Terminal has no tooltip");
        terminal.Focus(); terminal.CaretIndex = terminal.Text!.Length;
        owner.KeyTextInput("player volume 47"); Pump(owner);
        Require(owner.Terminal.Snapshot.Draft == "player volume 47", "Typing unprefixed command after prompt");
        PressEnter(owner); Settle(owner); Require(vm.Volume == 47 && terminal.Text!.EndsWith("nonet $ "), "Enter executes inline and creates next prompt");
        terminal.CaretIndex = 0; owner.KeyTextInput("nonet player status"); Pump(owner);
        Require(terminal.Text!.StartsWith(owner.Terminal.Snapshot.Transcript) && owner.Terminal.Snapshot.Draft == "nonet player status", "Clicking history cannot edit immutable output");
        owner.Commands.Publish(NonetMusicPlayer.Core.Commands.CommandResults.Completed("player.pause")); Pump(owner);
        Require(owner.Terminal.Snapshot.Draft == "nonet player status", "UI action logs do not erase draft");
        owner.Terminal.SetDraft("nonet clear"); PressEnter(owner); Settle(owner);
        Require(terminal.Text == "nonet $ ", "Clear leaves only prompt");
        terminal.Focus(); owner.KeyTextInput("nonet help player pause"); PressEnter(owner); Settle(owner);
        Require(terminal.Text!.Contains("nonet player pause") && terminal.Text.EndsWith("nonet $ "), "Terminal remains usable after clear");
        vm.Settings.TerminalOpacity = .3; vm.ApplySettings(); Pump(owner);
        Require(((Avalonia.Media.SolidColorBrush)owner.GetVisualDescendants().OfType<TerminalView>().Single().Background!).Color.A is 76 or 77, "Terminal opacity affects background only");
        Capture(owner, output, "embedded-terminal");
        Wait(owner, owner.Commands.ExecuteAsync("nonet playlist delete " + id + " -y")); vm.Navigate("library"); Pump(owner);
        Console.WriteLine("PASS CURRENT: home/cards/sidebar, scroll continuity, shared UI/terminal state and output, input validation, explicit confirmation and shell rejection.");
    }
    private static void PressEnter(Window window) { window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window); }
    private static T Wait<T>(Window window, Task<T> task) { var deadline = Environment.TickCount64 + 10000; while (!task.IsCompleted && Environment.TickCount64 < deadline) { Pump(window); Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException(); return task.GetAwaiter().GetResult(); }
    private static void Wait(Window window, Task task) { var deadline = Environment.TickCount64 + 10000; while (!task.IsCompleted && Environment.TickCount64 < deadline) { Pump(window); Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Settle(Window window) { for (var i = 0; i < 15; i++) { Pump(window); Thread.Sleep(15); } }
    private static void Capture(Window window, string output, string name) { using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
