using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>在隔离数据中真实保存并重新加载尺寸；窗口位置始终由启动时的工作区重新计算。</summary>
internal static class WindowSizePreferenceChecks
{
    private static bool _native;
    public static void Run(string output, bool native = false, string? styleFile = null)
    {
        _native = native;
        Geometry();
        var root = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N"));
        Size remembered;
        using (var vm = Create(root))
        {
            // 用户可能已保存上一轮过小的窗口；冷启动必须按恢复后的下限约束并居中。
            vm.Settings.WindowWidth = 480; vm.Settings.WindowHeight = 320;
            var window = Open(vm, styleFile); Pump(window, 120); Centered(window, "first launch");
            var area = window.Screens.Primary!.WorkingArea.Size.ToSize(window.Screens.Primary.Scaling);
            var minimum = WindowPlacementService.MinimumSize(area);
            // 原生边框与 DPI 取整可能进一步增大跟踪高度；验证实际客户区不会低于既有下限。
            Require(window.MinWidth == minimum.Width && window.MinHeight == minimum.Height && window.ClientSize.Width >= minimum.Width && window.ClientSize.Height >= minimum.Height, $"Previously saved undersized window respects the restored minimum: actual={window.ClientSize}, minimum={minimum}");
            var target = WindowPlacementService.FitSize(new(950, 610), area);
            window.Width = target.Width; window.Height = target.Height; Pump(window, 800);
            remembered = window.ClientSize;
            var saved = new AppStorage(root).Load().Settings;
            SameSize(new(saved.WindowWidth ?? 0, saved.WindowHeight ?? 0), remembered, "Resize is persisted after debounce");
            window.Position = new(window.Position.X + 80, window.Position.Y + 40); Pump(window, 30);
            window.Close();
        }
        using (var vm = Create(root))
        {
            var window = Open(vm, styleFile); Pump(window, 120);
            SameSize(window.ClientSize, remembered, "Cold restart restores saved size"); Centered(window, "cold restart ignores last position");
            window.WindowState = WindowState.Maximized; Pump(window, 120);
            SameSize(new(vm.Settings.WindowWidth ?? 0, vm.Settings.WindowHeight ?? 0), remembered, "Maximize does not overwrite normal size");
            window.WindowState = WindowState.Minimized; Pump(window, 60); window.Close();
        }
        using (var vm = Create(root))
        {
            var window = Open(vm, styleFile); Pump(window, 120);
            Require(window.WindowState == WindowState.Normal, "Restart opens as a normal window");
            SameSize(window.ClientSize, remembered, "Maximized/minimized close keeps remembered dimensions"); Centered(window, "restart after maximized/minimized close");
            vm.Navigate("settings"); Pump(window, 120);
            var reset = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RestoreDefaultWindowSize");
            Require(reset.IsEnabled, "Window size reset is independent of interface ownership");
            window.Position = new(window.Position.X + 60, window.Position.Y + 30);
            window.WindowState = WindowState.Maximized; Pump(window, 80);
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window, 160);
            var screen = window.Screens.ScreenFromWindow(window)!;
            var expected = WindowPlacementService.StartupSize(screen.WorkingArea.Size.ToSize(screen.Scaling), null, null);
            Require(window.WindowState == WindowState.Normal, "Reset restores normal state without a window animation");
            SameSize(window.ClientSize, expected, "Settings button restores default size"); Centered(window, "settings reset");
            var saved = new AppStorage(root).Load().Settings;
            SameSize(new(saved.WindowWidth ?? 0, saved.WindowHeight ?? 0), expected, "Reset is saved immediately");
            // 托盘式隐藏/显示不算重新启动，运行期间的拖动位置仍然保留。
            window.Position = new(window.Position.X + 50, window.Position.Y + 20); var position = window.Position;
            window.Hide(); window.Show(); Pump(window, 80);
            Require(window.Position == position, "Reopening an existing hidden window does not recenter it");
            var small = WindowPlacementService.FitSize(new(880, 560), screen.WorkingArea.Size.ToSize(screen.Scaling));
            window.Width = small.Width; window.Height = small.Height; Pump(window, 25);
            remembered = window.ClientSize; window.Close();
        }
        using (var vm = Create(root))
        {
            var window = Open(vm, styleFile); Pump(window, 100);
            SameSize(window.ClientSize, remembered, "Closing before debounce still saves the latest size"); Centered(window, "final restart");
            var json = JsonSerializer.Serialize(vm.Settings, AppStorage.Json);
            using var document = JsonDocument.Parse(json);
            Require(document.RootElement.TryGetProperty("windowWidth", out _) && document.RootElement.TryGetProperty("windowHeight", out _), "Dimensions are serialized");
            Require(!document.RootElement.TryGetProperty("windowX", out _) && !document.RootElement.TryGetProperty("windowY", out _) && !document.RootElement.TryGetProperty("windowPosition", out _), "Main window position is not serialized");
            window.Close();
        }
        Console.WriteLine("PASS saved normal size, centered cold startup, settings reset, no position persistence, maximize/minimize and quick-close protection" + (_native ? ", native Windows" : ""));
    }
    private static void Geometry()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{}", AppStorage.Json)!;
        Require(old.WindowWidth is null && old.WindowHeight is null, "Old settings use automatic default size");
        old.WindowWidth = double.NaN; old.WindowHeight = 600; old.Validate();
        Require(old.WindowWidth is null && old.WindowHeight is null, "Malformed dimensions fall back to defaults");
        SameSize(WindowPlacementService.StartupSize(new(1280, 720), 900, 600), new(900, 600), "Valid preferences are used");
        SameSize(WindowPlacementService.StartupSize(new(1280, 720), 480, 320), new(640, 480), "Undersized preferences cannot lower the established 640x480 minimum");
        SameSize(WindowPlacementService.MinimumSize(new(600, 400)), new(568, 368), "Small displays retain the existing work-area fitting policy");
        SameSize(WindowPlacementService.StartupSize(new(1024, 700), 3000, 2000), new(1016, 692), "Preferences fit a smaller screen");
        Require(WindowPlacementService.CenterPosition(new(-1920, 40, 1920, 1040), new(960, 600), 1) == new PixelPoint(-1440, 260), "Negative monitor coordinates are supported");
        Require(WindowPlacementService.CenterPosition(new(200, 50, 1920, 1080), new(800, 600), 1.5) == new PixelPoint(560, 140), "Centering accounts for display scaling");
    }
    private static MainViewModel Create(string root)
    {
        var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(root)), new ApplicationIntegrationChecks.SilentAudioPlayer());
        vm.Settings.ConfirmClose = vm.Settings.CloseToTray = false;
        return vm;
    }
    private static MainWindow Open(MainViewModel vm, string? styleFile)
    {
        var window = new MainWindow { DataContext = vm, ShowActivated = false, ShowInTaskbar = false };
        if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("size-fixture", [StyleSheetContract.Read(input)]); }
        window.PrepareInitialAppearance(); window.Show();
        return window;
    }
    private static void Centered(MainWindow window, string label)
    {
        var screen = window.Screens.ScreenFromWindow(window)!;
        var expected = WindowPlacementService.CenterPosition(screen.WorkingArea, window.FrameSize ?? window.ClientSize, window.DesktopScaling);
        Require(Math.Abs(window.Position.X - expected.X) <= 1 && Math.Abs(window.Position.Y - expected.Y) <= 1, $"Centered {label}: actual={window.Position}, expected={expected}");
    }
    private static void SameSize(Size actual, Size expected, string message) => Require(Math.Abs(actual.Width - expected.Width) <= 1 && Math.Abs(actual.Height - expected.Height) <= 1, $"{message}: actual={actual}, expected={expected}");
    private static void Pump(Window window, int milliseconds)
    {
        // 运行真实消息循环，让尺寸保存计时器参与调度；RunJobs 无法验证防抖落盘。
        var frame = new DispatcherFrame();
        using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.PushFrame(frame); window.UpdateLayout();
        if (!_native) window.CaptureRenderedFrame()?.Dispose();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
