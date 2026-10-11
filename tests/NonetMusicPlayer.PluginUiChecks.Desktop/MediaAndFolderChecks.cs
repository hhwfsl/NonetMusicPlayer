using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class MediaAndFolderChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.GetFullPath(Path.Combine(output, "media-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        Require(File.Exists(BackgroundVideoDecoder.ExecutablePath), "Run prepare-background-decoder.ps1 before building tests.");
        var video = Path.Combine(root, "带音轨 #background.mp4"); var gif = Path.Combine(root, "animation.gif"); var webp = Path.Combine(root, "animation.webp");
        Generate(["-f", "lavfi", "-i", "testsrc=size=160x90:rate=10:duration=1", "-f", "lavfi", "-i", "sine=frequency=1000:duration=1", "-c:v", "mpeg4", "-c:a", "aac", "-shortest", video]);
        Generate(["-f", "lavfi", "-i", "testsrc=size=160x90:rate=10:duration=1", gif]);
        Generate(["-f", "lavfi", "-i", "testsrc=size=160x90:rate=10:duration=1", "-c:v", "libwebp_anim", "-loop", "0", webp]);
        var info = BackgroundVideoDecoder.StartInfo(video, true, true);
        Require(!info.UseShellExecute && info.ArgumentList.Contains("-an") && info.ArgumentList.Contains("0:v:0") && info.ArgumentList.Contains("file,pipe"), "Video pipeline maps only video, disables audio and network protocols.");
        var storage = new AppStorage(Path.Combine(root, "data"));
        var original = SHA256.HashData(File.ReadAllBytes(video));
        foreach (var file in new[] { video, gif, webp }) Wait(BackgroundVideoDecoder.ValidateAsync(file));
        using (var raster = new AnimatedRasterDecoder(webp))
        {
            using var first = raster.ReadFrame(0); using var later = raster.ReadFrame(600);
            Require(!first.Pixels.AsSpan(0, first.Width * first.Height * 4).SequenceEqual(later.Pixels.AsSpan(0, later.Width * later.Height * 4)), "Animated WebP advances actual pixels, not only frame counters.");
        }
        var imported = Wait(AppBackgroundService.ImportDynamicAsync(storage, video));
        Require(imported != video && SHA256.HashData(File.ReadAllBytes(video)).SequenceEqual(original), "Background import does not modify source.");
        var migrated = AppBackgroundService.Import(storage, gif);
        Require(File.Exists(migrated) && migrated != gif, "Synchronous legacy background migration completes without waiting for UI context.");
        var broken = Path.Combine(root, "broken.mp4"); File.WriteAllText(broken, "not video");
        var existing = Directory.GetFiles(Path.GetDirectoryName(imported)!).Length;
        try { Wait(AppBackgroundService.ImportDynamicAsync(storage, broken)); throw new Exception("Broken video accepted."); } catch (InvalidDataException) { }
        Require(Directory.GetFiles(Path.GetDirectoryName(imported)!).Length == existing, "Failed media import cleans temporary copy.");
        using var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760 }; window.Show(); Pump();
        var media = window.GetVisualDescendants().OfType<BackgroundMediaView>().Single();
        Exception? failure = null; media.PlaybackFailed += e => failure = e;
        foreach (var file in new[] { imported, gif, webp })
        {
            var before = media.FramesPresented; vm.Settings.BackgroundImagePath = file; vm.ApplySettings();
            Until(() => media.FramesPresented > before + 27 || failure is not null); if (failure is not null) throw failure;
            Require(media.PixelBytes <= 4L * 1280 * 720 && media.PixelBytes > 0, "Frames loop with bounded pixel storage.");
        }
        window.WindowState = WindowState.Minimized; Pump(); Require(media.PixelBytes == 0, "Hidden background releases pixel buffer.");
        var stopped = media.FramesPresented; window.WindowState = WindowState.Normal; Until(() => media.FramesPresented > stopped + 2);
        Require(audio.Loads == 0, "Video with audio never enters music audio player.");
        vm.Settings.BackgroundImagePath = null; vm.ApplySettings(); Require(media.Source is null, "Removing background clears dynamic surface.");
        var folderA = Path.Combine(root, "歌单 A"); var folderB = Path.Combine(root, "Playlist B"); var nested = Path.Combine(folderA, "nested"); var empty = Path.Combine(root, "Empty");
        Directory.CreateDirectory(nested); Directory.CreateDirectory(folderB); Directory.CreateDirectory(empty);
        Wave(Path.Combine(folderA, "a.wav")); Wave(Path.Combine(nested, "b.wav")); Wave(Path.Combine(folderB, "c.wav"));
        File.WriteAllText(Path.Combine(folderA, "a.lrc"), "[00:00.000]fixture lyrics", new UTF8Encoding(false));
        var created = Wait(vm.CreatePlaylistsFromFoldersAsync([folderA, folderB, empty, folderA]));
        Require(created.Count == 3 && created[0].Name == "歌单 A" && created[1].Name == "Playlist B" && created[2].TrackIds.Count == 0, "One named playlist per unique selected folder, including empty folders.");
        Require(created[0].TrackIds.Count == 2 && created[1].TrackIds.Count == 1 && vm.State.Tracks.Count == 3, "Recursive folder imports remain isolated and deduplicated.");
        Require(vm.State.Tracks.Any(t => vm.Lyrics.Read(t.Id)?.Contains("fixture lyrics") == true), "Folder import retains companion lyrics.");
        var prompt = PlayerDialog.CreatePlaylist(window); Pump();
        var dialog = window.OwnedWindows.OfType<PlayerDialog>().Single();
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CreatePlaylistFromFolders").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
        Require(prompt.IsCompleted && prompt.Result is { FromFolders: true }, "New playlist dialog offers folder creation without name submission.");
        vm.Navigate("settings"); Pump();
        Require(!vm.Settings.StartOnLogin && window.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "StartOnLoginToggle").IsChecked == false, "Login startup defaults off with no OS side effects in tests.");
        Require(AutoStartService.BuildWindowsCommand(Path.Combine(root, "space path", "Nonet.exe")).StartsWith('"'), "Windows startup executable is quoted.");
        Require(AutoStartService.BuildLinuxEntry(Path.Combine(root, "space$%path", "Nonet")).Contains("%%"), "Linux startup escapes desktop-entry field codes.");
        var plist = XDocument.Parse(AutoStartService.BuildMacEntry(Path.Combine(root, "A&B", "Nonet")));
        Require(plist.Descendants("string").Last().Value.Contains("A&B"), "macOS startup uses XML-escaped exact program arguments.");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(root, "settings.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close(); AppBackgroundService.CleanupUnused(storage, null, 0);
        Require(File.Exists(video) && File.Exists(gif) && Directory.GetFiles(Path.GetDirectoryName(imported)!).Length == 0, "Managed cleanup never removes original background media.");
        Console.WriteLine("PASS silent video/GIF/WebP loop, bounded frames, hide/resume, malformed media cleanup, folder playlists, recursive import, lyrics, dialog, startup default/escaping and original preservation.");
    }
    private static void Generate(string[] arguments)
    {
        var info = new ProcessStartInfo(BackgroundVideoDecoder.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments)) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!; var error = process.StandardError.ReadToEnd(); process.WaitForExit(); Require(process.ExitCode == 0, error);
    }
    private static void Wave(string file)
    {
        using var writer = new BinaryWriter(File.Create(file)); var count = 16000;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(count + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count); writer.Write(new byte[count]);
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Until(Func<bool> condition) { var until = DateTime.UtcNow.AddSeconds(15); while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } Require(condition(), "Media/UI timeout."); Pump(); }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.Result; }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public int Loads { get; private set; } public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(1); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) { Loads++; return Task.CompletedTask; }
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
