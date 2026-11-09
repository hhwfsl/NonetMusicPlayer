using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;
using SkiaSharp;

/// <summary>真实大图和大分类库验证滚动条跳转、控件复用、封面归属与离页释放，不使用用户资料。</summary>
internal static class CatalogScrollChecks
{
    public static void Run(string output, string? styleFile)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var covers = Enumerable.Range(0, 12).Select(i => Path.Combine(root, $"cover-{i}.png")).ToArray();
        for (var i = 0; i < covers.Length; i++)
        {
            using var pixels = new SKBitmap(i == 0 ? 180 : 1536, i == 0 ? 2400 : 1536);
            pixels.Erase(Color(i)); using var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(covers[i]); encoded.SaveTo(file);
        }
        var invalid = Path.Combine(root, "invalid.png"); File.WriteAllText(invalid, "invalid artwork");
        GridReuse(root);
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root, "data"))), new SilentAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var tracks = Enumerable.Range(0, 2400).Select(i => new TrackItem("track-" + i, "Song " + i, $"Artist {i:D4}", $"Album {i:D4}", "file-" + i, ".flac", 0) { CoverPath = covers[i % covers.Length] }).ToArray();
        vm.AddToPlaylist(vm.CreatePlaylist("Scroll fixture"), tracks);
        vm.State.GroupCovers["album:Album 0000"] = invalid;
        vm.State.GroupCovers["artist:Artist 0000"] = covers[1];
        var owner = new MainWindow { DataContext = vm, Width = 1180, Height = 780 }; owner.Show(); Pump(owner);
        var metrics = new List<string>();
        try
        {
            foreach (var styled in styleFile is null ? new[] { false } : new[] { false, true })
            {
                if (styled) { using var input = File.OpenRead(styleFile!); owner.ApplyInterfaceLayers("fixture.scroll", [StyleSheetContract.Read(input)]); Pump(owner); }
                foreach (var page in new[] { "albums", "artists" })
                {
                    var before = ArtworkCache.Shared.Snapshot.DecodeCount;
                    vm.Navigate(page); Pump(owner);
                    var grid = owner.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single();
                    Wait(() => Images().Any() && Images().All(i => i.Source is Bitmap), owner);
                    Check(Pixel(Images().First()) == Color(page == "artists" ? 1 : 0), "Custom cover wins, corrupt custom falls back to track cover");
                    Check(ArtworkCache.Shared.Snapshot.DecodeCount == before, "Catalog must not synchronously populate artwork LRU");
                    var original = Cards().ToHashSet();
                    var bar = grid.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
                    var samples = new List<double>();
                    for (var frame = 0; frame < 35; frame++)
                    {
                        var watch = Stopwatch.StartNew();
                        // 模拟同一绘制帧收到多次滑块移动，只需呈现最后的视口。
                        for (var move = 0; move < 6; move++) bar.Value = (frame * 193 + move * 337) % 2300 * 242.0 / 4;
                        Pump(owner); samples.Add(watch.Elapsed.TotalMilliseconds);
                        Check(grid.RealizedCount < 50, "Realized controls bounded by viewport");
                        Check(Cards().Any(original.Contains), "Large scroll jumps reuse the existing visual tree");
                        Check(Cards().Any(c => Canvas.GetTop(c) <= grid.Offset.Y && Canvas.GetTop(c) + 242 > grid.Offset.Y), "Latest thumb position is covered after one render frame");
                    }
                    bar.Value = bar.Maximum; Pump(owner);
                    Wait(() => Images().All(i => i.Source is Bitmap), owner);
                    Check(Cards().Any(c => c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == (page == "artists" ? "Artist 2399" : "Album 2399"))), "Scroll reaches final category");
                    foreach (var card in Cards())
                    {
                        var name = card.GetVisualDescendants().OfType<TextBlock>().First().Text!;
                        var number = int.Parse(name.Split(' ')[1]);
                        var image = card.GetVisualDescendants().OfType<ThumbnailImage>().Single();
                        Check(image.Source is Bitmap { PixelSize.Width: <= ArtworkCache.MaxDimension, PixelSize.Height: <= ArtworkCache.MaxDimension }, "Decoded thumbnail dimensions are bounded");
                        Check(Pixel(image) == Color(number % covers.Length), "Recycled card never retains an old category image");
                    }
                    samples.Sort(); metrics.Add($"{(styled ? "preset" : "default")}/{page}: 2400 categories, realized={grid.RealizedCount}, frame median={samples[samples.Count / 2]:F2}ms, p95={samples[(int)(samples.Count * .95)]:F2}ms (headless fixture)");
                    using var capture = owner.CaptureRenderedFrame(); capture?.Save(Path.Combine(root, $"{styled}-{page}.png"), PngBitmapEncoderOptions.Default);
                    if (!styled && page == "albums")
                    {
                        var offset = grid.Offset; var saved = Images();
                        vm.Settings.OptimizeMemoryWhenMinimized = true; owner.WindowState = WindowState.Minimized;
                        // 不强制绘制隐藏窗口，等待真实的延迟内存回收任务完成。
                        var task = (Task)typeof(MainWindow).GetField("_memoryOptimizationTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
                        var end = Environment.TickCount64 + 10000;
                        while (!task.IsCompleted && Environment.TickCount64 < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
                        task.GetAwaiter().GetResult(); Check(saved.All(i => i.Source is null), "Minimizing releases visible thumbnail pixels");
                        owner.WindowState = WindowState.Normal; Pump(owner); Wait(() => saved.All(i => i.Source is Bitmap), owner);
                        Check(grid.Offset == offset && Images().SequenceEqual(saved), "Restoring preserves the same cards and offset");
                    }
                    var oldImages = Images();
                    Cards().Last().GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new(Button.ClickEvent)); Pump(owner);
                    Check(vm.Page == (page == "artists" ? "artist:" : "album:") + (page == "artists" ? "Artist 2399" : "Album 2399"), "Recycled click uses latest binding");
                    Wait(() => oldImages.All(i => i.Source is null), owner); grid.Refresh(); Check(grid.RealizedCount == 0, "Disposed grids cannot realize queued frames");

                    Grid[] Cards() => grid.GetVisualDescendants().OfType<Grid>().Where(c => c.Name == "MusicGroupCard").OrderBy(Canvas.GetTop).ThenBy(Canvas.GetLeft).ToArray();
                    ThumbnailImage[] Images() => grid.GetVisualDescendants().OfType<ThumbnailImage>().ToArray();
                }
            }
        }
        finally { owner.Close(); Pump(owner); }
        File.WriteAllLines(Path.Combine(root, "metrics.txt"), metrics); foreach (var metric in metrics) Console.WriteLine(metric);
        Console.WriteLine("PASS frame coalescing, viewport-bounded reuse, background thumbnails, fallback paths, latest binding, cancellation and disposal; default/preset.");
    }
    private static void GridReuse(string root)
    {
        var created = 0; var rebound = 0;
        // 单独的大网格让“滚过多少项”与“创建多少控件”可以直接比较。
        Control Create(int index) { created++; return new TextBlock { Tag = index, Text = index.ToString() }; }
        var grid = new VirtualizedCardGrid(10000, Create, (card, index) => { rebound++; card.Tag = index; ((TextBlock)card).Text = index.ToString(); });
        var owner = new Window { Width = 1000, Height = 700, Content = grid }; owner.Show(); Pump(owner);
        var initial = created;
        for (var step = 0; step < 80; step++) { grid.Offset = new(0, step * 7000); Pump(owner); }
        Check(rebound > 500 && created <= initial + 10 && grid.RealizedCount < 50, $"Viewport recycling stays bounded across thousands of cards: initial={initial}, created={created}, rebound={rebound}, realized={grid.RealizedCount}, viewport={grid.Viewport}, extent={grid.Extent}");
        grid.Offset = default; grid.Dispose(); Pump(owner); Check(grid.RealizedCount == 0, "Disposal rejects pending frame"); owner.Close(); Pump(owner);
        File.WriteAllText(Path.Combine(root, "reuse.txt"), $"created={created}, rebindings={rebound}");
    }
    private static SKColor Color(int i) => new((byte)(20 + i * 17), (byte)(30 + i * 11), (byte)(200 - i * 13));
    private static SKColor Pixel(ThumbnailImage image)
    {
        using var stream = new MemoryStream(); ((Bitmap)image.Source!).Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
        using var decoded = SKBitmap.Decode(stream); return decoded.GetPixel(0, 0);
    }
    private static void Pump(Window owner) { Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); }
    private static void Wait(Func<bool> condition, Window owner)
    {
        var end = Environment.TickCount64 + 10000;
        while (!condition() && Environment.TickCount64 < end) { Pump(owner); Thread.Sleep(3); }
        Check(condition(), "Timed out waiting for artwork lifecycle");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class SilentAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(100); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
