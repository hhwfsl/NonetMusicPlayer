using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class SelectionAndCustomizationChecks
{
    internal static void Run(MainWindow window, MainViewModel vm, string output)
    {
        vm.Navigate("songs"); Pump(window);
        var list = window.FindControl<TrackListBox>("TracksList")!;
        Require(!list.BatchMode && !window.FindControl<Grid>("SelectionToolbar")!.IsVisible, "No batch controls before opt-in");
        list.ScrollIntoView(vm.VisibleTracks[0]); Pump(window);
        var row = Row(window, vm.VisibleTracks[0]);
        Require(!row.Children.OfType<CheckBox>().Single().IsVisible, "Row checkbox hidden outside batch");
        var rowPoint = row.TranslatePoint(new Point(row.Bounds.Width / 3, row.Bounds.Height / 2), window)!.Value;
        Click(window, rowPoint); Require(list.SelectedItems!.Count == 0, "Row click cannot select outside batch");
        window.FindControl<Button>("BatchEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(list.BatchMode && window.FindControl<Grid>("SelectionToolbar")!.IsVisible, "Explicit batch entry shows toolbar");
        row = Row(window, vm.VisibleTracks[0]); rowPoint = row.TranslatePoint(new Point(row.Bounds.Width / 3, row.Bounds.Height / 2), window)!.Value;
        Click(window, rowPoint); Require(list.SelectedItems.Count == 0, "Row click cannot select inside batch either");
        var check = row.Children.OfType<CheckBox>().Single();
        Click(window, check.TranslatePoint(new Point(10, check.Bounds.Height / 2), window)!.Value);
        var all = window.FindControl<CheckBox>("SelectionToggle")!;
        Require(list.SelectedItems.Count == 1 && all.IsChecked is null && all.IsThreeState, "Partial selection is indeterminate");
        Click(window, all.TranslatePoint(new Point(10, all.Bounds.Height / 2), window)!.Value);
        Require(list.SelectedItems.Count == vm.VisibleTracks.Count && all.IsChecked == true && all.Content?.ToString() == "取消全选", "Actual checkbox click selects all");
        Click(window, all.TranslatePoint(new Point(10, all.Bounds.Height / 2), window)!.Value);
        Require(list.SelectedItems.Count == 0 && all.IsChecked == false && all.Content?.ToString() == "全选", "Same checkbox cancels all");
        window.FindControl<Button>("BatchEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
        Require(!list.BatchMode && list.SelectedItems.Count == 0, "Exiting batch clears selection");
        var entry = window.FindControl<Button>("BatchEntryButton")!; entry.Focus(); var initialPlaying = vm.IsPlaying;
        Press(window, Key.Space, PhysicalKey.Space); Require(vm.IsPlaying != initialPlaying && !list.BatchMode, "Global Space release cannot accidentally click focused button");
        Press(window, Key.Enter, PhysicalKey.Enter); Require(list.BatchMode, "Enter still activates focused button");
        entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // A second right click must synchronously close the first popup, without selecting.
        var playlistButtons = window.FindControl<StackPanel>("PlaylistNavigation")!.Children.OfType<Button>().ToArray();
        RightClick(window, playlistButtons[0]); var firstMenu = playlistButtons[0].ContextMenu!; Require(firstMenu.IsOpen, "Sidebar playlist context menu opens");
        RightClick(window, playlistButtons[1]); var secondMenu = playlistButtons[1].ContextMenu!;
        Require(!firstMenu.IsOpen && secondMenu.IsOpen, $"Only one context menu open (first={firstMenu.IsOpen}, second={secondMenu.IsOpen}, page={vm.Page})"); secondMenu.Close(); Pump(window);
        row = Row(window, vm.VisibleTracks[0]); RightClick(window, row); var rowMenu = row.ContextMenu;
        // Anchor may be the actual hit-test child, so use the tracked menu for validation.
        var active = (ContextMenu?)typeof(MainWindow).GetField("_activeMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window);
        Require(active?.IsOpen == true && list.SelectedItems.Count == 0, "Track right click does not select"); active!.Close();

        ShortcutService.Validate(vm.Settings.KeyBindings);
        var duplicate = ShortcutService.DefaultBindings; duplicate["next"] = duplicate["previous"];
        var rejected = false; try { ShortcutService.Validate(duplicate); } catch (InvalidDataException) { rejected = true; } Require(rejected, "Conflicting shortcuts rejected");
        foreach (var page in new[] { "plugins", "statistics", "lyrics", "settings" })
        {
            vm.Navigate(page); Pump(window); window.FindControl<Button>("PlayerMode")!.Focus();
            var playing = vm.IsPlaying; Press(window, Key.Space, PhysicalKey.Space);
            Require(vm.IsPlaying != playing, "Space playback on " + page);
        }
        var original = new Dictionary<string, string>(vm.Settings.KeyBindings); vm.Settings.KeyBindings["playPause"] = "Ctrl+P"; vm.ApplySettings();
        window.FindControl<Button>("PlayerMode")!.Focus(); var prior = vm.IsPlaying;
        Press(window, Key.Space, PhysicalKey.Space); Require(vm.IsPlaying == prior, "Old default no longer active after remap");
        ((ContextMenu?)typeof(MainWindow).GetField("_activeMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window))?.Close();
        Press(window, Key.P, PhysicalKey.P, RawInputModifiers.Control); Require(vm.IsPlaying != prior, "Custom shortcut active immediately");
        vm.Settings.KeyBindings = original; vm.ApplySettings();
        vm.Navigate("settings"); Pump(window);
        var settingsSearch = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SettingsSearch"); settingsSearch.Focus(); settingsSearch.Text = "快捷键"; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
        var captureButton = window.GetVisualDescendants().OfType<ShortcutCaptureButton>().First(); captureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); captureButton.Focus();
        var capturePlaying = vm.IsPlaying; Press(window, Key.Space, PhysicalKey.Space);
        Require(!captureButton.IsCapturing && captureButton.Content?.ToString() == "Space" && vm.IsPlaying == capturePlaying, "Capturing Space consumes KeyUp without re-entering recording or playing");
        settingsSearch.Focus(); settingsSearch.Text = "主题"; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
        var input = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AccentHex"); input.Focus(); prior = vm.IsPlaying;
        Press(window, Key.Space, PhysicalKey.Space); Require(vm.IsPlaying == prior, "Text input retains Space");
        Require(window.GetVisualDescendants().OfType<Border>().Single(t => t.Name == "AccentPreview").Background is SolidColorBrush, "Accent beside-hex color preview");
        if (OperatingSystem.IsWindows())
        {
            vm.Settings.TitleButtonsOnLeft = true; vm.ApplySettings(); Require(Grid.GetColumn(window.FindControl<StackPanel>("TitleButtons")!) == 0, "Traffic lights configurable to left");
            vm.Settings.TitleButtonsOnLeft = false; vm.ApplySettings(); Require(Grid.GetColumn(window.FindControl<StackPanel>("TitleButtons")!) == 2, "Traffic lights configurable to right");
        }
        using (var wheel = new ColorWheel()) { wheel.SetColor(Color.Parse("#123456")); Require(wheel.SelectedColor == Color.Parse("#123456"), "RGB to color-wheel round trip"); }
        var track = vm.State.Tracks.First(t => t.HasArtwork);
        vm.SetGroupCover("artist", track.Artist, track.CoverPath); Require(vm.GetGroupCover("artist", track.Artist) is not null, "Artist custom cover stored");
        vm.SetGroupCover("album", track.Album, track.CoverPath); Require(vm.GetGroupCover("album", track.Album) is not null, "Album custom cover stored");
        vm.Navigate("artists"); Pump(window); Capture(window, output, "artists-custom-cover");
        vm.Navigate("statistics"); Pump(window); Capture(window, output, "listening-statistics");
        vm.Navigate("songs");
        Pump(window); list.ScrollIntoView(vm.VisibleTracks[2]); Pump(window);
        row = Row(window, vm.VisibleTracks[2]); var pointToPlay = row.TranslatePoint(new Point(row.Bounds.Width / 3, row.Bounds.Height / 2), window)!.Value;
        Click(window, pointToPlay); Click(window, pointToPlay); Require(vm.CurrentTrack?.Id == vm.VisibleTracks[2].Id && list.SelectedItems.Count == 0, "Double-click playback remains available without selection");
        Console.WriteLine("PASS BETA4: opt-in checkbox-only selection, tri-state all/clear, single context menu, global/custom shortcuts, color wheel, title positions, artist/album covers and stats page");
    }
    private static Grid Row(MainWindow window, object track) => window.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("track-row") && ReferenceEquals(g.DataContext, track));
    private static void Click(MainWindow window, Point point) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(window); }
    private static void RightClick(MainWindow window, Control control) { var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value; window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Pump(window); }
    private static void Press(MainWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None) { window.KeyPress(key, modifiers, physical, null); window.KeyRelease(key, modifiers, physical, null); Pump(window); }
    private static void Pump(MainWindow window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string output, string name) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
