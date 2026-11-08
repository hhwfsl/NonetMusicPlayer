using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>同一歌词页内重载相同 LRC，检查保留的虚拟行仍能绘制主题色，避免重开页面掩盖故障。</summary>
internal static class LyricHighlightChecks
{
    private const string Lines = "[00:00]First line\n[00:00]第一句翻译\n[00:04]Second line\n[00:04]第二句翻译\n[00:08]Third line\n[00:12]Last line";
    public static void Run(string output, string? styleFile)
    {
        var motion = Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION");
        Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
        try
        {
            var storage = new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")));
            using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer());
            vm.Settings.CloseToTray = vm.Settings.ConfirmClose = false; vm.Settings.Theme = "Dark"; vm.Settings.Accent = "#18D568";
            var track = new TrackItem("highlight-fixture", "Fixture", "Artist", "Album", Path.Combine(storage.Root, "unused.wav"), ".wav", 20);
            vm.Lyrics.Save(track.Id, Lines); vm.State.Tracks.Add(track); vm.CurrentTrack = track;
            vm.PlaybackDuration = 20; vm.PlaybackPosition = 1.25;
            var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("highlight-fixture", [StyleSheetContract.Read(input)]); }
                window.Show(); vm.Navigate("lyrics"); Pump(window);
                var page = window.GetVisualDescendants().OfType<LyricsView>().Single();
                var list = page.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "LyricLines");
                var original = Row(0, false); var translation = Row(0, true);
                Active(original, "initial-original"); Active(translation, "initial-translation");
                var old = vm.LyricLines[0];
                vm.ReloadLyrics(); Pump(window);
                Require(!ReferenceEquals(old, vm.LyricLines[0]) && old == vm.LyricLines[0], "Real reload creates new value-equal line records");
                Require(ReferenceEquals(page, window.GetVisualDescendants().OfType<LyricsView>().Single()) && ReferenceEquals(original, Row(0, false)), "Equal snapshot retains the page and realized line controls");
                Active(original, "reload-original"); Active(translation, "reload-translation");
                vm.PlaybackPosition = 5.25; Pump(window);
                Active(Row(1, false), "next-original"); Active(Row(1, true), "next-translation");
                Require(original.ActiveClip.Bounds.Width == 0, "Previous line clears highlight after crossing a timestamp");
                // 播放时同样经过重载路径；暂停后保持当前位置，不能靠时钟或重开页面修补颜色。
                vm.IsPlaying = true;
                for (var i = 0; i < 3; i++) { vm.ReloadLyrics(); Pump(window); Active(Row(1, false), "playing-reload-" + i); }
                vm.IsPlaying = false; vm.PlaybackPosition = 1.25; Pump(window); Active(Row(0, false), "seek-back");
                vm.Settings.Accent = "#D53399"; vm.ApplySettings(); vm.ReloadLyrics(); Pump(window);
                Require(ReferenceEquals(page, window.GetVisualDescendants().OfType<LyricsView>().Single()), "Theme update and reload do not recreate the lyrics page");
                Active(Row(0, false), "theme-reload");
                var other = new TrackItem("highlight-other", "Other fixture", "Artist", "Album", track.FilePath, ".wav", 20);
                vm.Lyrics.Save(other.Id, Lines); vm.State.Tracks.Add(other); vm.CurrentTrack = other; Pump(window);
                Require(ReferenceEquals(original, Row(0, false)), "Switching tracks with identical lyrics retains equal visible content");
                Active(Row(0, false), "same-lyrics-other-track");
                // 内容真正改变时仍更新行；逐字歌词继续按时间推进，而不是全部按整行染色。
                vm.Lyrics.Save(other.Id, Lines.Replace("First line", "Updated first line")); vm.ReloadLyrics(); Pump(window);
                Require(Row(0, false).Text == "Updated first line", "Changed lyric content replaces the displayed row"); Active(Row(0, false), "changed-content");
                vm.Lyrics.Save(other.Id, "[00:00]<00:00>Alpha <00:02>Beta<00:04>\n[00:00]逐字翻译\n[00:05]Next line");
                vm.ReloadLyrics(); vm.PlaybackPosition = 1; Pump(window);
                Require(vm.LyricLines[0].Words.Count > 0, "Enhanced LRC fixture has word timings");
                var early = Row(0, false).ActiveClip.Bounds.Width; Active(Row(0, false), "word-early");
                vm.ReloadLyrics(); Pump(window); Active(Row(0, false), "word-reload");
                vm.PlaybackPosition = 3.5; Pump(window);
                Require(Row(0, false).ActiveClip.Bounds.Width > early, "Word-timed highlight continues to advance after reload"); Active(Row(0, true), "word-translation");
                Console.WriteLine("PASS retained line/translation theme-color pixels after identical LRC reload, playback, theme/track changes, seek, changed content and word-timed regression" + (styleFile is null ? "" : ", external style"));

                KaraokeLine Row(int index, bool translated)
                {
                    var row = list.ContainerFromIndex(index);
                    Require(row is not null, $"Playing lyric {index} has a realized row: selected={list.SelectedIndex}, count={list.ItemCount}");
                    return row!.GetVisualDescendants().OfType<KaraokeLine>().Single(line => line.Tag is ValueTuple<LyricLine, bool> tag && tag.Item2 == translated);
                }
                void Active(KaraokeLine line, string name)
                {
                    var file = Path.Combine(output, name + ".png");
                    using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(line.Bounds.Width), (int)Math.Ceiling(line.Bounds.Height)))) { bitmap.Render(line); bitmap.Save(file, PngBitmapEncoderOptions.Default); }
                    using var pixels = SkiaSharp.SKBitmap.Decode(file);
                    var color = line.TextColor;
                    var count = pixels.Pixels.Count(pixel => pixel.Alpha > 180 && Math.Abs(pixel.Red - color.R) < 15 && Math.Abs(pixel.Green - color.G) < 15 && Math.Abs(pixel.Blue - color.B) < 15);
                    Require(count > 30 && line.ActiveClip.Bounds.Width > 0, $"Active lyric paints theme-color glyphs at {name}: pixels={count}, clip={line.ActiveClip.Bounds}");
                }
            }
            finally { window.ExitApplication(); }
        }
        finally { Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", motion); }
    }
    private static void Pump(Window window) { for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); } }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
