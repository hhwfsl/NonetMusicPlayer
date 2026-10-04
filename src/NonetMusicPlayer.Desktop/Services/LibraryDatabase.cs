using System.Text.Json;
using NonetMusicPlayer.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>SQLite 事务型实体存储。歌曲、歌单和统计各自独立成行，仅写入实际变更的实体。</summary>
public sealed class LibraryDatabase(string path)
{
    private const int ApplicationId = 0x4C4D5031;
    private readonly Dictionary<(string Kind, string Id), (int Order, object Value)> _known = [];
    private bool _initialized;
    public int LastWrittenRows { get; private set; }
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    private sealed record Document(string Kind, string Id, int Order, object Fingerprint, Func<string> Serialize);
    private static IEnumerable<Document> Documents(AppState state, bool playbackOnly = false)
    {
        var metadata = new AppState { ManagedDataRoot = state.ManagedDataRoot, MusicFolders = state.MusicFolders, History = state.History, RecentTemporaryTracks = state.RecentTemporaryTracks,
            Settings = state.Settings, LastTrackId = state.LastTrackId, LastPosition = state.LastPosition, LastSourcePage = state.LastSourcePage, LastPlaylistId = state.LastPlaylistId,
            LastTemporaryFile = state.LastTemporaryFile, LastTemporaryCounted = state.LastTemporaryCounted, GroupCovers = state.GroupCovers, SoftwareDefaultGroupCovers = state.SoftwareDefaultGroupCovers };
        var root = JsonSerializer.Serialize(metadata, AppStorage.Json);
        yield return new("state", "root", 0, root, () => root);
        for (var i = 0; !playbackOnly && i < state.Tracks.Count; i++)
        {
            var t = state.Tracks[i];
            var value = (t.Title, t.Artist, t.Album, t.FilePath, t.Extension, t.FileSize, t.DurationSeconds, t.CoverPath, t.IsFavorite, t.ProviderId, t.ProviderTrackId, t.LyricsSourcePath);
            yield return new("track", t.Id, i, value, () => JsonSerializer.Serialize(t, AppStorage.Json));
        }
        for (var i = 0; !playbackOnly && i < state.Playlists.Count; i++)
        {
            var p = state.Playlists[i]; yield return new("playlist", p.Id, i, (p.Name, p.Description, p.CoverPath, string.Join('\0', p.TrackIds)), () => JsonSerializer.Serialize(p, AppStorage.Json));
        }
        for (var i = 0; i < state.ListeningEntries.Count; i++)
        {
            var e = state.ListeningEntries[i]; yield return new("listening", e.Date + ":" + e.TrackId, i, (e.Title, e.Artist, e.Seconds, e.PlayCount), () => JsonSerializer.Serialize(e, AppStorage.Json));
        }
    }
    private SqliteConnection Open(bool readOnly)
    {
        DataDirectoryService.RejectLinkedAncestors(Path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA busy_timeout=3000;"; command.ExecuteNonQuery();
        return connection;
    }
    public AppState Read()
    {
        if (new FileInfo(Path).Length > 256L * 1024 * 1024) throw new InvalidDataException("资料库数据库超过 256 MB 限制。");
        using var connection = Open(true); using var check = connection.CreateCommand(); check.CommandText = "PRAGMA application_id;";
        if (Convert.ToInt32(check.ExecuteScalar()) != ApplicationId) throw new InvalidDataException("文件不是 NonetMusicPlayer 资料库。");
        using var query = connection.CreateCommand(); query.CommandText = "SELECT kind,payload FROM entities ORDER BY kind,sort_order;";
        using var reader = query.ExecuteReader(); AppState? state = null; var tracks = new List<TrackItem>(); var playlists = new List<Playlist>(); var listening = new List<ListeningEntry>(); var count = 0;
        while (reader.Read())
        {
            if (++count > 500_000) throw new InvalidDataException("资料库实体过多。");
            var text = reader.GetString(1); if (text.Length > 100_000_000) throw new InvalidDataException("资料库实体过大。");
            switch (reader.GetString(0))
            {
                case "state": state = JsonSerializer.Deserialize<AppState>(text, AppStorage.Json); break;
                case "track": tracks.Add(JsonSerializer.Deserialize<TrackItem>(text, AppStorage.Json) ?? throw new InvalidDataException("歌曲记录为空。")); break;
                case "playlist": playlists.Add(JsonSerializer.Deserialize<Playlist>(text, AppStorage.Json) ?? throw new InvalidDataException("歌单记录为空。")); break;
                case "listening": listening.Add(JsonSerializer.Deserialize<ListeningEntry>(text, AppStorage.Json) ?? throw new InvalidDataException("统计记录为空。")); break;
                default: throw new InvalidDataException("未知资料库实体类型。");
            }
        }
        if (state is null) throw new InvalidDataException("资料库缺少设置记录。");
        state.Tracks = tracks; state.Playlists = playlists; state.ListeningEntries = listening;
        _known.Clear(); foreach (var document in Documents(state)) _known[(document.Kind, document.Id)] = (document.Order, document.Fingerprint);
        _initialized = true;
        return state;
    }
    public void Save(AppState state, bool playbackOnly = false)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var connection = Open(false); using var schema = connection.CreateCommand();
        schema.CommandText = $"PRAGMA application_id={ApplicationId}; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS entities(kind TEXT NOT NULL,id TEXT NOT NULL,sort_order INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(kind,id)) WITHOUT ROWID;"; schema.ExecuteNonQuery();
        // 定期进度保存只检查状态和统计。曲目/歌单修改均走完整保存，避免每 15 秒扫描数万首歌曲。
        playbackOnly &= _initialized;
        var present = Documents(state, playbackOnly).ToDictionary(d => (d.Kind, d.Id));
        using var transaction = connection.BeginTransaction(); using var write = connection.CreateCommand(); write.Transaction = transaction;
        write.CommandText = "INSERT INTO entities(kind,id,sort_order,payload) VALUES($kind,$id,$order,$payload) ON CONFLICT(kind,id) DO UPDATE SET sort_order=excluded.sort_order,payload=excluded.payload;";
        foreach (var name in new[] { "$kind", "$id", "$order", "$payload" }) write.Parameters.AddWithValue(name, "");
        LastWrittenRows = 0;
        foreach (var document in present.Values)
        {
            if (_known.TryGetValue((document.Kind, document.Id), out var prior) && prior.Order == document.Order && Equals(prior.Value, document.Fingerprint)) continue;
            write.Parameters[0].Value = document.Kind; write.Parameters[1].Value = document.Id; write.Parameters[2].Value = document.Order; write.Parameters[3].Value = document.Serialize(); write.ExecuteNonQuery(); LastWrittenRows++;
        }
        using var remove = connection.CreateCommand(); remove.Transaction = transaction; remove.CommandText = "DELETE FROM entities WHERE kind=$kind AND id=$id;"; remove.Parameters.AddWithValue("$kind", ""); remove.Parameters.AddWithValue("$id", "");
        // 完整恢复/迁移时缓存可能为空，从数据库补足删除集合，避免遗留旧实体。
        var old = _known.Keys.Where(k => !playbackOnly || k.Kind is "state" or "listening").ToList();
        if (!_initialized) { using var existing = connection.CreateCommand(); existing.Transaction = transaction; existing.CommandText = "SELECT kind,id FROM entities;"; using var rows = existing.ExecuteReader(); while (rows.Read()) old.Add((rows.GetString(0), rows.GetString(1))); }
        foreach (var key in old.Where(k => !present.ContainsKey(k))) { remove.Parameters[0].Value = key.Kind; remove.Parameters[1].Value = key.Id; remove.ExecuteNonQuery(); LastWrittenRows++; }
        transaction.Commit();
        if (!playbackOnly) _known.Clear(); else foreach (var key in old.Where(k => !present.ContainsKey(k))) _known.Remove(key);
        foreach (var document in present.Values) _known[(document.Kind, document.Id)] = (document.Order, document.Fingerprint);
        _initialized = true;
    }
    public void Backup(string destination)
    {
        DataDirectoryService.RejectLinkedAncestors(destination); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
        using var source = Open(true); using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()); target.Open(); source.BackupDatabase(target);
    }
}
