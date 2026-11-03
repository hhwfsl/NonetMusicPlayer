using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>覆盖触摸真实事件路由、DPI 换算和最小窗口中各操作控件的完整命中区域。</summary>
internal static class TouchResponsiveChecks
{
    private static bool _native;
    public static void Run(string output, string? styleFile, bool native = false)
    {
        _native = native;
        var oldMotion = Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION");
        Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
        try { ResizeInput(); Layouts(output, styleFile); }
        finally { Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", oldMotion); }
        Console.WriteLine("PASS touch/pen window resize, DPI/minimum bounds, responsive toolbars and complete player controls" + (styleFile is null ? "" : ", external style"));
    }
    private static void ResizeInput()
    {
        foreach (var scale in new[] { 1d, 1.5, 2 })
        {
            var result = TouchWindowResizer.Resize(new(400, 300), new(800, 600), WindowEdge.NorthWest, new(40 * scale, 30 * scale), scale, new(640, 480), new(1200, 900));
            Require(result.Size == new Size(760, 570) && result.Position == new PixelPoint(400 + (int)(40 * scale), 300 + (int)(30 * scale)), "North-west resize respects logical size and physical position at " + scale);
            result = TouchWindowResizer.Resize(new(400, 300), new(800, 600), WindowEdge.NorthWest, new(2000, 2000), scale, new(640, 480), new(1200, 900));
            Require(result.Size == new Size(640, 480) && result.Position == new PixelPoint(400 + (int)(160 * scale), 300 + (int)(120 * scale)), "Minimum size fixes opposite edges at " + scale);
        }
        var window = new MainWindow { Width = 900, Height = 650, ShowActivated = false, ShowInTaskbar = false }; window.Show(); Pump(window, 50);
        try
        {
            foreach (var type in new[] { PointerType.Touch, PointerType.Pen })
            foreach (var edge in Enum.GetValues<WindowEdge>())
            {
                window.Width = 900; window.Height = 650; window.Position = new(200, 120); Pump(window, 20);
                var point = edge switch {
                    WindowEdge.NorthWest => new Point(10, 10), WindowEdge.North => new(450, 10), WindowEdge.NorthEast => new(890, 10),
                    WindowEdge.West => new(10, 325), WindowEdge.East => new(890, 325),
                    WindowEdge.SouthWest => new(10, 640), WindowEdge.South => new(450, 640), _ => new Point(890, 640) };
                using var pointer = new Pointer(Pointer.GetNextFreeId(), type, true);
                var pressed = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed);
                var start = window.PointToScreen(point);
                var begin = new PointerPressedEventArgs(window, pointer, window, point, 10, pressed, KeyModifiers.None);
                window.RaiseEvent(begin);
                Require(begin.Handled && ReferenceEquals(pointer.Captured, window), type + " captures " + edge + " beyond the mouse-only five-pixel border");
                var moved = new PixelPoint(start.X + 30, start.Y + 20);
                window.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, window, pointer, window, window.PointToClient(moved), 20, pressed, KeyModifiers.None));
                Pump(window, 20);
                Require(window.Width != 900 || window.Height != 650, type + " changes window dimensions at " + edge);
                var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
                window.RaiseEvent(new PointerReleasedEventArgs(window, pointer, window, window.PointToClient(moved), 30, released, KeyModifiers.None, MouseButton.Left));
                Require(pointer.Captured is null, "Release clears resize capture");
            }
            // 控件即使落入扩大的边缘命中带，仍应接收原本的指针操作。
            var titleButton = window.FindControl<Button>("MinimizeWindowButton")!;
            using (var buttonPointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true))
            {
                var point = titleButton.TranslatePoint(new Point(titleButton.Bounds.Width / 2, 1), window)!.Value;
                titleButton.RaiseEvent(new PointerPressedEventArgs(titleButton, buttonPointer, window, point, 35, new(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
                Require(!ReferenceEquals(buttonPointer.Captured, window), "Touch resize does not steal title button input");
                buttonPointer.Capture(null);
            }
            using (var cancelled = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true))
            {
                window.RaiseEvent(new PointerPressedEventArgs(window, cancelled, window, new(10, 100), 36, new(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
                Require(ReferenceEquals(cancelled.Captured, window), "Resize starts before capability changes");
                window.CanResize = false; Require(cancelled.Captured is null, "Disabling resize releases active capture"); window.CanResize = true;
            }
            window.WindowState = WindowState.Maximized; Pump(window, 20);
            using var blocked = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
            window.RaiseEvent(new PointerPressedEventArgs(window, blocked, window, new(10, 10), 40, new(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            Require(blocked.Captured is null, "Maximized window cannot start a touch resize");
        }
        finally { window.Close(); }
    }
    private static void Layouts(string output, string? styleFile)
    {
        var storage = new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var tracks = Enumerable.Range(0, 24).Select(i => new TrackItem("responsive-" + i, "Sister's noise · 很长的歌曲标题 " + i, "fripSide / 艺术家", "Album", "unused.wav", ".wav", 262)).ToArray();
        vm.State.Tracks.AddRange(tracks); var playlist = vm.CreatePlaylist("我喜欢 · 响应式布局"); vm.AddToPlaylist(playlist, tracks);
        var window = new MainWindow { DataContext = vm, Width = 960, Height = 720, ShowActivated = false, ShowInTaskbar = false }; window.Show(); Pump(window, 80);
        try
        {
            if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("responsive-fixture", [StyleSheetContract.Read(input)]); }
            var playing = vm.PlayTrackAsync(tracks[0]); while (!playing.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } playing.GetAwaiter().GetResult();
            var cases = new[] { (640,480,false,14), (640,480,true,14), (640,480,true,22), (800,600,true,22), (1000,700,true,14), (1360,860,false,14), (480,600,true,14) };
            foreach (var (width, height, touch, font) in cases)
            {
                window.MinWidth = 320; window.MinHeight = 320; window.Width = width; window.Height = height;
                vm.Settings.TouchMode = touch; vm.Settings.FontSize = font; vm.ApplySettings();
                vm.Navigate("playlist:" + playlist.Id); Pump(window, 120);
                var label = $"{width}x{height}-touch{touch}-font{font}";
                PlayerFits(window, label, touch);
                NavigationFits(window, label);
                var flow = window.FindControl<Panel>("LibraryActionsFlow")!;
                Require(flow is WrapPanel, "Existing WrapPanel references remain compatible");
                foreach (var control in flow.Children.Where(c => c.IsVisible)) Fits(control, flow, label + "/toolbar");
                var actions = flow.Children.OfType<Button>().Where(b => b.IsVisible).ToArray();
                if (width >= 640) Require(actions.All(b => Math.Abs(b.Bounds.Y - actions[0].Bounds.Y) < 1), "No isolated trailing toolbar button at " + label);
                if (width == 640 && touch && font == 14) Save(window, Path.Combine(output, "playlist-minimum-touch.png"));
                var batch = window.FindControl<Button>("BatchEntryButton")!;
                batch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window, 50);
                foreach (var control in flow.Children.Where(c => c.IsVisible)) Fits(control, flow, label + "/batch-toolbar");
                var selection = window.FindControl<Panel>("SelectionToolbar")!;
                Fits(selection, window.FindControl<Control>("LibraryActions")!, label + "/selection");
                window.FindControl<ListBox>("TracksList")!.SelectedItems!.Add(tracks[0]); Pump(window, 25);
                foreach (var control in selection.Children.Where(c => c.IsVisible)) Fits(control, selection, label + "/partial-selection");
                window.FindControl<ListBox>("TracksList")!.SelectAll(); Pump(window, 25);
                foreach (var control in selection.Children.Where(c => c.IsVisible)) Fits(control, selection, label + "/all-selected");
                batch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                foreach (var page in new[] { "songs", "history", "albums", "artists", "playlists", "plugins", "statistics", "settings" })
                {
                    vm.Navigate(page); Pump(window, 45); PlayerFits(window, label + "/" + page, touch);
                    var heading = window.FindControl<Control>("PageHeading")!;
                    foreach (var control in heading.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Name is "CatalogSearchField" or "CatalogCreatePlaylist" or "PageBackToTop")) Fits(control, heading, label + "/" + page);
                }
                Console.WriteLine("PASS layout " + label);
            }
        }
        finally { window.Close(); }
    }
    private static void PlayerFits(MainWindow window, string label, bool touch)
    {
        var player = window.FindControl<Control>("PlayerSurface")!;
        var names = new[] { "PlayerArtwork", "PlayerTitle", "PlayerArtist", "PlayerPrevious", "PlayerPlay", "PlayerNext", "PlayerTime", "PlayerMode", "PlayerFavorite", "PlayerMore", "PlayerVolume", "PlayerDesktopLyrics", "PlayerQueue" };
        foreach (var name in names)
        {
            var control = window.FindControl<Control>(name)!; Require(control.IsEffectivelyVisible, name + " remains visible at " + label); Fits(control, player, label);
            if (touch && control is Button) Require(control.Bounds.Width >= 44 && control.Bounds.Height >= 44, name + " retains touch target at " + label);
        }
        var buttons = names.Select(n => window.FindControl<Control>(n)!).OfType<Button>().ToArray();
        for (var a = 0; a < buttons.Length; a++) for (var b = a + 1; b < buttons.Length; b++)
        {
            var first = new Rect(buttons[a].TranslatePoint(default, player)!.Value, buttons[a].Bounds.Size);
            var second = new Rect(buttons[b].TranslatePoint(default, player)!.Value, buttons[b].Bounds.Size);
            Require(!first.Intersects(second), $"Player buttons {buttons[a].Name}/{buttons[b].Name} do not overlap at {label}");
        }
    }
    private static void Fits(Control control, Control container, string label)
    {
        var point = control.TranslatePoint(default, container) ?? throw new InvalidOperationException(control.Name + " detached");
        Require(point.X >= -.5 && point.Y >= -.5 && point.X + control.Bounds.Width <= container.Bounds.Width + .5 && point.Y + control.Bounds.Height <= container.Bounds.Height + .5,
            $"{control.Name} fits {container.Name} at {label}: {point}, {control.Bounds.Size}, container {container.Bounds.Size}");
    }
    private static void NavigationFits(MainWindow window, string label)
    {
        var sidebar = window.FindControl<Control>("SidebarPanel")!;
        var rectangles = sidebar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("nav")).Select(button =>
        {
            var rect = new Rect(button.TranslatePoint(default, sidebar)!.Value, button.Bounds.Size).Intersect(new Rect(sidebar.Bounds.Size));
            foreach (var clip in button.GetVisualAncestors().OfType<Control>().TakeWhile(c => !ReferenceEquals(c, sidebar)).Where(c => c is ScrollViewer || c.ClipToBounds))
                rect = rect.Intersect(new Rect(clip.TranslatePoint(default, sidebar)!.Value, clip.Bounds.Size));
            return rect;
        }).Where(r => r.Width > 0 && r.Height > 0).ToArray();
        for (var a = 0; a < rectangles.Length; a++) for (var b = a + 1; b < rectangles.Length; b++)
            Require(!rectangles[a].Intersects(rectangles[b]), "Navigation buttons do not overlap at " + label);
    }
    private static void Save(Window window, string path)
    {
        if (!_native) { using var frame = window.CaptureRenderedFrame(); frame?.Save(path, PngBitmapEncoderOptions.Default); }
        else { using var frame = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, window.RenderScaling), new Vector(96 * window.RenderScaling, 96 * window.RenderScaling)); frame.Render(window); frame.Save(path, PngBitmapEncoderOptions.Default); }
    }
    private static void Pump(Window window, int ms)
    {
        if (_native)
        {
            var frame = new DispatcherFrame();
            using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(ms));
            Dispatcher.UIThread.PushFrame(frame); window.UpdateLayout();
        }
        else { var until = Environment.TickCount64 + ms; do { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); Thread.Sleep(3); } while (Environment.TickCount64 < until); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
