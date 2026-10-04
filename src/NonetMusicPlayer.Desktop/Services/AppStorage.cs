using System.Text.Json;
using System.Text.Json.Serialization;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

public sealed class AppStorage
{
    public string Root { get; }
    public AppPaths Paths { get; }
    public string ArtworkFolder => Path.Combine(Root, "Artwork");
    public string PluginsFolder => Path.Combine(Root, "Plugins");
    public string DefaultLyricsFolder => Path.Combine(Root, "Lyrics");
    public string LogsFolder => Path.Combine(Root, "Logs");
    public string PluginTempFolder => Path.Combine(Root, "Temp", "Plugins");
    public string BackupFolder { get; private set; }
    public string BackupPath => Path.Combine(BackupFolder, "state.json.bak");
    public string DatabasePath => Path.Combine(Root, "library.db");
    public string DatabaseBackupPath => Path.Combine(BackupFolder, "library.db.bak");
    private LibraryDatabase? _database, _pendingDatabase;
    private DateTimeOffset _databaseBackupAt = DateTimeOffset.MinValue;
    private bool _databaseHealthy = true;
    public string? PendingRoot { get; private set; }
    private string? _pendingBackup;
    private int _migrationActive;
    public bool IsMigrating => Volatile.Read(ref _migrationActive) != 0;
    internal void SetMigrationActive(bool value) => Interlocked.Exchange(ref _migrationActive, value ? 1 : 0);
    public string? RecoveryMessage { get; private set; }
    private bool _primaryHealthy = true;
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, TypeInfoResolver = AppJsonContext.Default };
    public static JsonSerializerOptions RpcJson { get; } = new(Json) { WriteIndented = false };
    public static string ResolveRoot() => new AppPaths().DataDirectory;
    public AppStorage(string? root = null, string? installationDirectory = null)
    {
        Paths = new(root, installationDirectory); Root = Paths.DataDirectory; BackupFolder = Paths.BackupDirectory; RecoveryMessage = Paths.RecoveryMessage;
        DataDirectoryService.ValidateBackup(Root, BackupFolder);
        foreach (var folder in new[] { Root, ArtworkFolder, PluginsFolder, DefaultLyricsFolder, LogsFolder, PluginTempFolder, BackupFolder }) Directory.CreateDirectory(folder);
        Paths.EnsureBootstrap();
    }
    public AppState Load()
    {
        foreach (var path in new[] { DatabasePath, DatabaseBackupPath, Path.Combine(Root, "state.json"), BackupPath, Path.Combine(Root, "state.json.bak") }.Distinct())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var state = ReadBackup(path);
                if (!string.IsNullOrWhiteSpace(state.ManagedDataRoot) && !string.Equals(state.ManagedDataRoot, Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    var oldRoot = state.ManagedDataRoot; RemapState(state, oldRoot, Root);
                    state.Settings.BackupFolder = DataDirectoryService.Remap(state.Settings.BackupFolder, oldRoot, Root);
                }
                state.ManagedDataRoot = Root;
                if (!string.IsNullOrWhiteSpace(state.Settings.BackupFolder))
                {
                    try { var value = state.Settings.BackupFolder; var backup = DataDirectoryService.Normalize(Path.IsPathRooted(value) ? value : Path.Combine(Root, value)); DataDirectoryService.ValidateBackup(Root, backup); Directory.CreateDirectory(backup); BackupFolder = backup; }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { RecoveryMessage = "自定义备份目录无法访问，暂使用引导文件指定的备份目录。"; }
                }
                if (path.EndsWith("bak")) RecoveryMessage = "数据文件损坏，已从上次备份恢复。";
                return state;
            }
            catch (Exception e) when (e is JsonException or IOException or InvalidDataException or ArgumentException or NotSupportedException or Microsoft.Data.Sqlite.SqliteException)
            {
                if (path == Path.Combine(Root, "state.json") || path == DatabasePath)
                {
                    _primaryHealthy = false;
                    if (path == DatabasePath) _databaseHealthy = false;
                    try { File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false); } catch (IOException) { }
                }
                RecoveryMessage = "数据文件无法读取，原文件已保留；请在设置中检查数据目录。";
            }
        }
        var empty = new AppState(); MigrateState(empty); return empty;
    }
    public AppState ReadBackup(string? path = null)
        => ParseBackup(ReadBackupText(path));
    internal string ReadBackupText(string? path = null)
    {
        path ??= File.Exists(DatabaseBackupPath) ? DatabaseBackupPath : BackupPath;
        if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new InvalidDataException("资料库备份超过 256 MB 限制。");
        using (var file = File.OpenRead(path))
        {
            var signature = new byte[16]; var length = file.Read(signature);
            if (length == 16 && System.Text.Encoding.ASCII.GetString(signature) == "SQLite format 3\0")
            {
                var database = new LibraryDatabase(path); var state = database.Read();
                if (path == DatabasePath) _database = database;
                return JsonSerializer.Serialize(state, Json);
            }
        }
        return File.ReadAllText(path);
    }
    internal AppState ParseBackup(string json)
    {
        AppState state;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.EnumerateObject().Any(property => property.Name.Equals("tracks", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("playlists", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("所选文件不是资料库备份：应包含 tracks 或 playlists。请选择播放器的 state.json 或 .bak。");
            state = JsonSerializer.Deserialize<AppState>(json, Json) ?? throw new InvalidDataException("资料库备份内容为空。");
        }
        catch (JsonException error) { throw new InvalidDataException("资料库备份 JSON 格式或字段类型无效；原文件保留，请选择有效的 state.json 或 .bak。", error); }
        if (state.SchemaVersion != 1) throw new InvalidDataException("资料库备份版本不受支持；请使用 schemaVersion 为 1 的备份。");
        state.Tracks ??= []; state.Playlists ??= []; state.MusicFolders ??= []; state.History ??= []; state.Settings ??= new();
        state.Tracks = state.Tracks.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id)).DistinctBy(t => t.Id).ToList();
        state.Playlists = state.Playlists.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id)).DistinctBy(p => p.Id).ToList();
        foreach (var playlist in state.Playlists) playlist.TrackIds ??= [];
        state.Settings.Validate(); MigrateState(state); return state;
    }
    public string ArchiveRestore(AppState current, string selectedJson)
    {
        // 独立命名的不可变快照不受滚动 state.json.bak 替换影响。
        ParseBackup(selectedJson); var currentJson = JsonSerializer.Serialize(current, Json);
        var name = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(BackupFolder, "Restores", name);
        AtomicWrite(Path.Combine(folder, "current.state.json"), currentJson); AtomicWrite(Path.Combine(folder, "selected.state.json"), selectedJson);
        if (_pendingBackup is { } pending && !string.Equals(pending, BackupFolder, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            var mirrored = Path.Combine(pending, "Restores", name);
            AtomicWrite(Path.Combine(mirrored, "current.state.json"), currentJson); AtomicWrite(Path.Combine(mirrored, "selected.state.json"), selectedJson);
        }
        return folder;
    }
    public static void MigrateState(AppState state)
    {
        state.RecentTemporaryTracks ??= [];
        var historyIds = state.History.ToHashSet(StringComparer.Ordinal);
        state.RecentTemporaryTracks = state.RecentTemporaryTracks.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Id) && historyIds.Contains(t.Id)).DistinctBy(t => t.Id).Take(state.Settings.HistoryLimit).ToList();
        state.ListeningEntries ??= []; state.GroupCovers ??= new(StringComparer.Ordinal); state.SoftwareDefaultGroupCovers ??= new(StringComparer.Ordinal);
        state.ListeningEntries = state.ListeningEntries.Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.TrackId) && DateOnly.TryParse(entry.Date, out _) && double.IsFinite(entry.Seconds) && entry.Seconds >= 0).ToList();
        foreach (var entry in state.ListeningEntries) { entry.PlayCount = Math.Max(0, entry.PlayCount); entry.Title ??= "未命名歌曲"; entry.Artist ??= L10n.T("Library.UnknownArtist"); }
        foreach (var track in state.Tracks.Concat(state.RecentTemporaryTracks))
        {
            track.Title ??= "未命名歌曲"; track.Artist ??= L10n.T("Library.UnknownArtist"); track.Album ??= L10n.T("Library.UnknownAlbum"); track.FilePath ??= ""; track.Extension ??= "";
            if (!double.IsFinite(track.DurationSeconds) || track.DurationSeconds < 0 || track.DurationSeconds > TimeSpan.MaxValue.TotalSeconds) track.DurationSeconds = 0;
        }
        var liked = state.Playlists.FirstOrDefault(p => p.Id == Playlist.LikedId);
        if (liked is null) { liked = new Playlist { Id = Playlist.LikedId, Name = L10n.T("Playlists.LikedSongs") }; state.Playlists.Insert(0, liked); }
        liked.Name = L10n.T("Playlists.LikedSongs");
        liked.TrackIds = (liked.TrackIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var track in state.Tracks.Where(t => t.IsFavorite)) if (!liked.TrackIds.Contains(track.Id)) liked.TrackIds.Add(track.Id);
        var favoriteIds = liked.TrackIds.ToHashSet(StringComparer.Ordinal);
        foreach (var track in state.Tracks) track.IsFavorite = favoriteIds.Contains(track.Id);
        foreach (var playlist in state.Playlists)
        {
            playlist.TrackIds = (playlist.TrackIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            playlist.Description ??= "";
            if (playlist.Name is null) playlist.Name = "未命名歌单";
        }
        if (!double.IsFinite(state.LastPosition) || state.LastPosition < 0 || state.LastPosition > TimeSpan.MaxValue.TotalSeconds) state.LastPosition = 0;
        if (state.LastSourcePage == "favorites") state.LastSourcePage = "playlist:" + Playlist.LikedId;
        if (!string.IsNullOrWhiteSpace(state.LastPlaylistId)) state.LastSourcePage = "playlist:" + state.LastPlaylistId;
        if (string.IsNullOrWhiteSpace(state.LastSourcePage)) state.LastSourcePage = "library";
        state.LastPlaylistId = state.LastSourcePage.StartsWith("playlist:", StringComparison.Ordinal) ? state.LastSourcePage[9..] : null;
    }
    public void Save(AppState state, bool playbackOnly = false)
    {
        state.ManagedDataRoot = Root;
        state.Settings.BackupFolder = DataDirectoryService.Contains(Root, BackupFolder) ? Path.GetRelativePath(Root, BackupFolder) : BackupFolder;
        // SQLite 事务只修改变化的行；旧 JSON 保留为迁移来源，不再在每次进度保存时重写。
        if (!_databaseHealthy)
        {
            // 损坏的主库及其侧文件只归档，不覆盖或删除；恢复数据在新数据库中事务写入。
            var archive = DatabasePath + ".corrupt-" + Guid.NewGuid().ToString("N");
            foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(DatabasePath + suffix)) File.Move(DatabasePath + suffix, archive + suffix);
            _database = null; _databaseHealthy = true;
        }
        _database ??= new LibraryDatabase(DatabasePath);
        var backup = _primaryHealthy && File.Exists(DatabasePath) && DateTimeOffset.UtcNow - _databaseBackupAt >= TimeSpan.FromMinutes(5);
        if (backup) { _database.Backup(DatabaseBackupPath); _databaseBackupAt = DateTimeOffset.UtcNow; }
        _database.Save(state, playbackOnly); _primaryHealthy = true;
        if (!File.Exists(DatabaseBackupPath)) { _database.Backup(DatabaseBackupPath); _databaseBackupAt = DateTimeOffset.UtcNow; }
        if (PendingRoot is { } pending)
        {
            DataDirectoryService.RejectLinkedAncestors(pending);
            SyncManagedResources(pending);
            var copy = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, Json), Json)!;
            RemapState(copy, Root, pending); copy.ManagedDataRoot = pending;
            var pendingBackup = _pendingBackup ?? Path.Combine(pending, "Backups");
            copy.Settings.BackupFolder = DataDirectoryService.Contains(pending, pendingBackup) ? Path.GetRelativePath(pending, pendingBackup) : pendingBackup;
            Directory.CreateDirectory(pendingBackup);
            _pendingDatabase ??= new LibraryDatabase(Path.Combine(pending, "library.db"));
            _pendingDatabase.Save(copy, playbackOnly); _pendingDatabase.Backup(Path.Combine(pendingBackup, "library.db.bak"));
        }
    }
    public void SetBackupFolder(string folder)
    {
        var candidate = DataDirectoryService.Normalize(folder); DataDirectoryService.ValidateBackup(Root, candidate);
        if (PendingRoot is { } pending) DataDirectoryService.ValidateBackup(pending, candidate);
        RejectForeignBackup(candidate);
        Directory.CreateDirectory(candidate);
        if (File.Exists(DatabasePath) && !File.Exists(Path.Combine(candidate, "library.db.bak"))) (_database ??= new LibraryDatabase(DatabasePath)).Backup(Path.Combine(candidate, "library.db.bak"));
        if (File.Exists(BackupPath) && Path.Combine(candidate, "state.json.bak") != BackupPath && !File.Exists(Path.Combine(candidate, "state.json.bak"))) File.Copy(BackupPath, Path.Combine(candidate, "state.json.bak"), false);
        Paths.SaveConfiguration(PendingRoot ?? Root, candidate);
        if (PendingRoot is not null) _pendingBackup = candidate;
        BackupFolder = candidate;
    }
    public DataDirectoryChangeResult ConfigureDataDirectory(string newRoot, string? backupFolder = null, bool copyExisting = true)
    {
        var ownsMigrationFlag = !IsMigrating; if (ownsMigrationFlag) SetMigrationActive(true);
        try
        {
        var candidate = DataDirectoryService.Normalize(newRoot);
        var backup = string.IsNullOrWhiteSpace(backupFolder) ? Path.Combine(candidate, "Backups") : DataDirectoryService.Normalize(backupFolder);
        DataDirectoryService.ValidateBackup(candidate, backup);
        RejectForeignBackup(backup);
        if (DataDirectoryService.Contains(Root, candidate) || DataDirectoryService.Contains(candidate, Root)) throw new InvalidDataException("新旧数据目录不能相同或相互包含。");
        if (copyExisting) DataDirectoryService.CopyNonDestructive(Root, candidate, skipRuntimeFiles: true);
        else { if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any()) throw new InvalidDataException("为保护现有数据，请选择空目录。"); Directory.CreateDirectory(candidate); }
        Directory.CreateDirectory(backup);
        Paths.SaveConfiguration(candidate, backup);
        _pendingDatabase = null; _pendingBackup = backup; PendingRoot = candidate;
        return new(Root, candidate, backup, true);
        }
        finally { if (ownsMigrationFlag) SetMigrationActive(false); }
    }
    private void RejectForeignBackup(string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(folder, BackupFolder, comparison) && !string.Equals(folder, _pendingBackup, comparison) && new[] { "state.json.bak", "library.db.bak" }.Any(name => File.Exists(Path.Combine(folder, name))))
            throw new InvalidDataException("所选目录已有资料库备份。为保护现有备份，请选择新的目录；如需读取该备份，请使用“从文件恢复”。");
    }
    public void SyncPendingResources()
    {
        if (PendingRoot is not { } pending) return;
        DataDirectoryService.RejectLinkedAncestors(pending); SyncManagedResources(pending);
    }
    public string? ArchivePendingPluginRemoval(string id)
    {
        if (IsMigrating) throw new InvalidOperationException("正在迁移数据目录，请等待完成后再卸载插件。");
        ArgumentNullException.ThrowIfNull(id);
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9.-]{2,80}$")) throw new InvalidDataException("插件标识符无效。");
        if (PendingRoot is not { } pending) return null;
        var plugins = Path.GetFullPath(Path.Combine(pending, "Plugins")); var source = Path.GetFullPath(Path.Combine(plugins, id));
        if (!DataDirectoryService.Contains(plugins, source)) throw new InvalidDataException("插件路径必须位于待启用的 Plugins 目录中。");
        if (!Directory.Exists(source)) return null;
        DataDirectoryService.RejectLinkedAncestors(source);
        var backup = _pendingBackup ?? BackupFolder; DataDirectoryService.ValidateBackup(pending, backup);
        var target = Path.Combine(backup, "RemovedPlugins", id + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
        if (!DataDirectoryService.Contains(backup, target)) throw new InvalidDataException("插件恢复归档必须位于备份目录内。");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try { Directory.Move(source, target); }
        catch (IOException) when (Directory.Exists(source) && !Directory.Exists(target))
        {
            // 跨卷迁移先完整复制，再移除已验证归属的待启用插件目录。
            DataDirectoryService.CopyNonDestructive(source, target);
            DataDirectoryService.RejectLinkedAncestors(source); Directory.Delete(source, true);
        }
        return target;
    }
    private void SyncManagedResources(string newRoot)
    {
        foreach (var category in new[] { "Artwork", "Lyrics", "Layouts", "Plugins", "Fonts" })
        {
            var source = Path.Combine(Root, category); if (!Directory.Exists(source)) continue;
            var folders = new Stack<string>(); folders.Push(source);
            while (folders.TryPop(out var folder))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (Path.GetExtension(entry).Equals(".tmp", StringComparison.OrdinalIgnoreCase) || !File.Exists(entry) && !Directory.Exists(entry)) continue;
                    if (category == "Plugins" && DataDirectoryService.IsPrivatePluginEntry(entry)) continue;
                    var attributes = File.GetAttributes(entry); if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) { folders.Push(entry); continue; }
                    var target = DataDirectoryService.Remap(entry, Root, newRoot); DataDirectoryService.RejectLinkedAncestors(Path.GetDirectoryName(target)!); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    DataDirectoryService.RejectLinkedAncestors(Path.GetDirectoryName(target)!);
                    if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("待启用目录中出现文件链接，已停止资源同步；原目录中的数据保留。");
                    if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(entry).Length || File.GetLastWriteTimeUtc(target) != File.GetLastWriteTimeUtc(entry)) File.Copy(entry, target, true);
                }
            }
        }
    }
    private static void RemapState(AppState state, string oldRoot, string newRoot)
    {
        if (state.Settings.BackgroundImagePath is { } background) state.Settings.BackgroundImagePath = DataDirectoryService.Remap(background, oldRoot, newRoot);
        foreach (var track in state.Tracks.Concat(state.RecentTemporaryTracks)) { track.FilePath = DataDirectoryService.Remap(track.FilePath, oldRoot, newRoot); if (track.CoverPath is not null) track.CoverPath = DataDirectoryService.Remap(track.CoverPath, oldRoot, newRoot); }
        foreach (var playlist in state.Playlists) if (playlist.CoverPath is not null) playlist.CoverPath = DataDirectoryService.Remap(playlist.CoverPath, oldRoot, newRoot);
        foreach (var key in state.GroupCovers.Keys.ToArray()) state.GroupCovers[key] = DataDirectoryService.Remap(state.GroupCovers[key], oldRoot, newRoot);
        state.MusicFolders = state.MusicFolders.Select(path => DataDirectoryService.Remap(path, oldRoot, newRoot)).ToList();
        if (!string.IsNullOrWhiteSpace(state.Settings.LyricsFolder)) state.Settings.LyricsFolder = DataDirectoryService.Remap(state.Settings.LyricsFolder, oldRoot, newRoot);
    }
    public static void AtomicWrite(string path, string text, bool backup = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            if (backup && File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ListeningStatisticsSnapshot))]
[JsonSerializable(typeof(List<PluginManifest>))]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(ProviderCatalog))]
[JsonSerializable(typeof(PlaybackSource))]
[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
internal partial class AppJsonContext : JsonSerializerContext { }
