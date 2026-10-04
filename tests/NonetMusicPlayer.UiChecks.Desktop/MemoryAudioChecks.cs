using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

/// <summary>Runs the actual MainViewModel -> native player -> UI dispatcher -> next-track chain.</summary>
internal static class MemoryAudioChecks
{
    public static void Run(string output, bool enforceBounds = true)
    {
        Directory.CreateDirectory(output);
        var data = Path.Combine(Path.GetFullPath(output), "memory-data-" + Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(data); var folder = Path.Combine(data, "Fixtures"); Directory.CreateDirectory(folder);
        var formats = new[] { ("wav", 44100, 1), ("wav", 48000, 2), ("flac", 44100, 2), ("mp3", 44100, 2) };
        var sources = new List<string>();
        foreach (var (extension, rate, channels) in formats)
        {
            var path = Path.Combine(folder, $"tone-{rate}-{channels}.{extension}");
            if (extension == "wav") AudioChecks.WriteTone(path, rate, channels, 1, 440);
            else AudioChecks.WriteCompressedTone(path, extension, rate, channels, 1);
            sources.Add(path);
        }
        var covers = new List<string>();
        for (var i = 0; i < 128; i++)
        {
            var cover = Path.Combine(storage.ArtworkFolder, $"cover-{i:000}.png");
            using (var bitmap = new WriteableBitmap(new PixelSize(320, 320), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            {
                using (var frame = bitmap.Lock())
                {
                    var pixels = new byte[frame.RowBytes * frame.Size.Height];
                    for (var offset = 0; offset < pixels.Length; offset += 4) { pixels[offset] = (byte)i; pixels[offset + 1] = (byte)(offset / 16); pixels[offset + 2] = (byte)(255 - i); pixels[offset + 3] = 255; }
                    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
                }
                bitmap.Save(cover, PngBitmapEncoderOptions.Default);
            }
            covers.Add(cover);
        }
        var tracks = Enumerable.Range(0, 128).Select(i => new TrackItem("memory-" + i, "Fixture " + i, "Fixture artist", "Fixture album " + i, sources[i % sources.Count], Path.GetExtension(sources[i % sources.Count]), new FileInfo(sources[i % sources.Count]).Length) { CoverPath = covers[i], DurationSeconds = 1 }).ToList();
        storage.Save(new AppState { Tracks = tracks, Settings = new AppSettings { Volume = 0, PlayMode = PlayMode.RepeatAll } });
        var before = Measure("before-playback");
        using var audio = new NativeAudioPlayer { Volume = 0 };
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        var errors = new List<string>(); vm.UserNotification += (_, args) => { if (!args.IsWarning) errors.Add(args.Message); };
        var ended = 0; var touched = new HashSet<string>(); var milestones = new List<MemoryPoint>();
        audio.PlaybackStopped += (_, _) => Interlocked.Increment(ref ended);
        Pump(vm.PlayTrackAsync(vm.State.Tracks[0]));
        var clock = Stopwatch.StartNew();
        while (Volatile.Read(ref ended) < 48 && clock.Elapsed.TotalSeconds < 90)
        {
            Dispatcher.UIThread.RunJobs();
            if (vm.CurrentTrack is { } current && touched.Add(current.Id))
            {
                var image = current.Artwork; Require(image is not null && image.Size.Width <= 320 && image.Size.Height <= 320, "Current real VM artwork decoded at bounded size");
                var number = touched.Count;
                if (number is 10 or 24 or 48) milestones.Add(Measure("after-track-" + number));
            }
            if (errors.Count > 0) throw new InvalidOperationException("Native VM automatic-next reported a failure: " + string.Join("; ", errors));
            Thread.Sleep(4);
        }
        vm.StopPlaybackCommand.Execute(null); Dispatcher.UIThread.RunJobs();
        Require(ended >= 48 && touched.Count >= 48 && errors.Count == 0, "48 naturally ended cross-format tracks through actual VM dispatcher and native player");
        var afterPlayback = Measure("after-48-natural-ends");
        foreach (var track in vm.State.Tracks) Require(track.Artwork is not null, "All 128 independent library covers remain available");
        var afterBrowse = Measure("after-browse-128-covers");
        // Keep old Image.Source proxies alive and prove eviction cannot break rendering.
        var retainedSources = vm.State.Tracks.Select(track => track.Artwork).ToArray();
        using (var rendered = new RenderTargetBitmap(new PixelSize(32, 32)))
        {
            var image = new Avalonia.Controls.Image { Source = retainedSources[0], Width = 32, Height = 32 };
            image.Measure(new Size(32, 32)); image.Arrange(new Rect(0, 0, 32, 32)); rendered.Render(image);
            if (enforceBounds)
            {
                var pixels = new byte[32 * 32 * 4]; var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { rendered.CopyPixels(new PixelRect(0, 0, 32, 32), pinned.AddrOfPinnedObject(), pixels.Length, 32 * 4); }
                finally { pinned.Free(); }
                Require(pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha > 0), "An evicted retained proxy still renders actual artwork pixels");
            }
        }
        var cacheType = typeof(TrackItem).Assembly.GetType("NonetMusicPlayer.Desktop.Services.ArtworkCache");
        if (enforceBounds)
        {
            Require(cacheType is not null, "Shared bounded artwork cache exists");
            var cache = cacheType!.GetProperty("Shared")!.GetValue(null)!;
            var portrait = Path.Combine(storage.ArtworkFolder, "portrait.png");
            using (var bitmap = new WriteableBitmap(new PixelSize(100, 2000), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul)) bitmap.Save(portrait, PngBitmapEncoderOptions.Default);
            var tall = new TrackItem { CoverPath = portrait }.Artwork;
            Require(tall is not null && tall.Size.Width <= 320 && tall.Size.Height <= 320, "Portrait covers bound both dimensions, not only width");
            var snapshot = cacheType.GetProperty("Snapshot")!.GetValue(cache)!;
            var count = (int)snapshot.GetType().GetProperty("Count")!.GetValue(snapshot)!;
            var bytes = (long)snapshot.GetType().GetProperty("EstimatedBytes")!.GetValue(snapshot)!;
            Require(count <= 48 && bytes <= 16 * 1024 * 1024, "Decoded artwork cache strictly bounded by entry count and 16 MiB pixel budget");
            Console.WriteLine($"MEMORY cache: count={count}, estimated-pixels={bytes / 1048576d:0.00} MiB");
            var bufferProperty = audio.GetType().GetProperty("BufferSnapshot");
            Require(bufferProperty is not null, "Native block/lifetime diagnostics exist");
            var buffer = bufferProperty!.GetValue(audio)!;
            var loaded = (long)buffer.GetType().GetProperty("LoadedSources")!.GetValue(buffer)!;
            var released = (long)buffer.GetType().GetProperty("ReleasedSources")!.GetValue(buffer)!;
            var active = (int)buffer.GetType().GetProperty("ActiveSources")!.GetValue(buffer)!;
            var maximum = (int)buffer.GetType().GetProperty("MaxDecodeSamples")!.GetValue(buffer)!;
            Require(loaded - released == active && active == 1 && maximum <= 2048, "Exactly one live source and at most 1024 stereo frames per decode call");
            Console.WriteLine($"MEMORY audio: loaded={loaded}, released={released}, active={active}, maximumDecodeSamples={maximum}");
            Require(afterPlayback.PrivateMiB - milestones[0].PrivateMiB < 48, "Post-warmup native playback private bytes do not grow without bound");
        }
        var report = new { Before = before, Milestones = milestones, AfterPlayback = afterPlayback, AfterBrowse = afterBrowse, NaturalEnds = ended, TracksVisited = touched.Count, Covers = 128, EnforceBounds = enforceBounds };
        File.WriteAllText(Path.Combine(output, "memory-report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        vm.Dispose(); var afterDispose = Measure("after-dispose");
        if (enforceBounds)
        {
            var cache = cacheType!.GetProperty("Shared")!.GetValue(null)!;
            cacheType.GetMethod("Clear")!.Invoke(cache, null);
            var snapshot = cacheType.GetProperty("Snapshot")!.GetValue(cache)!;
            Require((long)snapshot.GetType().GetProperty("EstimatedBytes")!.GetValue(snapshot)! == 0, "Cache clears all resident bitmap pixels");
        }
        GC.KeepAlive(retainedSources);
        Console.WriteLine($"PASS MEMORY/NATIVE VM: {ended} natural ends, {touched.Count} distinct tracks, 128 covers; final private={afterDispose.PrivateMiB:0.00} MiB");
    }
    private static void Pump(Task task)
    {
        var clock = Stopwatch.StartNew(); while (!task.IsCompleted && clock.Elapsed.TotalSeconds < 15) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
        Require(task.IsCompleted, "Native VM load completes"); task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    private static MemoryPoint Measure(string phase)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var result = new MemoryPoint(phase, process.PrivateMemorySize64 / 1048576d, process.WorkingSet64 / 1048576d, GC.GetTotalMemory(false) / 1048576d);
        Console.WriteLine($"MEMORY {phase}: private={result.PrivateMiB:0.00} MiB, working={result.WorkingSetMiB:0.00} MiB, managed={result.ManagedMiB:0.00} MiB"); return result;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed record MemoryPoint(string Phase, double PrivateMiB, double WorkingSetMiB, double ManagedMiB);
}
