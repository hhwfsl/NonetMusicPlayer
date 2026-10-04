using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class DesktopLyricsAndDownloadChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, "主题色歌词与边缘缩放", "翻译歌词"));
        vm.Settings.DesktopLyricsLocked = false; vm.Settings.DesktopLyricsWidth = 700; vm.Settings.DesktopLyricsHeight = 220;
        using (var lyrics = new DesktopLyricsWindow(owner, vm))
        {
            lyrics.Toggle(); Pump(lyrics); var line = Find<KaraokeLine>(lyrics, "DesktopCurrentLyric");
            foreach (var color in new[] { "#2299CC", "#EE6677", "#000000", "#FFFFFF" })
            {
                vm.Settings.Accent = color; vm.ApplySettings(); Pump(lyrics);
                Require(lyrics.GetVisualDescendants().OfType<KaraokeLine>().All(l => l.TextColor == Color.Parse(color)), "Both lyric lines follow live theme color");
            }
            vm.Settings.Accent = "#A895FF"; vm.ApplySettings(); Pump(lyrics);
            var textPoint = line.TranslatePoint(line.TextBounds.Center, lyrics)!.Value; lyrics.MouseMove(textPoint); Pump(lyrics);
            var handles = lyrics.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("DesktopLyricsResize") == true).ToArray();
            Require(handles.Length == 4 && handles.All(h => h.IsVisible), "All four frame edges can resize after lyric hover; no corner grip");
            foreach (var name in new[] { "Left", "Right", "Top", "Bottom" })
            {
                var handle = handles.Single(h => h.Name == "DesktopLyricsResize" + name);
                var left = name.Contains("Left"); var top = name.Contains("Top"); var horizontal = name is "Left" or "Right" || name.Length == 0 || name.Contains("Left") || name.Contains("Right");
                var vertical = name is "Top" or "Bottom" || name.Length == 0 || name.Contains("Top") || name.Contains("Bottom");
                var before = lyrics.Bounds.Size; var origin = lyrics.Position;
                var p = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), lyrics)!.Value;
                var start = lyrics.PointToScreen(p); var dx = horizontal ? left ? -24 : 24 : 0; var dy = vertical ? top ? -20 : 20 : 0;
                lyrics.MouseDown(p, MouseButton.Left); lyrics.MouseMove(p + new Vector(dx, dy), RawInputModifiers.LeftMouseButton); Pump(lyrics);
                var end = new PixelPoint(start.X + (int)(dx * lyrics.RenderScaling), start.Y + (int)(dy * lyrics.RenderScaling));
                lyrics.MouseUp(lyrics.PointToClient(end), MouseButton.Left); Pump(lyrics);
                Require(Math.Abs(lyrics.Width - before.Width - (horizontal ? 24 : 0)) < 2 && Math.Abs(lyrics.Height - before.Height - (vertical ? 20 : 0)) < 2, "Resize " + name + " changes only expected dimensions");
                if (left) Require(lyrics.Position.X < origin.X, "Left resize preserves opposite right edge");
                if (top) Require(lyrics.Position.Y < origin.Y, "Top resize preserves opposite bottom edge");
            }
            Require(vm.Settings.DesktopLyricsWidth == lyrics.Width && vm.Settings.DesktopLyricsHeight == lyrics.Height, "All edge sizes persist after release");
            using (var frame = lyrics.CaptureRenderedFrame()!) frame.Save(System.IO.Path.Combine(output, "beta10-lyrics-resize.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            lyrics.LockLyrics(); Pump(lyrics); Require(handles.All(h => !h.IsVisible), "Lock hides all resize handles"); lyrics.UnlockLyrics(); lyrics.HideLyrics();
        }
        vm.Navigate("statistics"); owner.Width = 980; owner.Height = 660; Pump(owner);
        var statistics = owner.GetVisualDescendants().OfType<StatisticsView>().Single();
        var date = statistics.DateSelector; var scroll = statistics.GetVisualDescendants().OfType<ScrollViewer>().First();
        var dateRight = date.TranslatePoint(new Point(date.Bounds.Width, 0), owner)!.Value.X; var scrollRight = scroll.TranslatePoint(new Point(scroll.Bounds.Width, 0), owner)!.Value.X;
        Require(scrollRight - dateRight >= 24, "Date selector stays away from scrollbar gutter");
        using (var frame = owner.CaptureRenderedFrame()!) frame.Save(System.IO.Path.Combine(output, "beta10-statistics.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        vm.Navigate("plugins"); Pump(owner);
        var canceled = false; var notification = owner.ShowPluginDownload(() => canceled = true); notification.Downloading("sample-win-x64.impp"); notification.Report(37); Pump(owner);
        var toast = Find<Border>(owner, "PluginDownloadToast"); var progress = Find<ProgressBar>(toast, "PluginDownloadProgress");
        Require(progress.Value == 37 && !progress.IsIndeterminate && TopLevel.GetTopLevel(toast) == owner, "Download percent is in a main-window toast, not a window");
        using (var frame = owner.CaptureRenderedFrame()!) frame.Save(System.IO.Path.Combine(output, "beta10-download-progress.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        notification.Complete("sample-win-x64.impp"); notification.Report(85); Pump(owner);
        Require(!owner.GetVisualDescendants().Any(v => v is Border b && b.Name == "PluginDownloadToast") && Find<Border>(owner, "PluginDownloadComplete").IsVisible, "Progress disappears before a separate completion toast; late updates ignored");
        Settle(owner, 4250); Require(!owner.GetVisualDescendants().Any(v => v is Border b && b.Name == "PluginDownloadComplete"), "Completion toast closes automatically");
        using (var cancelNotice = owner.ShowPluginDownload(() => canceled = true))
        {
            Pump(owner); Find<Border>(owner, "PluginDownloadToast").GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(canceled && !owner.GetVisualDescendants().Any(v => v is Border b && b.Name == "PluginDownloadToast"), "Cancel cleans up toast and requests download cancellation");
        }
        DownloadProgress(vm);
        owner.Width = 1360; owner.Height = 860; Pump(owner);
        Console.WriteLine("PASS BETA10: live theme-colored lyrics, four-edge mouse resize/anchors/persistence/lock, calendar gutter, in-window download progress/cancel/completion/automatic timeout, mocked known/unknown-length downloads");
    }
    private static void DownloadProgress(MainViewModel vm)
    {
        var bytes = new byte[150000]; Random.Shared.NextBytes(bytes);
        var asset = new PluginReleaseAsset("mock.impp", new Uri("https://github.com/test/repo/releases/download/v1/mock.impp"), bytes.Length);
        using var downloader = new PluginReleaseDownloader(new UnknownLengthHandler(bytes));
        var known = new ProgressRecorder();
        using (var package = downloader.DownloadAsync(asset, vm.Storage.PluginsFolder, known).GetAwaiter().GetResult())
            Require(new FileInfo(package.Path).Length == bytes.Length && known.Values.Any(p => p is > 0 and < 100) && known.Values.Last() == 100, "Release asset size supplies progress without Content-Length");
        var unknown = new ProgressRecorder();
        using (downloader.DownloadAsync(asset with { Size = 0 }, vm.Storage.PluginsFolder, unknown).GetAwaiter().GetResult())
            Require(unknown.Values.Take(unknown.Values.Count - 1).All(p => p == 0) && unknown.Values.Last() == 100, "Unknown length stays indeterminate until verified complete");
        Require(Directory.GetFiles(Path.Combine(vm.Storage.PluginsFolder, ".downloads")).Length == 0, "Mock progress download leaves no staging files");
    }
    private sealed class ProgressRecorder : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }
    private sealed class UnknownLengthHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new UnknownLengthContent(bytes) });
    }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, false));
    }
    private static void Settle(Window window, int milliseconds) { var until = Environment.TickCount64 + milliseconds; while (Environment.TickCount64 < until) { Pump(window); Thread.Sleep(15); } Pump(window); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); }
}
