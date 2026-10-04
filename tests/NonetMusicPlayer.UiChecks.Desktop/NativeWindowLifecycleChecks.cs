using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class NativeWindowLifecycleChecks
{
    public static void Run(string output)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows native integration probe");
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<NativeTestApp>().UsePlatformDetect().With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] }).WithInterFont().SetupWithLifetime(lifetime);
        var storage = new AppStorage(Path.Combine(output, "native-data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudio());
        vm.LyricLines.Add(new(0, "独立窗口与点击穿透测试", "第二行翻译"));
        var owner = new MainWindow { DataContext = vm, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Position = new(-10000, -10000) };
        lifetime.MainWindow = owner;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)); Exception? failure = null;
        Dispatcher.UIThread.Post(async () =>
        {
            DesktopLyricsWindow? lyrics = null;
            try
            {
                owner.Show(); owner.Position = new(-10000, -10000); await Task.Delay(180);
                Require(owner.TrayAvailable, "Native tray exporter available");
                // 激活不等于导航：原生失焦/恢复及最小化均应保留设置视口。
                owner.Width = 1100; owner.Height = 700; vm.Navigate("settings"); await Task.Delay(100); owner.UpdateLayout();
                var settingsViewer = owner.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
                owner.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<TextBox>().First().Focus();
                settingsViewer.Offset = new Vector(0, 500); await Task.Delay(80); var savedViewport = settingsViewer.Offset.Y;
                var ownerHandle = owner.TryGetPlatformHandle()!.Handle;
                SendMessage(ownerHandle, 0x0006, 0, 0); SendMessage(ownerHandle, 0x0006, 1, 0); await Task.Delay(120);
                Require(Math.Abs(settingsViewer.Offset.Y - savedViewport) < 2, "Native activation preserves settings scroll despite a focused input above it");
                owner.WindowState = WindowState.Minimized; await Task.Delay(80); owner.WindowState = WindowState.Normal; await Task.Delay(160); owner.UpdateLayout();
                var restoredViewer = owner.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
                Require(Math.Abs(restoredViewer.Offset.Y - savedViewport) < 2, "Native minimize/cache rebuild preserves settings scroll");
                vm.Navigate("library"); await Task.Delay(60);
                var applicationPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Nonet.exe"));
                WindowsIconRefreshService.RefreshIfChanged(storage.Root, applicationPath);
                var marker = Path.Combine(storage.Root, "Cache", "native-icon-revision.txt");
                Require(File.Exists(marker), "Targeted native shell icon refresh completed");
                var markerTime = File.GetLastWriteTimeUtc(marker); WindowsIconRefreshService.RefreshIfChanged(storage.Root, applicationPath);
                Require(File.GetLastWriteTimeUtc(marker) == markerTime, "Shell icon refresh is bounded to executable revisions");
                var tray = (TrayIcon)typeof(MainWindow).GetField("_tray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
                Require(tray.Menu!.Items.Count == 2, "Native tray menu has exactly open and exit");
                lyrics = (DesktopLyricsWindow)typeof(MainWindow).GetField("_desktopLyrics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
                lyrics.Toggle(); await Task.Delay(180);
                var handle = lyrics.TryGetPlatformHandle()!; Require(handle.HandleDescriptor == "HWND" && GetWindow(handle.Handle, 4) != owner.TryGetPlatformHandle()!.Handle && lyrics.Owner is null, "Lyrics HWND is not owned by main window");
                owner.WindowState = WindowState.Minimized; await Task.Delay(80); Require(IsWindowVisible(handle.Handle) && !IsIconic(handle.Handle), "Independent HWND survives main minimize");
                var region = CreateRectRgn(0, 0, 0, 0);
                try { Require(GetWindowRgn(handle.Handle, region) != 0 && !PtInRegion(region, 5, 5), "Non-lyric blank area excluded from native window region"); }
                finally { DeleteObject(region); }
                var line = lyrics.GetVisualDescendants().OfType<NonetMusicPlayer.Desktop.Controls.KaraokeLine>().First();
                var textPosition = lyrics.PointToScreen(line.TranslatePoint(line.TextBounds.Center, lyrics)!.Value);
                Require(SendMessage(handle.Handle, 0x84, 0, Packed(textPosition.X, textPosition.Y)) == 1, "Native text hit reveals frame");
                await Task.Delay(80); Require(lyrics.FrameVisible, "Native hover expands full interactive frame");
                var beforeWidth = lyrics.Width; var beforeHeight = lyrics.Height;
                foreach (var (name, dx, dy) in new[] { ("Right", 60, 0), ("Bottom", 0, 40) })
                {
                    var grip = lyrics.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DesktopLyricsResize" + name);
                    var gripPosition = lyrics.PointToScreen(grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), lyrics)!.Value);
                    Mouse(handle.Handle, 0x200, gripPosition, 0); Mouse(handle.Handle, 0x201, gripPosition, 1);
                    Require(GetCapture() == handle.Handle, "Native mouse press captures resize handle instead of starting window movement");
                    var destination = new PixelPoint(gripPosition.X + (int)(dx * lyrics.RenderScaling), gripPosition.Y + (int)(dy * lyrics.RenderScaling));
                    Mouse(handle.Handle, 0x200, destination, 1); Mouse(handle.Handle, 0x202, destination, 0); await Task.Delay(100);
                }
                Require(lyrics.Width >= beforeWidth + 58 && lyrics.Height >= beforeHeight + 38 && vm.Settings.DesktopLyricsWidth == lyrics.Width, "Actual Win32 mouse messages resize width/height and persist on release");
                Require(line.TextSize == 28, "Native resize preserves the explicit default font");
                var area = lyrics.Screens.ScreenFromWindow(lyrics)!.WorkingArea;
                foreach (var edge in new[] { "left", "right", "top", "bottom" })
                {
                    lyrics.Position = new PixelPoint(area.X + (area.Width - (int)(lyrics.Width * lyrics.RenderScaling)) / 2, area.Y + (area.Height - (int)(lyrics.Height * lyrics.RenderScaling)) / 2);
                    typeof(DesktopLyricsWindow).GetMethod("SetFrameVisible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(lyrics, [true]);
                    await Task.Delay(30);
                    var start = lyrics.PointToScreen(new Point(35, 25));
                    Mouse(handle.Handle, 0x201, start, 1); Require(GetCapture() == handle.Handle, "Lyrics move captures the mouse instead of entering native move loop");
                    var target = edge switch { "left" => new PixelPoint(area.X - 200, start.Y), "right" => new PixelPoint(area.Right + 200, start.Y), "top" => new PixelPoint(start.X, area.Y - 200), _ => new PixelPoint(start.X, area.Bottom + 200) };
                    // SendMessage does not move the real system cursor. Do not yield
                    // while captured, or unrelated OS-generated moves at the actual
                    // cursor position contradict the synthetic drag. Exercise the
                    // complete timer/render path synchronously between each move.
                    void RefreshFrame()
                    {
                        typeof(DesktopLyricsWindow).GetMethod("UpdateLyrics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(lyrics, null);
                        lyrics.UpdateLayout();
                        var nativeInput = typeof(DesktopLyricsWindow).GetField("_nativeInput", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(lyrics)!;
                        nativeInput.GetType().GetMethod("Refresh")!.Invoke(nativeInput, [false, lyrics.FrameVisible]);
                    }
                    Mouse(handle.Handle, 0x200, target, 1); RefreshFrame(); var clamped = lyrics.Position;
                    var changes = 0; var positions = new List<PixelPoint>(); EventHandler<PixelPointEventArgs> moved = (_, e) => { changes++; positions.Add(e.Point); }; lyrics.PositionChanged += moved;
                    try
                    {
                        for (var repeat = 0; repeat < 6; repeat++)
                        {
                            var more = edge switch { "left" => new PixelPoint(target.X - repeat * 30, target.Y), "right" => new PixelPoint(target.X + repeat * 30, target.Y), "top" => new PixelPoint(target.X, target.Y - repeat * 30), _ => new PixelPoint(target.X, target.Y + repeat * 30) };
                            Mouse(handle.Handle, 0x200, more, 1); RefreshFrame();
                            Require(lyrics.Position == clamped, $"Continuous outward motion never moves or snaps back at {edge}: expected {clamped}, actual {lyrics.Position}, work area {area}, pointer {more}; screens {string.Join(';', lyrics.Screens.All.Select(screen => screen.WorkingArea))}");
                        }
                        Require(changes == 0, $"No transient position events/jitter at {edge}: initial {clamped}, events {string.Join(';', positions)}, work area {area}");
                    }
                    finally { lyrics.PositionChanged -= moved; Mouse(handle.Handle, 0x202, target, 0); }
                    await Task.Delay(70); Require(lyrics.Position == clamped, "Settled release retains bounded position at " + edge);
                    Require(area.Contains(lyrics.Position) && lyrics.Position.X + lyrics.Bounds.Width * lyrics.RenderScaling <= area.Right + 1 && lyrics.Position.Y + lyrics.Bounds.Height * lyrics.RenderScaling <= area.Bottom + 1, "Lyrics remain entirely in work area");
                }
                var hadAttributes = GetLayeredWindowAttributes(handle.Handle, out var initialKey, out var initialAlpha, out var initialFlags);
                lyrics.LockLyrics(); await Task.Delay(80);
                var style = GetWindowLongPtr(handle.Handle, -20).ToInt64(); Require((style & (0x80000 | 0x20)) == (0x80000 | 0x20), "Locked HWND has layered and transparent click-through styles");
                var hasAttributes = GetLayeredWindowAttributes(handle.Handle, out var key, out var alpha, out var flags);
                Require(hadAttributes == hasAttributes && (!hasAttributes || key == initialKey && alpha == initialAlpha && flags == initialFlags), "Lock preserves Avalonia per-pixel rendering attributes without color-key override");
                var input = typeof(DesktopLyricsWindow).GetField("_nativeInput", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(lyrics)!;
                var surface = input.GetType().GetField("_surface", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(input)!;
                var pixelSize = (PixelSize)surface.GetType().GetField("_size", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
                var pointer = (nint)surface.GetType().GetField("_pixels", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(surface)!;
                var pixels = new byte[pixelSize.Width * pixelSize.Height * 4]; Marshal.Copy(pointer, pixels, 0, pixels.Length);
                var opaquePixels = 0;
                for (var offset = 0; offset < pixels.Length; offset += 4)
                {
                    var a = pixels[offset + 3]; if (a > 0) opaquePixels++;
                    Require(pixels[offset] <= a && pixels[offset + 1] <= a && pixels[offset + 2] <= a, "Native DIB pixels are correctly premultiplied");
                }
                Require(pixels[3] == 0 && opaquePixels > 100 && opaquePixels < pixels.Length / 4 * .15, "Native compositor receives text-only alpha, no opaque black rectangles");
                lyrics.HideLyrics(); Require(IsWindowVisible(handle.Handle), "Locked HWND cannot close");
                var content = (Control)lyrics.Content!; using (var screenshot = new RenderTargetBitmap(new PixelSize((int)lyrics.Width, (int)lyrics.Height))) { screenshot.Render(content); screenshot.Save(Path.Combine(output, "native-locked-lyrics.png"), PngBitmapEncoderOptions.Default); }
                lyrics.UnlockLyrics(); await Task.Delay(80); Require((GetWindowLongPtr(handle.Handle, -20).ToInt64() & 0x20) == 0, "Unlock restores native input");
                for (var i = 0; i < 4; i++)
                {
                    vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, "歌词窗口恢复检查 " + i + new string('字', i * 8), "Translation " + i));
                    lyrics.HideLyrics(); await Task.Delay(60); Require(!IsWindowVisible(handle.Handle), "Explicit hide stays hidden");
                    lyrics.Toggle(); await Task.Delay(120); Require(IsWindowVisible(handle.Handle) && !IsIconic(handle.Handle), "Player button restores existing lyrics HWND");
                    Require(lyrics.TryGetPlatformHandle()!.Handle == handle.Handle, "Show/hide reuses a single native lyrics window");
                    ShowWindow(handle.Handle, 6); await Task.Delay(1200); Require(!IsIconic(handle.Handle), "Unexpected native minimization recovers while lyrics enabled");
                    SetWindowPos(handle.Handle, -2, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); await Task.Delay(1200);
                    Require((GetWindowLongPtr(handle.Handle, -20).ToInt64() & 8) != 0, "Native topmost loss is recovered without activating window");
                    lyrics.LockLyrics(); await Task.Delay(70); lyrics.UnlockLyrics(); await Task.Delay(70);
                    Require(lyrics.GetVisualDescendants().OfType<NonetMusicPlayer.Desktop.Controls.KaraokeLine>().First().Text.Contains(i.ToString()), "Text stays current after lock/show/restore cycles");
                }
                lyrics.HideLyrics(); owner.RestoreFromTray(); await Task.Delay(80); owner.Close(); await Task.Delay(80); Require(!owner.IsVisible && owner.TrayAvailable, "Close hides to reachable tray");
                owner.RestoreFromTray(); Require(owner.IsVisible && ReferenceEquals(tray, typeof(MainWindow).GetField("_tray", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(owner)), "Tray open restores main window without duplicate icon");
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: independent HWND, minimize survival/recovery, topmost recovery, single-window hide/show cycles, eager text region, native mouse capture and resize/persistence, lock preserving alpha renderer, unlock, native tray exporter, hide/restore.");
                Console.WriteLine("PASS NATIVE BETA14: stable captured mouse movement at all four boundaries, targeted/idempotent shell icon refresh, HWND ownership, minimize/topmost recovery, native hit region, native resize/capture/persistence, alpha-safe lock cycles and single-window tray hide/restore");
            }
            catch (Exception error) { failure = error; }
            finally { lyrics?.Dispose(); vm.Settings.CloseToTray = false; owner.Close(); cancellation.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(cancellation.Token);
        if (failure is not null) throw failure;
        if (!File.Exists(Path.Combine(output, "result.txt"))) throw new TimeoutException("Native integration probe timed out.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static nint Packed(int x, int y) => (nint)((uint)(ushort)x | (uint)(ushort)y << 16);
    private static void Mouse(nint hwnd, uint message, PixelPoint position, nint flags) { var point = new NativePoint { X = position.X, Y = position.Y }; ScreenToClient(hwnd, ref point); SendMessage(hwnd, message, flags, Packed(point.X, point.Y)); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint GetCapture();
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    public sealed class NativeTestApp : Application
    {
        public override void Initialize()
        {
            var template = new App(); template.Initialize(); var resources = template.Resources; template.Resources = new Avalonia.Controls.ResourceDictionary(); Resources = resources;
            foreach (var style in template.Styles.ToArray()) { template.Styles.Remove(style); Styles.Add(style); }
        }
    }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint region);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    private sealed class SilentAudio : IAudioPlayer
    {
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public event EventHandler<AudioPlaybackErrorEventArgs>? PlaybackFailed { add { } remove { } }
        public event EventHandler<AudioOutputChangedEventArgs>? OutputDeviceChanged { add { } remove { } }
        public event EventHandler? OutputDevicesChanged { add { } remove { } }
        public IReadOnlyList<string> Devices => ["系统默认"];
        public string DeviceName { get; set; } = "系统默认";
        public bool IsPlaying => false;
        public bool IsAvailable => true;
        public float Volume { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(100);
        public void Load(string path) { }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
