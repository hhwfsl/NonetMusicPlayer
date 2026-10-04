using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>当前修订的独立夹具；所有删除、迁移和更新只作用于 artifacts 内的测试目录。</summary>
internal static class DesktopRevisionChecks
{
    public static void Run(string output)
    {
        var root = Path.Combine(output, "revision-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        ExperienceRevisionChecks.Run(output);
        CheckLyrics(root); CheckDatabase(root); CheckUpdates(root);
        var storage = new AppStorage(Path.Combine(root, "ui-data")); var scanner = new MusicLibraryScanner(storage); var audio = new ApplicationIntegrationChecks.SilentAudioPlayer();
        using var vm = new MainViewModel(scanner, audio); var owner = new MainWindow { DataContext = vm }; owner.Show(); Pump(owner);
        var sources = Path.Combine(root, "originals"); Directory.CreateDirectory(sources); var wav = Path.Combine(sources, "song.wav"); WriteWave(wav);
        var sourceLyric = Path.ChangeExtension(wav, ".lrc"); File.WriteAllText(sourceLyric, "[00:00.000]光[00:00.300]の[00:00.700]道[00:01.000]\n[00:00.000]中文译文[00:01.000]\n[00:01.000]第二句\n");
        var first = ApplicationIntegrationChecks.Wait(vm.ImportAsync([wav])).Single(); var auto = vm.State.Playlists.Single(p => !p.IsSystem);
        var other = vm.CreatePlaylist("other"); vm.AddToPlaylist(other, [first]);
        ApplicationIntegrationChecks.Wait(vm.PlayTrackAsync(first)); audio.Position = TimeSpan.FromSeconds(.5); vm.RefreshPlaybackState();
        vm.Navigate("lyrics"); Pump(owner); var page = owner.GetVisualDescendants().OfType<LyricsView>().Single();
        Require(page.GetVisualDescendants().OfType<KaraokeLine>().Any(), "Page uses timed glyph renderer");
        var list = page.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "LyricLines");
        var point = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), owner)!.Value;
        owner.MouseWheel(point, new Vector(0, -1)); Pump(owner); Require(page.IsPreviewing, "Manual scroll enters preview");
        var watch = Stopwatch.StartNew(); page.RestorePlaybackFocus(); Pump(owner);
        Require(!page.IsPreviewing && list.SelectedIndex == 0 && watch.Elapsed.TotalMilliseconds < 500, "Focus restore immediately cancels preview and centers real playback");
        audio.Position = TimeSpan.FromSeconds(1.5); vm.RefreshPlaybackState(); page.RestorePlaybackFocus(); Pump(owner); Require(list.SelectedIndex == 1, "Focus restore samples current audio position");
        vm.LyricLines.Clear(); for (var i = 0; i < 100; i++) vm.LyricLines.Add(new(i, "第 " + i + " 行歌词", "translation")); Pump(owner);
        audio.Position = TimeSpan.FromSeconds(60); vm.RefreshPlaybackState(); page.RestorePlaybackFocus(); Pump(owner);
        Require(list.SelectedIndex == 60, "Virtualized focus restore reaches a distant line without delay");
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(); Require(scroll.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden && scroll.Offset.Y > 1000, "Lyrics center far-away row with hidden scrollbar");
        using (var frame = owner.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "revision-lyrics.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        using (var overlay = new DesktopLyricsWindow(owner, vm))
        {
            overlay.Toggle(); Pump(overlay); var size = new Size(overlay.Width, overlay.Height);
            vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, new string('光', 200), new string('译', 200))); vm.RefreshPlaybackState(); Pump(overlay);
            Require(Math.Abs(overlay.Width - size.Width) < 1 && Math.Abs(overlay.Height - size.Height) < 1, "Long lyrics never resize desktop overlay");
            Require(overlay.GetVisualDescendants().OfType<KaraokeLine>().All(t => !t.Wrap && t.TextSize <= vm.Settings.DesktopLyricsFontSize), "Desktop lines use fixed-size clipped marquee");
            overlay.HideLyrics();
        }
        vm.Navigate("terminal"); Pump(owner); var terminal = owner.GetVisualDescendants().OfType<TerminalView>().Single();
        var unused = vm.CreatePlaylist("unused"); vm.DeletePlaylist(unused); Pump(owner);
        Require(vm.Page == "terminal" && ReferenceEquals(terminal, owner.GetVisualDescendants().OfType<TerminalView>().Single()), "Sidebar delete preserves terminal instance and route");
        vm.Navigate("settings"); Pump(owner); var settings = owner.GetVisualDescendants().OfType<SettingsView>().Single(); var settingsScroll = settings.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
        settingsScroll.Offset = new Vector(0, 500); Pump(owner); var offset = settingsScroll.Offset;
        owner.WindowState = WindowState.Minimized; Pump(owner); owner.WindowState = WindowState.Normal; Pump(owner);
        Require(ReferenceEquals(settings, owner.GetVisualDescendants().OfType<SettingsView>().Single()) && Math.Abs(settingsScroll.Offset.Y - offset.Y) < 2, "Minimize does not rebuild settings or reset scroll");
        Require(settings.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == L10n.T("Update.About")), "About includes version and repository");
        vm.State.History.Insert(0, first.Id); vm.Navigate("history"); vm.RemoveTracks([first]);
        Require(!vm.State.History.Contains(first.Id) && vm.State.Tracks.Contains(first) && auto.TrackIds.Contains(first.Id) && other.TrackIds.Contains(first.Id), "Recent removal does not delete music or playlist references");
        vm.Navigate("album:" + first.Album); vm.RemoveTracks([first]); Require(vm.State.Tracks.Contains(first), "Album deletion is rejected");
        vm.Navigate("artist:" + first.Artist); vm.RemoveTracks([first]); Require(vm.State.Tracks.Contains(first), "Artist deletion is rejected");
        vm.Navigate("playlist:" + auto.Id); vm.RemoveTracks([first]); Require(vm.State.Tracks.Contains(first) && File.Exists(vm.Lyrics.PathFor(first.Id)), "Removing one reference keeps shared metadata");
        vm.Navigate("playlist:" + other.Id); vm.RemoveTracks([first]);
        Require(!vm.State.Tracks.Any(t => t.Id == first.Id) && !File.Exists(vm.Lyrics.PathFor(first.Id)) && File.Exists(wav) && File.Exists(sourceLyric), "Last reference removes only managed data, never original audio or lyrics");
        vm.UndoCommand.Execute(null); Require(vm.State.Tracks.Any(t => t.Id == first.Id) && File.Exists(vm.Lyrics.PathFor(first.Id)) && other.TrackIds.Contains(first.Id), "One undo restores all managed data and references");
        vm.RemoveLibraryTracks([first]); Require(vm.State.Playlists.All(p => !p.TrackIds.Contains(first.Id)) && !vm.State.Tracks.Any(t => t.Id == first.Id) && File.Exists(wav) && File.Exists(sourceLyric), "Explicit browse deletion removes all references but retains sources");
        owner.Close(); Console.WriteLine("PASS revision UI: immediate lyrics, fixed marquee frame, persistent views, statistical pages and reference cleanup");
    }
    private static void CheckLyrics(string root)
    {
        var lines = LyricsService.Parse("[00:01.000]光[00:01.300]の[00:01.700]道[00:02.000]\n[00:01.000]中文译文[00:02.000]\n[00:03]\n[00:04:20]下一句\n[00:05;200]最后一句");
        Require(lines.Count == 3 && lines[0].Words.Count == 3 && lines[0].Translation == "中文译文" && lines[0].TranslationWords.Count == 0, "Square word timestamps distinguish ordinary translation and remove blank lines");
        Require(Math.Abs(LyricsService.CharacterProgress(lines[0].Words, 1.5) - 1.5) < .001, "Word progress uses explicit fragment durations");
        Require(LyricsService.Parse("[00:01]<00:01.00>光<00:01.30>道<00:02.00>").Single().Words.Count == 2, "Angle-tag Enhanced LRC supported");
        Require(LyricsService.Parse("[00:01][00:02]重复一句").Count == 2, "Ordinary multi-time LRC retained");
        // 默认使用内置夹具；需要验证外部歌词时显式指定，不绑定开发机器的磁盘。
        var supplied = Environment.GetEnvironmentVariable("NONET_TEST_LRC");
        if (File.Exists(supplied)) { var parsed = LyricsService.Parse(File.ReadAllText(supplied)); Require(parsed.Any(l => l.Words.Count > 1) && parsed.Any(l => l.Translation.Length > 0) && parsed.All(l => !string.IsNullOrWhiteSpace(l.Text)), "User sample parses word timing, translations and no blank rows"); Console.WriteLine("PASS supplied LDDC lyrics: " + parsed.Count + " rows"); }
        var audioPath = Path.Combine(root, "embedded.wav"); WriteWave(audioPath);
        using (var tag = TagLib.File.Create(audioPath)) { tag.Tag.Lyrics = "[00:00]内[00:00.500]嵌[00:01.000]"; tag.Save(); }
        var storage = new AppStorage(Path.Combine(root, "embedded-data")); var scanner = new MusicLibraryScanner(storage); var imported = scanner.Scan(audioPath).Single(); var lyrics = new LyricsService(storage.DefaultLyricsFolder);
        Require(!File.Exists(lyrics.PathFor(imported.Id)) && LyricsService.Parse(lyrics.ReadForTrack(imported.Id, audioPath)).Single().Words.Count == 2, "Embedded enhanced lyrics read directly without exported file");
        var manual = Path.Combine(root, "manual.lrc"); File.WriteAllText(manual, "[00:01]手动歌词"); lyrics.Import(imported.Id, manual);
        Require(LyricsService.Parse(lyrics.ReadForTrack(imported.Id, audioPath)).Single().Text == "手动歌词", "Manual lyrics override embedded lyrics");
        Console.WriteLine("PASS lyric parsing and embedded/manual precedence");
    }
    private static void CheckDatabase(string root)
    {
        var storage = new AppStorage(Path.Combine(root, "migration-data")); var old = new AppState { LastPosition = 12.5, Tracks = [new TrackItem("unicode", "光の道標", "鹿乃", "album", "source.flac", ".flac", 1)] };
        AppStorage.AtomicWrite(Path.Combine(storage.Root, "state.json"), JsonSerializer.Serialize(old, AppStorage.Json));
        var migrated = storage.Load(); storage.Save(migrated);
        Require(File.Exists(storage.DatabasePath) && File.Exists(storage.DatabaseBackupPath) && File.Exists(Path.Combine(storage.Root, "state.json")), "Migration retains original JSON and writes SQLite plus consistent backup");
        var reloaded = new AppStorage(storage.Root).Load(); Require(reloaded.Tracks.Single().Title == "光の道標" && reloaded.LastPosition == 12.5, "SQLite reload preserves Unicode and state");
        var database = new LibraryDatabase(storage.DatabasePath); database.Read(); migrated.LastPosition = 13; database.Save(migrated); Require(database.LastWrittenRows == 1, "Position update writes one row only");
        database.Save(migrated); Require(database.LastWrittenRows == 0, "Unchanged save writes no entity rows");
        migrated.Tracks[0].Title = "new"; database.Save(migrated); Require(database.LastWrittenRows == 1 && database.Read().Tracks[0].Title == "new", "Mutable track fingerprint detects changes");
        Require(storage.ReadBackup().Tracks[0].Title == "光の道標", "Database backup reads a consistent previous snapshot");
        var next = Path.Combine(root, "migrated-copy"); storage.ConfigureDataDirectory(next); storage.Save(migrated); Require(storage.ReadBackup(Path.Combine(next, "library.db")).LastPosition == 13, "Pending relocation mirrors database and latest state");
        var foreign = Path.Combine(root, "foreign"); Directory.CreateDirectory(foreign); File.WriteAllText(Path.Combine(foreign, "library.db.bak"), "keep"); Reject(() => storage.SetBackupFolder(foreign), "Foreign database backup refused");
        var benchmark = new AppState { Tracks = Enumerable.Range(0, 10_000).Select(i => new TrackItem("t" + i, "Benchmark 曲目 " + i, "artist", "album", "file" + i + ".flac", ".flac", 1234)).ToList() };
        var db = new LibraryDatabase(Path.Combine(root, "benchmark.db")); db.Save(benchmark); const int iterations = 12;
        var sw = Stopwatch.StartNew(); for (var i = 0; i < iterations; i++) { benchmark.LastPosition = i; db.Save(benchmark, playbackOnly: true); } var sqliteMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); for (var i = 0; i < iterations; i++) { benchmark.LastPosition = i; AppStorage.AtomicWrite(Path.Combine(root, "benchmark.json"), JsonSerializer.Serialize(benchmark, AppStorage.Json)); } var jsonMs = sw.Elapsed.TotalMilliseconds;
        File.WriteAllText(Path.Combine(root, "storage-benchmark.txt"), $"10,000 tracks / {iterations} position saves\nSQLite incremental: {sqliteMs:0.00} ms\nSource-generated JSON full snapshot: {jsonMs:0.00} ms\nSQLite changed rows/save: {db.LastWrittenRows}\n");
        Console.WriteLine($"BENCHMARK 10000 tracks/{iterations} saves: SQLite {sqliteMs:0.0}ms; source-generated JSON {jsonMs:0.0}ms");
        Console.WriteLine("PASS SQLite migration, incremental rows, backups and relocation");
    }
    private static void CheckUpdates(string root)
    {
        const string version = "0.3.0-beta.99"; var rid = ReleaseUpdateService.PlatformRid;
        var executable = OperatingSystem.IsWindows() ? "Nonet.exe" : OperatingSystem.IsMacOS() ? "Contents/MacOS/Nonet" : "Nonet";
        var manifest = new UpdateManifest { Version = version, Rid = rid, Files = new() { ["notes.txt"] = Hash("new notes"), [executable] = Hash("new executable") } };
        var zip = Path.Combine(root, "release.zip"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "notes.txt", "new notes"); WriteEntry(archive, executable, "new executable"); WriteEntry(archive, ReleaseUpdateService.ManifestName, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        var bytes = File.ReadAllBytes(zip); var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var releaseJson = JsonSerializer.Serialize(new[] { new { tag_name = "desktop-v" + version, body = "notes", draft = false, assets = new[] { new { name = "NonetMusicPlayer.Desktop-" + version + "-" + rid + ".zip", browser_download_url = ReleaseUpdateService.Repository + "/releases/download/desktop-v" + version + "/package.zip", size = bytes.Length, digest } } } });
        using var http = new HttpClient(new FakeHttpHandler(releaseJson, bytes)); var service = new ReleaseUpdateService(http);
        var release = ApplicationIntegrationChecks.Wait(service.CheckAsync("0.3.0-beta.4", rid)); Require(release?.Asset is not null, "Select matching desktop release, not CLI/Android");
        var progress = new List<double>(); var prepared = ApplicationIntegrationChecks.Wait(service.DownloadAsync(release!, Path.Combine(root, "update-data"), new ImmediateProgress(progress.Add)));
        Require(progress.Last() == 1 && File.Exists(Path.Combine(prepared.Payload, executable.Replace('/', Path.DirectorySeparatorChar))), "Download progress, digest and manifest verification");
        var installation = Path.Combine(root, "installation"); Directory.CreateDirectory(installation); var oldExe = Path.Combine(installation, executable.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(oldExe)!); File.WriteAllText(oldExe, "old executable"); File.WriteAllText(Path.Combine(installation, "notes.txt"), "old notes");
        var data = Path.Combine(installation, "Data"); Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "keep.txt"), "music data");
        var plan = new UpdateApplyPlan { Installation = installation, Payload = prepared.Payload, Executable = executable, DataRoot = data, BackupRoot = Path.Combine(data, "Backups"), Manifest = manifest };
        UpdateInstaller.Apply(plan, Path.Combine(prepared.StageRoot, "rollback"));
        Require(File.ReadAllText(oldExe) == "new executable" && File.ReadAllText(Path.Combine(data, "keep.txt")) == "music data", "Apply changes only application files");
        // 已经替换第一份文档后，让主程序目标发生目录冲突，验证真正的中途回滚。
        File.WriteAllText(Path.Combine(installation, "notes.txt"), "rollback original"); File.Delete(oldExe); Directory.CreateDirectory(oldExe);
        Reject(() => UpdateInstaller.Apply(plan, Path.Combine(prepared.StageRoot, "rollback-midway")), "Midway replacement fails safely");
        Require(File.ReadAllText(Path.Combine(installation, "notes.txt")) == "rollback original" && File.ReadAllText(Path.Combine(data, "keep.txt")) == "music data", "Midway rollback restores original files and data");
        Directory.Delete(oldExe); File.WriteAllText(oldExe, "new executable");
        var originalDigest = release!.Asset!.Digest; release.Asset.Digest = "sha256:" + new string('0', 64);
        Reject(() => ApplicationIntegrationChecks.Wait(service.DownloadAsync(release, Path.Combine(root, "bad-digest-data"))), "Download digest mismatch rejected");
        Require(!Directory.EnumerateDirectories(Path.Combine(root, "bad-digest-data", "Updates")).Any(), "Failed download cleans only its own stage"); release.Asset.Digest = originalDigest;
        Require(ApplicationIntegrationChecks.Wait(service.CheckAsync("0.3.0-beta.4", "wrong-rid")) is null, "Wrong platform ignored");
        Reject(() => ReleaseUpdateService.ValidateRelative("../outside"), "Zip slip rejected"); Reject(() => ReleaseUpdateService.ValidateRelative("Data/state.json"), "User data in package rejected"); Reject(() => ReleaseUpdateService.ValidateRelative("Nonet.bootstrap.json"), "Bootstrap in package rejected");
        File.WriteAllText(Path.Combine(prepared.Payload, "notes.txt"), "tampered"); Reject(() => UpdateInstaller.Apply(plan, Path.Combine(prepared.StageRoot, "rollback-2")), "Staged tampering rejected before replacement");
        Require(File.ReadAllText(oldExe) == "new executable", "Rejected update leaves installed executable untouched");
        Require(ReleaseUpdateService.CompareVersions("0.3.0-beta.10", "0.3.0-beta.9") > 0 && ReleaseUpdateService.CompareVersions("0.3.0", "0.3.0-beta.99") > 0, "Semantic prerelease version order");
        Require(ReleaseUpdateService.CompareVersions("999999999999.0.0", "0.3.0") == 0, "Untrusted overflowing release tag ignored");
        Console.WriteLine("PASS offline GitHub update simulation: selection, progress, SHA, manifest, apply and rejection; no upload");
    }
    private sealed class FakeHttpHandler(string json, byte[] package) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(json) : new ByteArrayContent(package) }); }
    private sealed class ImmediateProgress(Action<double> action) : IProgress<double> { public void Report(double value) => action(value); }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void WriteEntry(ZipArchive archive, string path, string value) { using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(value); }
    private static void WriteWave(string path)
    {
        using var writer = new BinaryWriter(File.Create(path)); const int size = 48000 * 8 * 4;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(size + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Write(new byte[size]);
    }
    private static void Pump(Window window) { for (var i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); Thread.Sleep(10); } }
    private static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); }
    private static void Reject(Action action, string description) { try { action(); } catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException) { return; } throw new InvalidOperationException(description); }
}
