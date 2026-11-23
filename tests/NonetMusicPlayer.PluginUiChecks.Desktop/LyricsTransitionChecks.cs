using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>覆盖歌词双向切换、快速重复导航、最小化清理和设置中的相对路径，不使用真实音乐或用户配置。</summary>
internal static class LyricsTransitionChecks
{
    public static void Run(string output, string? styleFile = null)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CheckSnapshotScale();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new AppStorage(Path.Combine(root, "data"));
        NonetMusicPlayer.Core.Diagnostics.AppLog.Initialize(storage.Root);
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (File.Exists(font)) vm.Settings.FontFilePath = AppFontService.Import(storage, font);
        var background = Path.Combine(root, "background.png");
        using (var image = new RenderTargetBitmap(new PixelSize(64, 64))) image.Save(background, PngBitmapEncoderOptions.Default);
        vm.Settings.BackgroundImagePath = AppBackgroundService.Import(storage, background);
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760 };
        if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("fixture.transition", [NonetMusicPlayer.Core.Plugins.StyleSheetContract.Read(input)]); window.PrepareInitialAppearance(); }
        window.Show(); Pump();
        var titleButtons = window.FindControl<StackPanel>("TitleButtons")!;
        using var arrow = new Cursor(StandardCursorType.Arrow);
        Check(titleButtons.Children.All(button => button.Cursor?.ToString() == arrow.ToString()), "Caption buttons use the arrow cursor.");
        var maximize = window.FindControl<Button>("MaximizeWindowButton")!;
        Check(Equals(ToolTip.GetTip(maximize), L10n.T("Common.MaximizeWindow")), "Normal state offers only Maximize.");
        window.WindowState = WindowState.Maximized;
        Check(Equals(ToolTip.GetTip(maximize), L10n.T("Common.RestoreWindow")), "Maximized state offers only Restore.");
        window.WindowState = WindowState.Normal; Pump();
        Check(Equals(ToolTip.GetTip(maximize), L10n.T("Common.MaximizeWindow")), "Restoring updates the action immediately.");
        vm.Navigate("settings"); Pump();
        var labels = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
        Check(!labels.Any(t => t == L10n.T("Extensions.ProjectFolder")), "No plugin project directory setting.");
        var backgroundLabel = labels.Single(t => t.StartsWith(L10n.T("Settings.BackgroundImage") + ": "));
        Check(backgroundLabel.Contains("Backgrounds") && !backgroundLabel.Contains(storage.Root), "Background path is relative to data root.");
        if (vm.Settings.FontFilePath is not null)
        {
            var fontLabel = labels.Single(t => t.StartsWith(L10n.T("Settings.FontFile") + ": "));
            Check(fontLabel.Contains("Fonts") && !fontLabel.Contains(storage.Root), "Font path is relative to data root.");
        }
        var motionEnabled = InterfaceMotion.IsEnabled;
        var oldPage = window.FindControl<ContentControl>("AlternatePage")!;
        var priorContent = oldPage.Content;
        var seek = window.FindControl<NonetMusicPlayer.Desktop.Controls.PlayerProgressSlider>("PlaybackSlider")!;
        var title = window.FindControl<StackPanel>("TitleBrand")!;
        var caption = window.FindControl<Border>("TitleBarSurface")!;
        var player = window.FindControl<Border>("PlayerSurface")!;
        var content = window.FindControl<Border>("ContentSurface")!;
        var sidebar = window.FindControl<Border>("SidebarPanel")!;
        var sidebarBrand = window.InterfaceLyricsProfile?.EdgeToEdge == true && title.GetVisualAncestors().Contains(sidebar);
        for (var n = 0; n < 120; n++) vm.LyricLines.Add(new(n, $"第 {n} 行歌词" + (n % 9 == 0 ? new string('长', 80) : ""), n % 3 == 0 ? "Translation " + new string('x', 70) : ""));
        vm.PlaybackDuration = 180; vm.PlaybackPosition = 73.25;
        Pump();
        var titlePixels = ReadTitlePixels();
        vm.Navigate("lyrics");
        CenteredBeforeFrame(73);
        NonetMusicPlayer.Core.Diagnostics.AppLog.Flush();
        Check(motionEnabled == Overlay(), "Opening uses one finite overlay when animations are enabled.");
        if (motionEnabled)
        {
            var first = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay");
            Check(first.Child is Image { RenderTransform: TranslateTransform { Y: > 0 } } && oldPage.IsEffectivelyVisible && ReferenceEquals(oldPage.Content, priorContent), "Before the first frame the original page remains attached and visible.");
            Check(!window.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "LyricsTransitionBackdrop"), "The shell is never represented by a backdrop bitmap.");
            Check(content.Effect is BlurEffect { Radius: 0 }, "The background starts sharp when opening.");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "FullLyricsHost");
            var opening = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay");
            Check(opening.Child is Image { RenderTransform: TranslateTransform { Y: > 0 } } && opening.ZIndex > 20, "Bottom player expands the lyrics viewport over the workspace.");
            var brandTop = title.TranslatePoint(new Point(title.Bounds.Width / 2, 1), opening)!.Value;
            Check(CanCover(opening, brandTop) == sidebarBrand, "The slide reaches the top of the edge-to-edge sidebar brand and leaves the normal caption brand clear.");
            CheckCaptionUncovered(opening);
            Check(host.IsVisible && host.Opacity == 0 && !host.IsHitTestVisible, "Live lyrics clock remains attached while transition input is suppressed.");
            CheckSeekUncovered(opening);
            ObserveMotion(true, () => Check(titlePixels.SequenceEqual(ReadTitlePixels()),
                "The title icon and text render the same pixels while the lyrics page enters."));
            Pump(80);
            Check(!Overlay() && content.Effect is null && oldPage.Content is null && host.Opacity == 1 && host.IsHitTestVisible, "Opening releases the old page, blur and snapshot and restores lyrics input.");
            CenteredBeforeFrame(73);
        }
        vm.Navigate("settings");
        Check(motionEnabled == Overlay(), "Closing keeps outgoing lyrics only as a noninteractive snapshot.");
        if (motionEnabled)
        {
            Check(content.Effect is BlurEffect { Radius: >= 39 }, "Closing starts at the strong blur radius.");
            window.UpdateLayout();
            var closing = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay");
            CheckSeekUncovered(closing);
            CheckCaptionUncovered(closing);
            ObserveMotion(false);
        }
        Pump(80); Check(!Overlay() && content.Effect is null, "Closing releases the overlay and blur.");
        vm.PlaybackPosition = 91.25; vm.Navigate("lyrics"); CenteredBeforeFrame(91); vm.Navigate("settings"); vm.Navigate("lyrics"); CenteredBeforeFrame(91); Pump(80);
        Check(!Overlay() && content.Effect is null && vm.Page == "lyrics", "Rapid toggles leave the latest page without stale effects.");
        vm.Navigate("settings"); Pump(80); vm.Settings.PlayerTop = true; vm.ApplySettings(); Pump();
        vm.PlaybackPosition = 51.25; vm.Navigate("lyrics"); CenteredBeforeFrame(51);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        if (motionEnabled) Check(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay").Child is Image { RenderTransform: TranslateTransform transform } && (window.InterfaceSettings.PlayerTop ? transform.Y < 0 : transform.Y > 0), "Transition direction uses the effective player placement.");
        window.WindowState = WindowState.Minimized; Pump(); Check(!Overlay() && content.Effect is null, "Minimize removes the transition effects immediately.");
        window.WindowState = WindowState.Normal; Pump();
        var previous = Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION");
        try
        {
            Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
            vm.Navigate("settings"); vm.PlaybackPosition = 40.25; vm.Navigate("lyrics"); CenteredBeforeFrame(40); Check(!Overlay(), "Reduced motion navigates without snapshots.");
            foreach (var index in new[] { 0, 119, 36, 73 })
            {
                vm.Navigate("settings"); vm.PlaybackPosition = index + .25; vm.Navigate("lyrics"); CenteredBeforeFrame(index); Pump(); CenteredBeforeFrame(index);
            }
        }
        finally { Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", previous); }
        window.Close(); Pump();
        CheckManualVersion();
        Console.WriteLine("PASS lyrics centering, vertical slide, progressive backdrop blur, sharp title/seek, rapid navigation, cancellation, reduced motion and snapshot scale.");
        bool Overlay() => window.GetVisualDescendants().OfType<Border>().Count(b => b.Name == "LyricsTransitionOverlay") == 1;
        static bool CanCover(Border overlay, Point point) => new Rect(overlay.Bounds.Size).Contains(point)
            && (overlay.Clip?.FillContains(point) ?? true);
        void CheckCaptionUncovered(Border overlay)
        {
            Check(titleButtons.Children.All(button => !CanCover(overlay, button.TranslatePoint(new Rect(button.Bounds.Size).Center, overlay)!.Value)),
                "The native caption buttons stay above the lyrics transition.");
        }
        void CheckSeekUncovered(Border overlay)
        {
            var topHalf = seek.ThumbCenter - new Vector(0, seek.ThumbRadius - 1);
            var point = seek.TranslatePoint(topHalf, overlay)!.Value;
            Check(!new Rect(overlay.Bounds.Size).Contains(point) || overlay.Clip is { } clip && !clip.FillContains(point),
                "The transition leaves the protruding part of the actual seek thumb unobstructed.");
        }
        SkiaSharp.SKColor[] ReadTitlePixels()
        {
            var origin = title.TranslatePoint(default, window)!.Value;
            // 读取已绘制帧，避免截图推进时钟后将转场结束画面误认成中间帧。
            using var frame = window.GetLastRenderedFrame()!;
            using var buffer = new MemoryStream(); frame.Save(buffer, PngBitmapEncoderOptions.Default); buffer.Position = 0;
            using var pixels = SkiaSharp.SKBitmap.Decode(buffer);
            return (from y in Enumerable.Range((int)origin.Y, (int)title.Bounds.Height)
                    from x in Enumerable.Range((int)origin.X, (int)title.Bounds.Width)
                    select pixels.GetPixel(x, y)).ToArray();
        }
        void ObserveMotion(bool opening, Action? checkFrame = null)
        {
            var motion = typeof(MainWindow).GetField("_lyricsMotion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            var cancelField = typeof(LyricsPageMotion).GetField("_cancel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var priorCancel = (Action?)cancelField.GetValue(motion); string? cancellation = null;
            cancelField.SetValue(motion, (Action)(() => { cancellation = Environment.StackTrace; priorCancel?.Invoke(); }));
            var values = new List<double>(); var end = Environment.TickCount64 + 1500;
            var initial = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay");
            var transform = (TranslateTransform)((Image)initial.Child!).RenderTransform!;
            var brandTop = sidebarBrand && opening ? title.TranslatePoint(default, initial)!.Value.Y : 0;
            var brandBottom = brandTop + title.Bounds.Height;
            var blur = content.Effect as BlurEffect ?? throw new InvalidOperationException("Expected a live blur on the background page.");
            Check(sidebar.GetVisualDescendants().Prepend(sidebar).Any(element => ReferenceEquals(element.Effect, blur)),
                "The sidebar receives the same animated blur as the page.");
            Check(new Control[] { title, caption, player, seek }.All(control =>
                control.GetVisualAncestors().Prepend(control).All(element => !ReferenceEquals(element.Effect, blur))),
                "No caption or player control is inside a transition blur.");
            var radii = new List<double> { blur.Radius };
            values.Add(transform.Y);
            void Changed(object? sender, AvaloniaPropertyChangedEventArgs args) { if (args.Property == TranslateTransform.YProperty) values.Add(transform.Y); }
            void BlurChanged(object? sender, AvaloniaPropertyChangedEventArgs args) { if (args.Property == BlurEffect.RadiusProperty) radii.Add(blur.Radius); }
            transform.PropertyChanged += Changed;
            blur.PropertyChanged += BlurChanged;
            while (Overlay() && Environment.TickCount64 < end)
            {
                var overlay = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LyricsTransitionOverlay");
                values.Add(((TranslateTransform)((Image)overlay.Child!).RenderTransform!).Y);
                if (values.Count >= 3 && checkFrame is not null) { checkFrame(); checkFrame = null; }
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(4);
            }
            transform.PropertyChanged -= Changed;
            blur.PropertyChanged -= BlurChanged;
            Capture(opening ? "lyrics-opening-settled.png" : "lyrics-closing-settled.png");
            File.WriteAllText(Path.Combine(root, opening ? "opening-trace.txt" : "closing-trace.txt"), string.Join(", ", values) + "\n" + string.Join(", ", radii) + "\n" + cancellation);
            Check(cancellation is null, "A stable page transition must not be canceled by a layout callback: " + cancellation);
            Check(values.Count >= 3 && checkFrame is null, "Observe actual position updates rather than accepting an already completed animation.");
            for (var i = 1; i < values.Count; i++) Check(opening ? values[i] <= values[i - 1] + .5 : values[i] >= values[i - 1] - .5, "Snapshot position must not jump back at animation completion.");
            if (sidebarBrand && opening)
                Check(values.Any(y => y >= brandBottom) && values.Any(y => y > brandTop + .5 && y < brandBottom - .5) && values.Any(y => y <= brandTop),
                    "The sliding edge partially covers the sidebar brand before it covers the entire brand.");
            Check(radii.Max() >= 39 && radii.Any(radius => radius is > 2 and < 38), "Blur renders at intermediate and strong radii.");
            for (var i = 1; i < radii.Count; i++) Check(opening ? radii[i] >= radii[i - 1] - .1 : radii[i] <= radii[i - 1] + .1, "Blur follows both transition directions without jumping back.");
        }
        void Capture(string name) { using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default); }
        // 不推进 Dispatcher 或动画时钟，确保快照捕获前已经完成真实虚拟化列表的定位。
        void CenteredBeforeFrame(int index)
        {
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "LyricLines");
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Check(list.SelectedIndex == index, "The selected lyric is sampled from the current playback position.");
            var row = list.ContainerFromIndex(index);
            Check(row is not null, $"Current lyric {index} is realized before the first transition frame.");
            var center = row!.TranslatePoint(new Point(0, row!.Bounds.Height / 2), scroll)!.Value.Y;
            Check(Math.Abs(center - scroll.Viewport.Height / 2) < 2, $"Current lyric is centered before the snapshot, not after animation: delta {center - scroll.Viewport.Height / 2}.");
        }
    }
    private static void CheckManualVersion()
    {
        var currentLanguage = L10n.Language;
        var version = NonetMusicPlayer.Core.Updates.ReleaseUpdateService.VersionOf(typeof(MainWindow).Assembly);
        try
        {
            foreach (var language in new[] { "zh-CN", "en-US", "ja-JP" })
            {
                L10n.SetLanguage(language);
                var manual = new UserManualWindow();
                var scroll = (ScrollViewer)((DockPanel)manual.Content!).Children.Single();
                var text = ((StackPanel)scroll.Content!).Children.OfType<TextBlock>().ToArray();
                Check(text.Length is > 8 and < 40 && text[1].Text == L10n.Format("Common.UserManualVersion", version),
                    $"{language} uses a concise guide and the current desktop assembly version.");
                manual.Close();
            }
        }
        finally { L10n.SetLanguage(currentLanguage); }
    }
    private static void Pump(int count = 32) { for (var i = 0; i < count; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }
    // 在高 DPI 和快照像素限额触发时比较标记的实际绘制尺寸，避免只检查动画类型而遗漏画面缩放。
    private static void CheckSnapshotScale()
    {
        foreach (var dimensions in new[] { new Size(800, 500), new Size(1800, 1400) })
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var source = new Canvas { Width = dimensions.Width, Height = dimensions.Height, Background = Brushes.Black };
            var marker = new Border { Width = 60, Height = 40, Background = Brushes.Red };
            Canvas.SetLeft(marker, 180); Canvas.SetTop(marker, 150); source.Children.Add(marker);
            source.Measure(dimensions); source.Arrange(new Rect(dimensions));
            using var snapshot = LyricsPageMotion.Capture(source, new Rect(100, 100, 400, 300), scale)!;
            using var rendered = new RenderTargetBitmap(new PixelSize(400, 300));
            using (var drawing = rendered.CreateDrawingContext()) drawing.DrawImage(snapshot, new Rect(0, 0, 400, 300));
            using var bytes = new MemoryStream(); rendered.Save(bytes, PngBitmapEncoderOptions.Default); bytes.Position = 0;
            using var pixels = SkiaSharp.SKBitmap.Decode(bytes);
            var points = Enumerable.Range(0, 400).Where(x => pixels.GetPixel(x, 70).Red > 200).ToArray();
            Check(points.Length is >= 58 and <= 62 && Math.Abs(points[0] - 80) < 2, $"Snapshot retains logical dimensions at {scale}x; observed {points.Length} pixels at {points.FirstOrDefault()}.");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
