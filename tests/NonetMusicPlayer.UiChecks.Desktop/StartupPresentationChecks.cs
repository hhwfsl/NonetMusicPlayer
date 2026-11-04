using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>记录 DWM 真正可见后的外框和首帧像素，不能仅凭 Opened 或离屏截图判断启动稳定。</summary>
internal static class StartupPresentationChecks
{
    public static void Run(string output, string? styleFile)
    {
        var records = new List<object>();
        foreach (var theme in new[] { "Dark", "Light" })
        foreach (var minimum in new[] { false, true })
        {
            var storage = new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")));
            using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer());
            vm.Settings.CloseToTray = vm.Settings.ConfirmClose = false; vm.Settings.Theme = theme;
            vm.Settings.WindowWidth = minimum ? 480 : 950; vm.Settings.WindowHeight = minimum ? 320 : 610;
            var window = new MainWindow { DataContext = vm, ShowActivated = true, Title = "Nonet · 启动呈现验收" };
            if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("startup-fixture", [StyleSheetContract.Read(input)]); }
            var handle = window.TryGetPlatformHandle()!.Handle;
            try
            {
                Require(Cloaked(handle), "Native surface is withheld before first Show");
                window.PrepareInitialAppearance(); Require(!window.IsVisible, "Appearance preparation never exposes the window");
                window.Show();
                var deadline = Environment.TickCount64 + 5000;
                while (Cloaked(handle) && Environment.TickCount64 < deadline) Pump(10);
                Require(!Cloaked(handle), "Real compositor frame reveals the window without a fixed delay or deadlock");
                GetWindowRect(handle, out var first);
                var screen = window.Screens.Primary!;
                var expected = WindowPlacementService.CenterPosition(screen.WorkingArea, window.FrameSize ?? window.ClientSize, window.DesktopScaling);
                Require(Math.Abs(first.Left - expected.X) <= 1 && Math.Abs(first.Top - expected.Y) <= 1, "First exposed frame is already centered using its final native size");
                Require(window.ClientSize.Width >= window.MinWidth && window.ClientSize.Height >= window.MinHeight, "First exposed frame respects 640x480 minimum and work-area fitting");
                // 在客户区内容边缘取样，深色启动不允许展示整块白色底面；只记录隔离测试窗口。
                var surface = window.FindControl<Border>("ContentSurface")!;
                var point = surface.TranslatePoint(new Point(4, 4), window)!.Value;
                var dc = GetDC(handle); uint pixel;
                try { pixel = GetPixel(dc, (int)(point.X * window.RenderScaling), (int)(point.Y * window.RenderScaling)); }
                finally { ReleaseDC(handle, dc); }
                Require(pixel != uint.MaxValue, "First exposed native client pixel is readable");
                if (theme == "Dark") Require((pixel & 255) < 220 && ((pixel >> 8) & 255) < 220 && ((pixel >> 16) & 255) < 220, $"Dark startup never reveals a white client surface: {pixel:X6}");
                for (var i = 0; i < 25; i++)
                {
                    Pump(20); GetWindowRect(handle, out var next);
                    Require(next.Equals(first), "Window never moves or resizes after its first exposed frame");
                }
                // 首次打开的呈现保护仅运行一次，托盘显示沿用用户本次进程的位置。
                window.Position = new(first.Left + 35, first.Top + 20); var moved = window.Position;
                window.Hide(); window.Show(); Pump(80);
                Require(!Cloaked(handle) && window.Position == moved, "Tray-style reopening does not cloak or recenter again");
                records.Add(new { theme, minimum, window.ClientSize, first, pixel });
            }
            finally { window.ExitApplication(); }
        }
        // 用户在首帧到来前退出，不应留下异步恢复、重新显示或访问已销毁窗口的回调。
        using (var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(output, "closed-early-" + Guid.NewGuid().ToString("N")))), new ApplicationIntegrationChecks.SilentAudioPlayer()))
        {
            vm.Settings.CloseToTray = vm.Settings.ConfirmClose = false;
            var window = new MainWindow { DataContext = vm, ShowActivated = false };
            window.Show(); window.ExitApplication(); Pump(100);
            Require(!window.IsVisible, "Closing before the first frame never reopens the window");
        }
        File.WriteAllText(Path.Combine(output, "frames.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS real first-frame reveal, dark/light and saved/minimum size, native pixel, centered first exposure, no later move/resize, retained tray position");
        Console.WriteLine(File.ReadAllText(Path.Combine(output, "result.txt")));
    }
    private static void Pump(int milliseconds) { var frame = new DispatcherFrame(); using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(milliseconds)); Dispatcher.UIThread.PushFrame(frame); }
    private static bool Cloaked(nint handle) { if (DwmGetWindowAttribute(handle, 14, out var value, sizeof(int)) != 0) throw new InvalidOperationException("DWM state unavailable"); return value != 0; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}
