using System.Reflection;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

internal static class DataStatisticsChecks
{
    public static void Run(string output)
    {
        var fixture = Path.Combine(output, "data-statistics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        CheckDirectories(fixture); CheckStatistics(fixture); CheckMovedTrackImport(fixture); CheckCustomLyricsDirectory(fixture);
        Console.WriteLine("Data/statistics checks passed: portable bootstrap, non-destructive migration, latest-state mirroring, owned covers, actual listening and restore.");
    }
    private static void CheckDirectories(string fixture)
    {
        var environment = Environment.GetEnvironmentVariable("LMP_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("LMP_DATA_DIR", null);
            var installation = Path.Combine(fixture, "portable");
            var storage = new AppStorage(installationDirectory: installation);
            Require(storage.Root == Path.Combine(installation, "Data") && storage.BackupFolder == Path.Combine(storage.Root, "Backups"), "Default data lives under installation/Data, not AppData");
            using (var bootstrap = JsonDocument.Parse(File.ReadAllText(storage.Paths.BootstrapPath)))
                Require(bootstrap.RootElement.GetProperty("dataDirectory").GetString() == "Data" && !Path.IsPathRooted(bootstrap.RootElement.GetProperty("backupDirectory").GetString()!), "Portable bootstrap stores installation-local paths relatively");
            var cover = Path.Combine(storage.ArtworkFolder, "managed.png"); File.WriteAllText(cover, "fixture asset");
            var state = new AppState { Tracks = [Track("portable")], GroupCovers = new() { ["artist:艺人"] = cover }, Settings = new() { TitleButtonsOnLeft = true, KeyBindings = new() { ["favorite"] = "Ctrl+Shift+D" } } };
            state.Tracks[0].CoverPath = cover; storage.Save(state); storage.Save(state);
            Require(File.Exists(storage.DatabaseBackupPath) && !File.Exists(Path.Combine(storage.Root, "state.json.bak")), "Backups are categorized under configured directory");
            var movedInstallation = Path.Combine(fixture, "portable-moved"); DataDirectoryService.CopyNonDestructive(installation, movedInstallation);
            var moved = new AppStorage(installationDirectory: movedInstallation); var relocated = moved.Load();
            Require(moved.Root == Path.Combine(movedInstallation, "Data") && moved.BackupFolder == Path.Combine(moved.Root, "Backups"), "Moving the complete portable folder relocates data and backup roots");
            Require(relocated.Tracks[0].CoverPath == Path.Combine(moved.ArtworkFolder, "managed.png") && relocated.GroupCovers["artist:艺人"] == relocated.Tracks[0].CoverPath, "Relocation remaps managed artwork references");
            Require(File.Exists(cover) && File.Exists(relocated.Tracks[0].CoverPath), "Portable move test is non-destructive");
            var next = Path.Combine(fixture, "new-data"); var migration = storage.ConfigureDataDirectory(next);
            Require(migration.RequiresRestart && storage.Root != next && storage.PendingRoot == next && File.Exists(Path.Combine(next, "library.db")), "Directory migration copies but keeps current runtime rooted until restart");
            state.ListeningEntries.Add(new() { Date = "2026-10-01", TrackId = "portable", Title = "歌曲", Artist = "艺人", Seconds = 3.5, PlayCount = 1 });
            var laterCover = Path.Combine(storage.ArtworkFolder, "later.png"); File.WriteAllText(laterCover, "later asset"); state.GroupCovers["album:专辑"] = laterCover;
            storage.Save(state); var pendingState = storage.ReadBackup(Path.Combine(next, "library.db"));
            Require(pendingState.ListeningEntries.Single().Seconds == 3.5 && pendingState.GroupCovers["album:专辑"] == Path.Combine(next, "Artwork", "later.png") && File.Exists(pendingState.GroupCovers["album:专辑"]), "Subsequent state and new resources are mirrored until restart");
            var externalBackup = Path.Combine(fixture, "independent-backups"); storage.SetBackupFolder(externalBackup); storage.Save(state);
            using (var bootstrap = JsonDocument.Parse(File.ReadAllText(storage.Paths.BootstrapPath)))
                Require(bootstrap.RootElement.GetProperty("backupDirectory").GetString() == externalBackup, "Changing backup after pending migration updates next-start pointer");
            Require(storage.ReadBackup(Path.Combine(next, "library.db")).Settings.BackupFolder == externalBackup && File.Exists(storage.DatabaseBackupPath), "Pending state honors latest independent backup directory");
            var plugin = Path.Combine(storage.PluginsFolder, "demo.test"); Directory.CreateDirectory(plugin);
            File.WriteAllText(Path.Combine(plugin, "main.py"), "# public plugin entry"); File.WriteAllText(Path.Combine(plugin, "secrets.json"), "private"); File.WriteAllText(Path.Combine(plugin, "client.key"), "private");
            storage.SyncPendingResources(); var mirroredPlugin = Path.Combine(next, "Plugins", "demo.test");
            Require(File.Exists(Path.Combine(mirroredPlugin, "main.py")) && !File.Exists(Path.Combine(mirroredPlugin, "secrets.json")) && !File.Exists(Path.Combine(mirroredPlugin, "client.key")), "Pending plugin sync preserves package entry and excludes known private credential files");
            Throws(() => storage.ArchivePendingPluginRemoval("../outside"), "Pending plugin archive rejects unsafe IDs");
            var removed = storage.ArchivePendingPluginRemoval("demo.test");
            Require(removed is not null && File.Exists(Path.Combine(removed, "main.py")) && !Directory.Exists(mirroredPlugin) && Directory.Exists(plugin), "Pending plugin removal is recoverably archived without deleting current plugin yet");
            File.Delete(Path.Combine(plugin, "main.py")); File.Delete(Path.Combine(plugin, "secrets.json")); File.Delete(Path.Combine(plugin, "client.key")); Directory.Delete(plugin);
            storage.SyncPendingResources(); Require(!Directory.Exists(mirroredPlugin), "Successful plugin uninstall cannot be resurrected by later mirroring");
            Throws(() => storage.ConfigureDataDirectory(Path.Combine(storage.Root, "nested")), "Nested roots reject recursive copying");
            Throws(() => storage.ConfigureDataDirectory(storage.Root), "Identical roots are rejected");
            Throws(() => storage.SetBackupFolder(storage.Root), "Backup cannot contain or equal data root");
            Throws(() => storage.SetBackupFolder(Path.Combine(storage.ArtworkFolder, "backup")), "Backup cannot mix into managed artwork");
            var foreignBackup = Path.Combine(fixture, "foreign-backup"); Directory.CreateDirectory(foreignBackup); File.WriteAllText(Path.Combine(foreignBackup, "state.json.bak"), "do not replace");
            Throws(() => storage.SetBackupFolder(foreignBackup), "Existing unrelated backup directory is refused"); Require(File.ReadAllText(Path.Combine(foreignBackup, "state.json.bak")) == "do not replace", "Refused backup directory remains untouched");
            var occupied = Path.Combine(fixture, "occupied"); Directory.CreateDirectory(occupied); File.WriteAllText(Path.Combine(occupied, "keep.txt"), "keep");
            Throws(() => storage.ConfigureDataDirectory(occupied), "Non-empty targets are rejected"); Require(File.ReadAllText(Path.Combine(occupied, "keep.txt")) == "keep", "Rejected migration preserves target contents");
            var settings = new AppSettings { KeyBindings = new() { ["favorite"] = "Space" } }; settings.Validate();
            Require(settings.KeyBindings.Count == 0 && settings.ValidationWarning is not null, "Invalid conflicting shortcut state recovers to defaults without startup crash");
            Environment.SetEnvironmentVariable("LMP_DATA_DIR", Path.Combine(fixture, "environment-root"));
            var overrideStorage = new AppStorage(installationDirectory: Path.Combine(fixture, "unused-install"));
            Require(overrideStorage.Root.EndsWith("environment-root") && !File.Exists(overrideStorage.Paths.BootstrapPath), "Explicit environment test root does not mutate installation bootstrap");
        }
        finally { Environment.SetEnvironmentVariable("LMP_DATA_DIR", environment); }
    }
    private static void CheckStatistics(string fixture)
    {
        var storage = new AppStorage(Path.Combine(fixture, "listening")); var first = Track("first"); first.Artist = "甲 / 乙"; var second = Track("second"); second.Artist = "AC/DC";
        storage.Save(new AppState { Tracks = [first, second] });
        var clock = new ManualClock(); var audio = new StatisticsAudio();
        using (var vm = new MainViewModel(new MusicLibraryScanner(storage), audio, clock))
        {
            first = vm.State.Tracks[0]; second = vm.State.Tracks[1]; Wait(vm.PlayTrackAsync(first));
            Sample(vm, audio, clock, .5); Require(vm.GetStatistics().TotalPlays == 0, "A sub-second start does not count as a play");
            Sample(vm, audio, clock, .5); var stats = vm.GetStatistics(); Require(Near(stats.TotalSeconds, 1) && stats.TotalPlays == 1 && stats.ActiveDays == 1 && Near(stats.TodaySeconds, 1), "Real audio advance credits actual seconds and one session play");
            vm.TogglePlayPauseCommand.Execute(null); clock.Advance(1); Tick(vm); Require(Near(vm.GetStatistics().TotalSeconds, 1), "Paused wall time never credits listening");
            vm.TogglePlayPauseCommand.Execute(null); Sample(vm, audio, clock, .5); Require(vm.GetStatistics().TotalPlays == 1, "Pause and resume do not duplicate play count");
            var beforeSeek = vm.GetStatistics().TotalSeconds; vm.Seek(200); Tick(vm); Require(Near(vm.GetStatistics().TotalSeconds, beforeSeek), "Seek jump itself contributes no listening time");
            Sample(vm, audio, clock, .5); Require(Near(vm.GetStatistics().TotalSeconds, beforeSeek + .5), "Listening after a seek resumes with fresh anchor");
            vm.IsSeeking = true; Sample(vm, audio, clock, .5); vm.IsSeeking = false; Require(Near(vm.GetStatistics().TotalSeconds, beforeSeek + .5), "Seek drag excludes sampling interval");
            vm.ApplySettings(); Sample(vm, audio, clock, 1); Require(Near(vm.GetStatistics().TotalSeconds, beforeSeek + 1.5), "Normal playback credits real seconds");
            var beforeSleep = vm.GetStatistics().TotalSeconds; Sample(vm, audio, clock, 30); Require(Near(vm.GetStatistics().TotalSeconds, beforeSleep), "Long suspended or delayed timer interval is conservatively excluded");
            Wait(vm.PlayTrackAsync(second)); Sample(vm, audio, clock, 1); stats = vm.GetStatistics();
            Require(stats.TotalPlays == 2 && stats.Songs.Count == 2 && stats.Artists.Any(artist => artist.Name == "AC/DC") && stats.Artists.All(artist => artist.Name is not ("AC" or "DC")), "Song and collaborator rankings preserve slash-containing artist names");
            Require(stats.Artists.Any(artist => artist.Name == "甲") && stats.Artists.Any(artist => artist.Name == "乙"), "Tagged multiple performers receive separate ranking entries");
            var image = Path.Combine(fixture, "personal.png"); using (var bitmap = new WriteableBitmap(new Avalonia.PixelSize(8, 20), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul)) bitmap.Save(image, PngBitmapEncoderOptions.Default);
            vm.SetGroupCover("artists", "AC/DC", image); var owned = vm.State.GroupCovers["artist:AC/DC"]; Require(owned.StartsWith(storage.ArtworkFolder) && owned.EndsWith(".png") && vm.GetGroupCover("artist", "AC/DC") is not null, "Artist cover saved as an owned, cached PNG");
            vm.SetGroupCover("album", "专辑", image); File.Delete(image); Require(vm.GetGroupCover("artists", "AC/DC") is not null, "Removing external source does not break owned group artwork");
            vm.SetGroupCover("artist", "AC/DC", null); Require(vm.GetGroupCover("artist", "AC/DC") is null, "Null restores default group cover");
            vm.Seek(25); vm.Save(); var backup = Path.Combine(fixture, "explicit-restore.json"); AppStorage.AtomicWrite(backup, JsonSerializer.Serialize(storage.Load(), AppStorage.Json));
            var selectedJson = File.ReadAllText(backup);
            var unrelated = Path.Combine(fixture, "not-a-library.json"); File.WriteAllText(unrelated, "{\"schemaVersion\":1,\"theme\":\"Dark\"}");
            Throws(() => vm.RestoreLibraryBackup(unrelated), "Unrelated JSON is rejected before replacing a library"); Require(vm.GetStatistics().TotalPlays == 2 && !Directory.Exists(Path.Combine(storage.BackupFolder, "Restores")), "Rejected restore does not mutate data or create a restoration archive");
            var savedTotal = vm.GetStatistics().TotalSeconds; vm.State.ListeningEntries.Clear(); vm.Save(); vm.RestoreLibraryBackup(backup);
            Require(Near(vm.GetStatistics().TotalSeconds, savedTotal) && !vm.IsPlaying && vm.PlaybackPosition == 25 && vm.CurrentTrack?.Id == "second", "Explicit backup restores statistics and displayed progress without autoplay");
            Require(File.ReadAllText(backup) == selectedJson, "Explicit backup import preserves original file bytes");
            var restoreArchives = Directory.GetDirectories(Path.Combine(storage.BackupFolder, "Restores"));
            Require(restoreArchives.Length == 1 && File.Exists(Path.Combine(restoreArchives[0], "current.state.json")) && File.ReadAllText(Path.Combine(restoreArchives[0], "selected.state.json")) == selectedJson, "Restoration independently archives current and exact selected JSON");
            var legacyDirectory = Path.Combine(fixture, "old-library"); Directory.CreateDirectory(legacyDirectory);
            new LyricsService(Path.Combine(legacyDirectory, "Lyrics")).Save(first.Id, "[00:01.00]旧歌词");
            var legacy = new AppState { Tracks = vm.State.Tracks, ListeningEntries = vm.State.ListeningEntries, GroupCovers = vm.State.GroupCovers, LastTrackId = "second", LastPosition = 25 };
            var legacyFile = Path.Combine(legacyDirectory, "state.json"); AppStorage.AtomicWrite(legacyFile, JsonSerializer.Serialize(legacy, AppStorage.Json));
            vm.RestoreLibraryBackup(legacyFile); Require(vm.Lyrics.Read(first.Id).Contains("旧歌词") && File.Exists(legacyFile), "Old backup without ManagedDataRoot discovers sibling Lyrics via explicit selected source");
            var rollingBackup = JsonSerializer.Serialize(new LibraryDatabase(storage.DatabaseBackupPath).Read(), AppStorage.Json); vm.RestoreLibraryBackup();
            Require(Directory.GetDirectories(Path.Combine(storage.BackupFolder, "Restores")).Length == 3 && Directory.EnumerateFiles(Path.Combine(storage.BackupFolder, "Restores"), "selected.state.json", SearchOption.AllDirectories).Any(file => File.ReadAllText(file) == rollingBackup), "Rolling backup restoration preserves selected bytes in uniquely named archive");
            // 滚动数据库快照采用五分钟周期；继续迁移测试前恢复明确选定的完整样本。
            vm.RestoreLibraryBackup(backup);
            var eventOnUi = false; var guarded = false; vm.SettingsChanged += (_, _) => eventOnUi = Dispatcher.UIThread.CheckAccess();
            var expectedPlays = vm.State.ListeningEntries.Sum(entry => entry.PlayCount);
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(vm.IsMigratingData) || !vm.IsMigratingData) return;
                guarded = storage.IsMigrating && MigrationBlocked(() => vm.SetBackupFolder(Path.Combine(fixture, "blocked-backup"))) && MigrationBlocked(() => vm.RestoreLibraryBackup()) && MigrationBlocked(() => vm.ConfigureDataDirectory(Path.Combine(fixture, "blocked-root")));
            };
            Wait(vm.ConfigureDataDirectoryAsync(Path.Combine(fixture, "async-migration")));
            Require(!vm.IsBusy && !vm.IsMigratingData && !storage.IsMigrating && guarded && eventOnUi && storage.PendingRoot is not null && storage.ReadBackup(Path.Combine(storage.PendingRoot, "library.db")).ListeningEntries.Sum(entry => entry.PlayCount) == expectedPlays, "Async migration guards root mutations then commits latest state and events on UI thread");
            Throws(() => Wait(vm.ConfigureDataDirectoryAsync(Path.Combine(storage.Root, "invalid-nested"))), "Async migration validates overlap"); Require(!vm.IsBusy, "Failed async migration clears busy state");
        }
        using (var restored = new MainViewModel(new MusicLibraryScanner(storage), new StatisticsAudio(), clock))
            Require(restored.GetStatistics().TotalPlays == 2 && restored.GetGroupCover("albums", "专辑") is not null && !restored.IsPlaying, "Source-generated persistence retains statistics and group covers");
    }
    private static void CheckMovedTrackImport(string fixture)
    {
        var source = new AppStorage(Path.Combine(fixture, "import-before-move"));
        var music = Path.Combine(source.Root, "Music"); Directory.CreateDirectory(music);
        var file = Path.Combine(music, "portable.wav");
        using (var writer = new BinaryWriter(File.Create(file)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 2000);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(1000); writer.Write(2000);
            writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(2000); writer.Write(new byte[2000]);
        }
        File.WriteAllText(Path.ChangeExtension(file, ".lrc"), "[00:00.10]portable lyric");
        var original = new MusicLibraryScanner(source).ReadTrack(file); original.IsFavorite = true;
        source.Save(new AppState { Tracks = [original], Playlists = [new Playlist { Id = "kept", Name = "保留关联", TrackIds = [original.Id] }] });
        var next = Path.Combine(fixture, "import-after-move"); source.ConfigureDataDirectory(next);
        var migrated = new AppStorage(next);
        using var vm = new MainViewModel(new MusicLibraryScanner(migrated), new StatisticsAudio());
        var movedFile = Path.Combine(next, "Music", "portable.wav");
        Wait(vm.ImportAsync([movedFile]));
        Require(vm.State.Tracks.Count == 1 && vm.State.Tracks[0].Id == original.Id && vm.State.Tracks[0].IsFavorite
            && vm.State.Playlists.Single(p => p.Id == "kept").TrackIds.SequenceEqual([original.Id]) && vm.Lyrics.Read(original.Id).Contains("portable lyric"), "Reimport of moved local music retains identity, favorite, playlist and existing lyrics without duplicates");
        var savedLyric = vm.Lyrics.PathFor(original.Id); File.Move(savedLyric, savedLyric + ".test-preserved");
        Wait(vm.ImportAsync([movedFile]));
        Require(vm.State.Tracks.Count == 1 && vm.Lyrics.Read(original.Id).Contains("portable lyric"), "Fresh adjacent lyric discovery is attached to retained migrated identity");
    }
    private static void CheckCustomLyricsDirectory(string fixture)
    {
        var storage = new AppStorage(Path.Combine(fixture, "lyrics-settings-source"));
        var custom = Path.Combine(fixture, "user-selected-lyrics"); var track = Track("custom-folder");
        storage.Save(new AppState { Tracks = [track], Settings = new() { LyricsFolder = custom } });
        using (var vm = new MainViewModel(new MusicLibraryScanner(storage), new StatisticsAudio()))
        {
            Require(vm.Lyrics.Folder == custom && vm.Settings.LyricsFolder == custom, "Startup preserves explicitly selected lyrics directory");
            vm.Lyrics.Save(track.Id, "[00:01.00]custom folder lyric"); vm.ApplySettings();
            Require(vm.Lyrics.Folder == custom && vm.Settings.LyricsFolder == custom, "Unrelated settings do not reset lyrics folder");
            var next = Path.Combine(fixture, "lyrics-settings-migrated"); vm.ConfigureDataDirectory(next);
            vm.Lyrics.Save(track.Id, "[00:01.00]latest external lyric");
            Require(!File.Exists(new LyricsService(Path.Combine(next, "Lyrics")).PathFor(track.Id)), "External custom lyrics directory is not silently duplicated into the migrated default");
            using var moved = new MainViewModel(new MusicLibraryScanner(new AppStorage(next)), new StatisticsAudio());
            Require(moved.Lyrics.Folder == custom && moved.Lyrics.Read(track.Id).Contains("latest external lyric"), "Restart after data migration continues to use custom external lyrics folder");
        }
        using var restarted = new MainViewModel(new MusicLibraryScanner(storage), new StatisticsAudio());
        Require(restarted.Lyrics.Folder == custom && restarted.Lyrics.Read(track.Id).Contains("latest external lyric"), "Custom lyrics setting and content survive restart");
        var selected = storage.DatabasePath; restarted.RestoreLibraryBackup(selected);
        Require(restarted.Lyrics.Folder == storage.DefaultLyricsFolder && restarted.Lyrics.Read(track.Id).Contains("latest external lyric")
            && File.Exists(new LyricsService(custom).PathFor(track.Id)), "Explicit restore imports custom source lyrics into managed default and preserves external original");
    }
    private static TrackItem Track(string id) => new(id, "歌曲-" + id, "艺人", "专辑", "virtual-" + id + ".wav", ".wav", 20) { DurationSeconds = 1000 };
    private static bool Near(double a, double b) => Math.Abs(a - b) < .001;
    private static void Sample(MainViewModel vm, StatisticsAudio audio, ManualClock clock, double seconds) { clock.Advance(seconds); audio.Advance(seconds); Tick(vm); }
    private static void Tick(MainViewModel vm) => typeof(MainViewModel).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [null, EventArgs.Empty]);
    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } task.GetAwaiter().GetResult(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws(Action action, string message) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException(message); }
    private static bool MigrationBlocked(Action action) { try { action(); } catch (InvalidOperationException exception) when (exception.Message.Contains("迁移")) { return true; } return false; }
    private sealed class ManualClock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(_milliseconds);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(double seconds) => _milliseconds += (long)(seconds * 1000);
    }
    private sealed class StatisticsAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(1000); public TimeSpan Position { get; set; }
        public float Volume { get; set; } public float Speed { get; set; } = 1;
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Advance(double seconds) { if (IsPlaying) Position += TimeSpan.FromSeconds(seconds * Speed); }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; } public void Dispose() => Stop();
    }
}
