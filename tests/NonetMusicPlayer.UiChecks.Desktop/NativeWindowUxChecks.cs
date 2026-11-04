using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>使用真实 Win32 窗口验证标题命中、原生移动循环与贴靠协议，不接触用户运行实例。</summary>
internal static class NativeWindowUxChecks
{
    public static void Run(string output, string? styleFile, bool preview)
    {
        var storage = new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer());
        vm.Settings.CloseToTray = vm.Settings.ConfirmClose = false; vm.Settings.Theme = "Dark";
        var window = new MainWindow { DataContext = vm, Title = "Nonet · 窗口交互验收", Width = 960, Height = 720, ShowActivated = false };
        if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("native-ux-fixture", [StyleSheetContract.Read(input)]); }
        window.PrepareInitialAppearance(); window.Show(); Pump(120);
        try
        {
            var handle = window.TryGetPlatformHandle()!.Handle;
            var style = unchecked((uint)GetWindowLong(handle, -16));
            Require((style & 0x000F0000u) == 0x000F0000u, "Native system menu, resize, minimize and maximize capabilities remain available");
            // Windows 实际使用的最小跟踪尺寸也必须遵守下限，不能只改 XAML 的属性值。
            var trackingBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
            try
            {
                Marshal.StructureToPtr(default(NativeMinMaxInfo), trackingBuffer, false);
                SendMessage(handle, 0x0024, 0, trackingBuffer);
                var tracking = Marshal.PtrToStructure<NativeMinMaxInfo>(trackingBuffer);
                Require(tracking.MinTrackSize.X >= Math.Floor(window.MinWidth * window.RenderScaling) && tracking.MinTrackSize.Y >= Math.Floor(window.MinHeight * window.RenderScaling), "Native resize tracking honors the restored minimum at current DPI");
            }
            finally { Marshal.FreeHGlobal(trackingBuffer); }
            foreach (var left in new[] { false, true })
            {
                vm.Settings.TitleButtonsOnLeft = left; vm.ApplySettings(); Pump(50);
                var max = window.FindControl<Button>("MaximizeWindowButton")!;
                var point = max.PointToScreen(new Rect(max.Bounds.Size).Center);
                var hit = SendMessage(handle, 0x0084, 0, Packed(point.X, point.Y));
                Require(hit == 9, $"Maximize button reports HTMAXBUTTON at either position: result={hit}, client={window.PointToClient(point)}, bounds={max.Bounds}, input={window.InputHitTest(window.PointToClient(point))}, visible={max.IsEffectivelyVisible}, enabled={max.IsEnabled}");
                Require(Hit(window.FindControl<Button>("CloseWindowButton")!) == 1 && Hit(window.FindControl<Button>("MinimizeWindowButton")!) == 1, "Close and minimize remain clickable client controls at either position");
                Require(Hit(window.FindControl<TextBlock>("ApplicationNameLabel")!) == 2, "Application name participates in native HTCAPTION hit testing");
                var badge = window.FindControl<StackPanel>("TitleBrand")!.Children.OfType<Button>().Single(b => b.Name == "NewVersionBadge");
                badge.IsVisible = true; Pump(20);
                Require(Hit(badge) == 1, "Dynamically created New button remains interactive inside native caption");
                badge.IsVisible = false;
                var extensions = window.FindControl<Button>("TitleExtensionsButton")!; extensions.IsVisible = true; Pump(20);
                Require(Hit(extensions) == 1, "Plugin title action and its icon remain client hit targets");
                extensions.IsVisible = false;
                // 后加的插件按钮也应自动排除原生拖动，不能依靠固定按钮名单。
                // 小型图标夹具避免挤出清蓝侧栏品牌区域，只验证可见控件的原生命中。
                var dynamicButton = new Button { Content = "+", Width = 28, Height = 28, MinWidth = 0, Padding = new(0) };
                window.FindControl<StackPanel>("TitleBrand")!.Children.Add(dynamicButton); Pump(20);
                var dynamicHit = Hit(dynamicButton);
                Require(dynamicHit == 1, $"Later plugin buttons do not become draggable caption regions: hit={dynamicHit}, button={dynamicButton.Bounds}, parent={(dynamicButton.Parent as Visual)?.Bounds}, window={window.ClientSize}");
                window.FindControl<StackPanel>("TitleBrand")!.Children.Remove(dynamicButton);
            }
            GetCursorPos(out var cursor);
            var entered = 0; var exited = 0; var zeroReleases = 0; var clientPresses = 0;
            window.FindControl<Border>("TitleBarSurface")!.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (e.Source is Visual source && !source.GetSelfAndVisualAncestors().Any(v => v is Button)) clientPresses++;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            Win32Properties.CustomWndProcHookCallback observer = (nint hwnd, uint message, nint wp, nint lp, ref bool handled) =>
            {
                if (message == 0x0231) entered++;
                if (message == 0x0232) exited++;
                if (message == 0x0202 && lp == 0) zeroReleases++;
                return 0;
            };
            Win32Properties.AddWndProcHookCallback(window, observer);
            try
            {
                // 自动验收只查询原生命中，不把缺少真实按键状态的 SendMessage 伪拖动当作实测。
                foreach (var x in new[] { 420, 520, 620 })
                {
                    window.Position = new(cursor.X - (int)(x * window.RenderScaling), cursor.Y - (int)(25 * window.RenderScaling)); Pump(30);
                    Require(SendMessage(handle, 0x0084, 0, Packed(cursor.X, cursor.Y)) == 2, "Empty title region is natively draggable");
                }
                // 双击直接走 Windows 标题栏协议，避免客户端再次切换造成还原/最大化互相抵消。
                SendMessage(handle, 0x00A3, 2, Packed(cursor.X, cursor.Y)); Pump(80);
                Require(window.WindowState == WindowState.Maximized, "Native caption double-click maximizes exactly once");
                var titlePoint = window.PointToScreen(new Point(420, 25));
                SendMessage(handle, 0x00A3, 2, Packed(titlePoint.X, titlePoint.Y)); Pump(80);
                Require(window.WindowState == WindowState.Normal, "Native caption double-click restores exactly once");
                SendMessage(handle, 0x0112, 0xF030, 0); Pump(80);
                Require(window.WindowState == WindowState.Maximized, "Native maximize command is honored");
                SendMessage(handle, 0x0112, 0xF120, 0); Pump(80);
                Require(window.WindowState == WindowState.Normal, "Native restore command is honored");
                var area = window.Screens.ScreenFromWindow(window)!.WorkingArea;
                // 仅在分区容得下既有最小尺寸时验收，不为四分屏降低窗口的 640×480 下限。
                var zoneWidth = area.Width / 2; var zoneHeight = area.Height / 2;
                if (zoneWidth / window.RenderScaling >= window.MinWidth + 20 && zoneHeight / window.RenderScaling >= window.MinHeight + 20)
                {
                    SetWindowPos(handle, 0, area.X, area.Y, zoneWidth, zoneHeight, 0x0014); Pump(80);
                    GetWindowRect(handle, out var rect);
                    Require(rect.Right - rect.Left == zoneWidth && rect.Bottom - rect.Top == zoneHeight, "Window fits a native quarter-screen zone");
                    Require(window.ClientSize.Width >= window.MinWidth && window.ClientSize.Height >= window.MinHeight, "Quarter-screen zone retains the established client minimum");
                }
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS HTCAPTION, caption button exclusions, native double-click, HTMAXBUTTON, native styles and maximize/restore; physical mouse movement requires interactive check");
                Console.WriteLine(File.ReadAllText(Path.Combine(output, "result.txt")));
                if (preview)
                {
                    // 只记录真实输入的原生循环与悬停，供桌面自动化拖动后核对，不向产品注入消息。
                    window.Position = new(200, 150); window.Activate();
                    using var timer = DispatcherTimer.Run(() =>
                    {
                        GetCursorPos(out var actual);
                        File.WriteAllText(Path.Combine(output, "interactive.json"), JsonSerializer.Serialize(new
                        {
                            entered, exited, zeroReleases, clientPresses,
                            position = new { window.Position.X, window.Position.Y },
                            cursor = new { actual.X, actual.Y },
                            hovered = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsPointerOver).Select(b => b.Name ?? b.Tag?.ToString() ?? "button").ToArray(),
                            resizeEdges = ((Grid)window.Content!).Children.OfType<Border>().Where(b => b.Tag is string).All(b => b.IsHitTestVisible)
                        }));
                        return true;
                    }, TimeSpan.FromMilliseconds(200));
                    Pump(600000);
                }
            }
            finally { Win32Properties.RemoveWndProcHookCallback(window, observer); }
        }
        finally { window.ExitApplication(); }
        nint Hit(Control control) { var point = control.PointToScreen(new Rect(control.Bounds.Size).Center); return SendMessage(window.TryGetPlatformHandle()!.Handle, 0x0084, 0, Packed(point.X, point.Y)); }
        void Pump(int milliseconds) { var frame = new DispatcherFrame(); EventHandler closed = (_, _) => frame.Continue = false; window.Closed += closed; using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(milliseconds)); Dispatcher.UIThread.PushFrame(frame); window.Closed -= closed; window.UpdateLayout(); }
    }
    private static nint Packed(int x, int y) => (nint)((y & 0xffff) << 16 | (x & 0xffff));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
}
