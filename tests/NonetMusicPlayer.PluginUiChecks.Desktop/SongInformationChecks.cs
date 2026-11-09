using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>自产音调验证真实标签与音频规格、只读加载、云端/损坏回退和最小窗口下固定关闭入口。</summary>
internal static class SongInformationChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "长路径 - 音乐信息.wav"); Wave(path);
        using (var audio = TagLib.File.Create(path))
        {
            audio.Tag.Title = "夜空中的合成音调"; audio.Tag.Performers = ["测试歌手"]; audio.Tag.Album = "声音实验室";
            audio.Tag.AlbumArtists = ["合辑艺术家"]; audio.Tag.Composers = ["测试作曲者"]; audio.Tag.Genres = ["Electronic"]; audio.Tag.Year = 2026;
            audio.Tag.Track = 3; audio.Tag.TrackCount = 12; audio.Tag.Disc = 1; audio.Tag.DiscCount = 2; audio.Save();
        }
        var original = SHA256.HashData(File.ReadAllBytes(path)); var storage = new AppStorage(Path.Combine(root, "data"));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new SilentAudio()); vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var owner = new MainWindow { DataContext = vm, Width = 960, Height = 720 }; owner.Show(); Pump();
        var track = new TrackItem("fixture", "旧标题", "旧歌手", "旧专辑", path, ".wav", 1);
        foreach (var language in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            vm.Settings.Language = language; vm.ApplySettings(); L10n.SetLanguage(language); Pump();
            var dialog = new SongInfoDialog(owner, track); dialog.Show(owner); Wait(dialog.MetadataLoad); Pump();
            var values = dialog.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text ?? "").ToArray();
            Check(values.Contains("夜空中的合成音调") && values.Contains("声音实验室") && values.Contains("合辑艺术家") && values.Contains("测试作曲者"), "Read actual tags without altering library model");
            Check(values.Any(t => t.Contains("2026") && t.Contains("Electronic")) && values.Any(t => t.Contains("3 / 12") && t.Contains("1 / 2")), "Release, genre, track and disc metadata");
            Check(values.Any(t => t.Contains("48 kHz") && t.Contains("16 bit") && t.Contains(L10n.T("SongInfo.Stereo")))
                && values.Any(t => t.Contains("1,536") || t.Contains("1536")), "Real PCM sample rate, depth, channels and bitrate: " + language + " / " + string.Join(" | ", values));
            Check(values.Contains(path) && dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SongFilePath").IsEnabled, "Full selectable path and reveal action retained");
            var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SongInfoScroll");
            var close = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseSongInfo");
            Check(!close.GetVisualAncestors().Contains(scroll), "Close action stays outside scrolled content");
            using (var frame = dialog.CaptureRenderedFrame()!) frame.Save(Path.Combine(root, "song-info-" + language + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            dialog.Close();
        }
        Check(track.Title == "旧标题" && original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Metadata display changes neither file nor stored track");
        vm.Settings.Language = "zh-CN"; vm.ApplySettings(); L10n.SetLanguage("zh-CN"); owner.Width = 640; owner.Height = 480; Pump();
        var small = new SongInfoDialog(owner, track); small.Show(owner); Wait(small.MetadataLoad); Pump();
        var closeSmall = small.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseSongInfo"); var point = closeSmall.TranslatePoint(default, small)!.Value;
        Check(small.Bounds.Width <= owner.Bounds.Width && small.Bounds.Height <= owner.Bounds.Height && point.Y + closeSmall.Bounds.Height <= small.Bounds.Height, "Minimum window retains visible close action");
        using (var frame = small.CaptureRenderedFrame()!) frame.Save(Path.Combine(root, "song-info-small.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); small.Close();
        // 云端曲目即使带有本地样式路径，也不能读取该文件或泄漏它的标签。
        var cloud = new TrackItem("cloud", "云端曲目", "歌手", "云专辑", path, ".flac", 0) { ProviderId = "fixture.cloud" };
        var remote = new SongInfoDialog(owner, cloud); remote.Show(owner); Wait(remote.MetadataLoad); Pump();
        var remoteText = remote.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text).ToArray();
        Check(remoteText.Contains("云端曲目") && !remoteText.Contains("测试作曲者") && !remoteText.Contains(path) && !remoteText.Contains("0 MiB"), "Cloud metadata never probes local or network audio and hides unknown size"); remote.Close();
        var missing = new SongInfoDialog(owner, new TrackItem("missing", "保留标题", "歌手", "专辑", Path.Combine(root, "missing.wav"), ".wav", 0));
        missing.Show(owner); Wait(missing.MetadataLoad); Pump(); Check(missing.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == L10n.T("SongInfo.FileUnavailable")), "Missing file uses saved information"); missing.Close();
        var brokenPath = Path.Combine(root, "broken.wav"); File.WriteAllBytes(brokenPath, new byte[80]);
        var broken = new SongInfoDialog(owner, new TrackItem("broken", "损坏标签", "歌手", "专辑", brokenPath, ".wav", 80));
        broken.Show(owner); Wait(broken.MetadataLoad); Pump(); Check(broken.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == L10n.T("SongInfo.MetadataUnavailable")), "Corrupt tags do not break dialog"); broken.Close();
        var closing = new SongInfoDialog(owner, track); closing.Show(owner); closing.Close(); Wait(closing.MetadataLoad); owner.Close();
        Console.WriteLine("PASS rich song tags/audio/file information, three languages, read-only async loading, cloud/missing/corrupt fallback, compact scroll layout and early close. " + root);
    }
    private static void Wave(string path)
    {
        using var output = new BinaryWriter(File.Create(path)); const int dataSize = 48000 * 2 * 2;
        output.Write("RIFF"u8); output.Write(dataSize + 36); output.Write("WAVEfmt "u8); output.Write(16); output.Write((short)1); output.Write((short)2);
        output.Write(48000); output.Write(192000); output.Write((short)4); output.Write((short)16); output.Write("data"u8); output.Write(dataSize); output.Write(new byte[dataSize]);
    }
    private static void Wait(Task task) { var end = Environment.TickCount64 + 10000; while (!task.IsCompleted) { if (Environment.TickCount64 > end) throw new TimeoutException(); Pump(); } task.GetAwaiter().GetResult(); Pump(); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class SilentAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
