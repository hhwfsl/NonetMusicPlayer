using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Reflection;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>记录首次显示和连续改变尺寸时的校验提示，不能只检查最终截图里的控件边界。</summary>
internal static class LayoutValidationChecks
{
    public static void Run(string output, string? styleFile)
    {
        var storage = new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer());
        vm.Settings.FontSize = 13; vm.Settings.SidebarWidth = 193.66666666666677;
        vm.Settings.ConfirmClose = vm.Settings.CloseToTray = false;
        var phase = "startup"; var warnings = new List<string>();
        vm.UserNotification += (_, e) =>
        {
            if (e.IsWarning && (e.Message.Contains("player.", StringComparison.Ordinal) || e.Message.Contains("布局", StringComparison.Ordinal) || e.Message.Contains("播放栏", StringComparison.Ordinal)))
            { warnings.Add(phase + ": " + e.Message); Console.WriteLine("WARNING " + warnings[^1]); }
        };
        var window = new MainWindow { DataContext = vm, Width = 1152, Height = 619.2 };
        window.SetRenderScaling(1.5);
        if (styleFile is not null) { using var file = File.OpenRead(styleFile); window.ApplyInterfaceLayers("validation-fixture", [StyleSheetContract.Read(file)]); }
        window.PrepareInitialAppearance(); window.Show(); Pump(window, 100);
        try
        {
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                window.SetRenderScaling(scale); Pump(window, 30);
                foreach (var touch in new[] { false, true, false })
                {
                    phase = $"scale={scale}, touch={touch}"; vm.Settings.TouchMode = touch; vm.ApplySettings(); Pump(window, 30);
                    foreach (var width in new[] { 1360d, 1001.3, 1000, 999.3, 921.6, 800, 720.3, 640, 721.3, 1000, 1360 })
                    {
                        phase = $"scale={scale}, touch={touch}, width={width}";
                        window.Width = width; window.Height = width < 1000 ? 518.4 : 760; Pump(window, 30);
                    }
                }
            }
            if (warnings.Count > 0) throw new InvalidOperationException($"Valid layout produced {warnings.Count} warnings: " + warnings[0]);
            // 真正的大幅越界仍必须失败，不能把修复退化为关闭布局保护。
            window.ApplyInterfaceLayers("validation-fixture", []); window.Width = 1360; vm.Settings.TouchMode = true; vm.ApplySettings(); Pump(window, 50);
            var play = window.FindControl<Button>("PlayerPlay")!;
            var center = play.TranslatePoint(new Point(play.Bounds.Width / 2, play.Bounds.Height / 2), window)!.Value.X;
            if (Math.Abs(center - window.Bounds.Width / 2) > 1 / window.RenderScaling + 0.000001) throw new InvalidOperationException("Wide default player transport must stay centered");
            window.Width = 720; Pump(window, 50);
            var queue = window.FindControl<Button>("PlayerQueue")!; queue.Width = 100; window.UpdateLayout();
            var rejected = false;
            try { typeof(MainWindow).GetMethod("ValidateVisibleLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Genuine queue overflow must still be rejected");
        }
        finally { window.Close(); }
        if (warnings.Count > 0) throw new InvalidOperationException($"Valid layout produced {warnings.Count} warnings: " + warnings[0]);
        Console.WriteLine("PASS valid startup/resize/touch/DPI sequences never report a fallback; genuine overflow is still rejected");
    }
    private static void Pump(Window window, int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        do { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); Thread.Sleep(2); } while (Environment.TickCount64 < until);
    }
}
