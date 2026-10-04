using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class RootReviewChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(string output)
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        using var audio = new FakeAudio();
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")))), audio);
        vm.Settings.ConfirmClose = false;
        for (var i = 0; i < 8; i++) vm.State.Tracks.Add(new TrackItem("review-" + i, "审查歌曲 " + i, "艺术家", "专辑", Path.Combine(output, "fixture-" + i + ".wav"), ".wav", 0) { DurationSeconds = 100 });
        vm.ApplyFilter(); vm.PlayTrackAsync(vm.State.Tracks[0]).GetAwaiter().GetResult();
        var window = new MainWindow { DataContext = vm, Width = 980, Height = 660 }; window.Show(); Pump(window);
        var errors = new List<string>();
        void Case(string title, Action action)
        {
            try { action(); Console.WriteLine("PASS ROOT REVIEW: " + title); }
            catch (Exception error) { errors.Add(title + ": " + error.Message); Console.WriteLine("FAIL ROOT REVIEW: " + title + ": " + error); }
            finally { ActiveMenu(window)?.Close(); foreach (var owned in window.OwnedWindows.ToArray()) owned.Close(); Pump(window); }
        }
        Case("native button Enter, global Space, reserved Enter validation", () =>
        {
            vm.Navigate("songs"); Pump(window);
            var batch = window.FindControl<Button>("BatchEntryButton")!; batch.Focus(); var playing = vm.IsPlaying;
            var list = window.FindControl<TrackListBox>("TracksList")!;
            Press(window, Key.Space, PhysicalKey.Space); Check(vm.IsPlaying != playing && !list.BatchMode, "Space controls only player, not focused ordinary Button");
            batch.Focus(); Press(window, Key.Enter, PhysicalKey.Enter); Check(list.BatchMode, "Enter still activates ordinary button");
            Press(window, Key.Enter, PhysicalKey.Enter); Check(!list.BatchMode, "Enter can deactivate batch button");
            var bindings = ShortcutService.DefaultBindings; bindings["playPause"] = "Enter";
            Reject(() => ShortcutService.Validate(bindings), "Enter cannot steal native button activation");
        });
        Case("checkbox-only selection and native checkbox Space", () =>
        {
            vm.Navigate("songs"); Pump(window);
            var list = window.FindControl<TrackListBox>("TracksList")!;
            if (!list.BatchMode) Click(window, window.FindControl<Button>("BatchEntryButton")!);
            list.SelectedItems!.Clear(); list.ScrollIntoView(vm.VisibleTracks[0]); Pump(window);
            var row = Row(window, vm.VisibleTracks[0]); var title = row.Children.OfType<TextBlock>().First(t => Grid.GetColumn(t) == 3);
            Click(window, title); Check(list.SelectedItems.Count == 0, "Song title cannot select");
            var box = row.Children.OfType<CheckBox>().Single(); Click(window, box, new Point(10, box.Bounds.Height / 2));
            Check(list.SelectedItems.Count == 1 && box.IsChecked == true, "Pointer checks exactly one song");
            box.Focus(); var playing = vm.IsPlaying; Press(window, Key.Space, PhysicalKey.Space);
            Check(list.SelectedItems.Count == 0 && box.IsChecked == false && vm.IsPlaying == playing, "Space toggles checkbox, never music");
            Click(window, title, null, MouseButton.Right); Check(ActiveMenu(window)?.IsOpen == true && list.SelectedItems.Count == 0, "Right click opens menu without selecting");
            ActiveMenu(window)!.Close(); list.Focus(); Press(window, Key.Down, PhysicalKey.ArrowDown); Check(list.SelectedItems.Count == 0, "Arrow navigation cannot create hidden selection");
            var all = window.FindControl<CheckBox>("SelectionToggle")!; all.Focus(); Press(window, Key.Space, PhysicalKey.Space);
            Check(list.SelectedItems.Count == vm.VisibleTracks.Count, "Toolbar checkbox Space selects all");
            Press(window, Key.Space, PhysicalKey.Space); Check(list.SelectedItems.Count == 0, "Same checkbox Space clears all");
            Click(window, window.FindControl<Button>("BatchEntryButton")!); Check(!list.BatchMode && list.SelectedItems.Count == 0, "Exit removes batch selection");
        });
        Case("one pointer click replaces mode/more menu and volume flyout", () =>
        {
            var mode = window.FindControl<Button>("PlayerMode")!; var more = window.FindControl<Button>("PlayerMore")!; var volume = window.FindControl<Button>("PlayerVolume")!;
            Click(window, mode); var first = ActiveMenu(window); Check(first?.IsOpen == true, "Mode menu opens");
            Click(window, more); var second = ActiveMenu(window); Check(first?.IsOpen == false && second?.IsOpen == true && second != first, "One click replaces mode with more");
            Click(window, volume); Check(second?.IsOpen == false && volume.Flyout?.IsOpen == true, "One click replaces more with volume");
            Click(window, mode); Check(volume.Flyout?.IsOpen == false && ActiveMenu(window)?.IsOpen == true, "One click replaces volume with mode (flyout=" + volume.Flyout?.IsOpen + ", active=" + ActiveMenu(window)?.IsOpen + ")");
            vm.Navigate("statistics"); Pump(window); Check(ActiveMenu(window) is null, "Page navigation closes active context menu");
        });
        Case("title safe-interior motion restores all resize edge hit tests", () =>
        {
            var edges = ((Grid)window.Content!).Children.OfType<Border>().Where(b => b.Tag is string).ToArray();
            Check(edges.Length == 8, "All eight native resize regions exist");
            typeof(MainWindow).GetField("_awaitingMovePointer", PrivateInstance)!.SetValue(window, true);
            foreach (var edge in edges) { edge.IsHitTestVisible = false; edge.Cursor = new Cursor(StandardCursorType.Arrow); }
            window.Cursor = new Cursor(StandardCursorType.Arrow); window.MouseMove(new Point(60, 80)); Pump(window);
            Check(edges.All(e => e.IsHitTestVisible) && window.Cursor is null, "Released pointer in interior restores all edge hit tests and inherited cursor");
            window.MouseMove(new Point(2, window.Bounds.Height / 2)); Pump(window);
            Check(edges.First(e => (string)e.Tag! == "Left").Cursor?.ToString() != "Arrow", "Restored edge has original resize cursor");
        });
        Case("shortcut capture key routing and minimum-window row geometry", () =>
        {
            vm.Navigate("settings"); Pump(window);
            ExpandSettings(window, "快捷键");
            var capture = window.GetVisualDescendants().OfType<ShortcutCaptureButton>().First(); capture.BringIntoView(); Pump(window);
            Click(window, capture); Check(capture.IsCapturing, "Click enters capture"); var playing = vm.IsPlaying;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Pump(window);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Pump(window);
            Check(!capture.IsCapturing && capture.Content?.ToString() == "Space" && vm.IsPlaying == playing, $"Captured Space never controls player (focused={capture.IsFocused}, capturing={capture.IsCapturing}, content={capture.Content}, play={vm.IsPlaying}/{playing})");
            Click(window, capture); Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control); Check(capture.Content?.ToString() == "Ctrl+K", "Real Ctrl+K captured");
            Click(window, capture); Press(window, Key.Escape, PhysicalKey.Escape); Check(!capture.IsCapturing && capture.Content?.ToString() == "Ctrl+K", "Escape cancels without changing captured gesture");
            Click(window, capture); Pump(window); var row = capture.GetVisualAncestors().OfType<Grid>().First();
            var p = capture.TranslatePoint(default, row)!.Value; var clear = row.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "清空"); var clearP = clear.TranslatePoint(default, row)!.Value;
            Console.WriteLine($"MEASURE capture row={row.Bounds.Width:0.0} capture=[{p.X:0.0},{p.X + capture.Bounds.Width:0.0}] clear=[{clearP.X:0.0},{clearP.X + clear.Bounds.Width:0.0}]");
            Check(clearP.X >= p.X + capture.Bounds.Width - .5 && clearP.X + clear.Bounds.Width <= row.Bounds.Width + .5, "Capture and clear buttons fit their row at 980px width");
            Press(window, Key.Escape, PhysicalKey.Escape);
        });
        Case("shortcut capture active label and clear button stay within minimum-window card", () =>
        {
            vm.Navigate("settings"); Pump(window);
            ExpandSettings(window, "快捷键");
            var capture = window.GetVisualDescendants().OfType<ShortcutCaptureButton>().First(); capture.BringIntoView(); Pump(window); Click(window, capture); Pump(window);
            var row = capture.GetVisualAncestors().OfType<Grid>().First(); var p = capture.TranslatePoint(default, row)!.Value;
            var clear = row.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "清空"); var clearP = clear.TranslatePoint(default, row)!.Value;
            Console.WriteLine($"MEASURE active capture row={row.Bounds.Width:0.0} capture=[{p.X:0.0},{p.X + capture.Bounds.Width:0.0}] clear=[{clearP.X:0.0},{clearP.X + clear.Bounds.Width:0.0}]");
            Check(clearP.X >= p.X + capture.Bounds.Width - .5 && clearP.X + clear.Bounds.Width <= row.Bounds.Width + .5, "Capture label cannot push clear outside row/card");
            Press(window, Key.Escape, PhysicalKey.Escape);
        });
        Case("RGB/wheel/brightness/Hex committed input and direct final apply", () =>
        {
            var result = ColorPickerDialog.Show(window, "#123456"); Pump(window);
            var dialog = window.OwnedWindows.OfType<ColorPickerDialog>().Single(); Pump(dialog);
            var red = dialog.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "ColorRed");
            var green = dialog.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "ColorGreen");
            var blue = dialog.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "ColorBlue");
            var hex = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Width == 130);
            var wheel = dialog.GetVisualDescendants().OfType<ColorWheel>().Single();
            Edit(dialog, red.GetVisualDescendants().OfType<TextBox>().Single(), "128"); hex.Focus(); Pump(dialog);
            Check(red.Value == 128 && wheel.SelectedColor.R == 128 && hex.Text == "#803456", "Typed RGB commits to wheel and hex");
            Click(dialog, wheel, new Point(wheel.Bounds.Width / 2 + 70, wheel.Bounds.Height / 2)); Check(hex.Text == ColorPickerDialog.Hex(wheel.SelectedColor), "Actual wheel pointer updates RGB and Hex");
            var brightness = dialog.GetVisualDescendants().OfType<Slider>().Single(); Click(dialog, brightness, new Point(brightness.Bounds.Width / 2, brightness.Bounds.Height / 2));
            Check(hex.Text == ColorPickerDialog.Hex(wheel.SelectedColor) && (double)(red.Value ?? -1) == wheel.SelectedColor.R, "Pointer brightness slider synchronizes channels");
            Edit(dialog, hex, "#00FF7F"); Check(wheel.SelectedColor != Color.Parse("#00FF7F"), "Hex remains unapplied during editing");
            Press(dialog, Key.Enter, PhysicalKey.Enter); Check(wheel.SelectedColor == Color.Parse("#00FF7F") && red.Value == 0 && green.Value == 255 && blue.Value == 127 && !hex.IsFocused, "Enter commits Hex, synchronizes wheel/RGB and releases focus");
            var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "应用颜色");
            Console.WriteLine($"MEASURE color dialog={dialog.Bounds.Width:0.0}x{dialog.Bounds.Height:0.0}; owner={window.Bounds.Width:0.0}x{window.Bounds.Height:0.0}");
            Check(dialog.Bounds.Height <= window.Bounds.Height && apply.TranslatePoint(default, dialog)!.Value.Y + apply.Bounds.Height <= dialog.Bounds.Height, "Color final action fits minimum owner at 96DPI");
            using (var frame = dialog.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "color-picker-interaction.png"), PngBitmapEncoderOptions.Default);
            Edit(dialog, hex, "#00FF7F"); Click(dialog, apply); Check(result.IsCompletedSuccessfully && result.Result == "#00FF7F", "Final Apply accepts directly typed Hex without intermediate button");
            result = ColorPickerDialog.Show(window, "#123456"); Pump(window); dialog = window.OwnedWindows.OfType<ColorPickerDialog>().Single(); Pump(dialog);
            hex = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Width == 130); Edit(dialog, hex, "invalid");
            Press(dialog, Key.Enter, PhysicalKey.Enter);
            Check(!result.IsCompleted && dialog.IsVisible && hex.Text == "#123456" && !hex.IsFocused, "Invalid Hex is not applied; the last valid color and open dialog are retained while focus is released"); dialog.Close();
        });
        Case("settings path links and nonexistent target safety", () =>
        {
            vm.Navigate("settings"); Pump(window);
            ExpandSettings(window, "歌词", "数据与恢复", "自定义 UI");
            var links = window.GetVisualDescendants().OfType<Button>().Where(b => ToolTip.GetTip(b)?.ToString()?.Contains(L10n.T("Common.OpenLocation")) == true).ToArray();
            Check(links.Length >= 4 && links.All(b => b.Content is TextBlock { TextWrapping: TextWrapping.Wrap } text && ToolTip.GetTip(b)!.ToString()!.Contains(text.Text!)), "Lyric/layout/data/backup paths are full clickable wrapped labels with full-path tooltips");
            Reject(() => MainWindow.OpenPath(Path.Combine(output, "not-created-" + Guid.NewGuid().ToString("N"))), "Missing path fails before shell launch");
        });
        Case("rejected setting toggle restores state and reports instead of throwing", () =>
        {
            var factory = typeof(SettingsView).Assembly.GetType("NonetMusicPlayer.Desktop.Views.Ui")!.GetMethod("Toggle", BindingFlags.Public | BindingFlags.Static)!;
            var box = (ToggleSwitch)factory.Invoke(null, [false, (Action<bool>)(_ => throw new InvalidOperationException("正在复制数据目录，请稍后再修改插件。"))])!;
            var host = new Window { DataContext = vm, Content = box, Width = 260, Height = 120 };
            var reported = false;
            EventHandler<UserNotificationEventArgs> notice = (_, args) => reported |= args.Message.Contains("复制数据目录");
            vm.UserNotification += notice;
            try { host.Show(); Pump(host); box.IsChecked = true; Check(box.IsChecked == false && reported, "Rejected toggle rolls back and surfaces its error"); }
            finally { vm.UserNotification -= notice; host.Close(); }
        });
        vm.Navigate("songs"); Pump(window); window.Close();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }
    private static Grid Row(MainWindow window, object track) => window.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("track-row") && ReferenceEquals(g.DataContext, track));
    private static void ExpandSettings(Window window, params string[] names)
    {
        foreach (var expander in window.GetLogicalDescendants().OfType<Expander>().Where(e => names.Contains(e.Header?.ToString(), StringComparer.Ordinal))) expander.IsExpanded = true;
        Pump(window);
    }
    private static ContextMenu? ActiveMenu(MainWindow window) => (ContextMenu?)typeof(MainWindow).GetField("_activeMenu", PrivateInstance)!.GetValue(window);
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None) { window.KeyPress(key, modifiers, physical, null); window.KeyRelease(key, modifiers, physical, null); Pump(window); }
    private static void Edit(Window window, TextBox input, string value) { input.Focus(); input.SelectAll(); window.KeyTextInput(value); Pump(window); }
    private static void Click(Window window, Control control, Point? local = null, MouseButton button = MouseButton.Left)
    { var p = control.TranslatePoint(local ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value; window.MouseDown(p, button); window.MouseUp(p, button); Pump(window); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is InvalidDataException or DirectoryNotFoundException) { return; } throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(100); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; } public void Dispose() { }
    }
}
