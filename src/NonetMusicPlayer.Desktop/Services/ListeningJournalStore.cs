using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>长期保存的听歌明细。仅缓存未写入的行；历史通过 SQLite 索引分页读取。</summary>
public sealed class ListeningJournalStore(string databasePath)
{
    private sealed record Pending(string SessionId, long Time, bool Closed, Func<string> Serialize, long Revision);
    private readonly Dictionary<(string Kind, string Id), Pending> _pending = [];
    private long _revision;
    public bool HasPending { get { lock (_pending) return _pending.Count > 0; } }
    private void Stage(string kind, string id, string sessionId, long time, bool closed, Func<string> serialize)
    {
        lock (_pending) _pending[(kind, id)] = new(sessionId, time, closed, serialize, ++_revision);
    }
    public void Stage(ListeningSessionRecord record) => Stage("session", record.Id, record.Id, record.StartedUtc.ToUnixTimeMilliseconds(), record.EndedUtc.HasValue, () => JsonSerializer.Serialize(record, AppStorage.Json));
    public void Stage(ListeningIntervalRecord record) => Stage("interval", record.Id, record.SessionId, record.StartedUtc.ToUnixTimeMilliseconds(), true, () => JsonSerializer.Serialize(record, AppStorage.Json));
    public void Stage(ListeningEventRecord record) => Stage("event", record.Id, record.SessionId, record.OccurredUtc.ToUnixTimeMilliseconds(), true, () => JsonSerializer.Serialize(record, AppStorage.Json));
    public void Stage(HourlyListeningEntry record) => Stage("hour", record.UtcHour.ToString(System.Globalization.CultureInfo.InvariantCulture), "", checked(record.UtcHour * 3600000), true, () => JsonSerializer.Serialize(record, AppStorage.Json));
    public IReadOnlyList<ListeningJournalDocument> Capture()
    {
        lock (_pending) return _pending.Select(p => new ListeningJournalDocument(p.Key.Kind, p.Key.Id, p.Value.SessionId, p.Value.Time, p.Value.Closed, p.Value.Serialize(), p.Value.Revision)).ToArray();
    }
    public void Acknowledge(IReadOnlyList<ListeningJournalDocument> written)
    {
        lock (_pending) foreach (var row in written) if (_pending.TryGetValue((row.Kind, row.Id), out var pending) && pending.Revision == row.Revision) _pending.Remove((row.Kind, row.Id));
    }
    public void ClearPending() { lock (_pending) _pending.Clear(); }
    internal static bool Exists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='listening_journal';";
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }
    internal static void EnsureSchema(SqliteConnection connection)
    {
        using var schema = connection.CreateCommand();
        schema.CommandText = "CREATE TABLE IF NOT EXISTS listening_journal(kind TEXT NOT NULL,id TEXT NOT NULL,session_id TEXT NOT NULL,occurred_utc INTEGER NOT NULL,closed INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(kind,id)) WITHOUT ROWID; CREATE INDEX IF NOT EXISTS listening_journal_time ON listening_journal(kind,occurred_utc,id); CREATE INDEX IF NOT EXISTS listening_journal_session ON listening_journal(session_id,occurred_utc,id); CREATE INDEX IF NOT EXISTS listening_journal_open ON listening_journal(closed) WHERE kind='session' AND closed=0;";
        schema.ExecuteNonQuery();
    }
    internal static void Write(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<ListeningJournalDocument> rows)
    {
        if (rows.Count == 0) return;
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO listening_journal(kind,id,session_id,occurred_utc,closed,payload) VALUES($kind,$id,$session,$time,$closed,$payload) ON CONFLICT(kind,id) DO UPDATE SET session_id=excluded.session_id,occurred_utc=excluded.occurred_utc,closed=excluded.closed,payload=excluded.payload;";
        foreach (var name in new[] { "$kind", "$id", "$session", "$time", "$closed", "$payload" }) command.Parameters.AddWithValue(name, "");
        foreach (var row in rows)
        {
            command.Parameters[0].Value = row.Kind; command.Parameters[1].Value = row.Id; command.Parameters[2].Value = row.SessionId;
            command.Parameters[3].Value = row.Time; command.Parameters[4].Value = row.Closed ? 1 : 0; command.Parameters[5].Value = row.Payload; command.ExecuteNonQuery();
        }
    }
    private SqliteConnection Open()
    {
        DataDirectoryService.RejectLinkedAncestors(databasePath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var options = connection.CreateCommand(); options.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA busy_timeout=3000;"; options.ExecuteNonQuery(); return connection;
    }
    public JsonObject Page(string kind = "session", int limit = 100, int offset = 0, string? sessionId = null)
    {
        if (kind is not ("session" or "interval" or "event" or "hour")) throw new InvalidDataException("Unknown listening record kind.");
        if (sessionId?.Length > 100) throw new InvalidDataException("Invalid listening session ID.");
        limit = Math.Clamp(limit, 1, 500); offset = Math.Max(0, offset); var items = new JsonArray(); long total = 0;
        if (File.Exists(databasePath))
        {
            using var connection = Open();
            if (Exists(connection))
            {
                using var count = connection.CreateCommand(); count.CommandText = "SELECT count(*) FROM listening_journal WHERE kind=$kind AND ($session IS NULL OR session_id=$session);";
                count.Parameters.AddWithValue("$kind", kind); count.Parameters.AddWithValue("$session", (object?)sessionId ?? DBNull.Value); total = Convert.ToInt64(count.ExecuteScalar());
                using var query = connection.CreateCommand(); query.CommandText = "SELECT payload FROM listening_journal WHERE kind=$kind AND ($session IS NULL OR session_id=$session) ORDER BY occurred_utc DESC,id DESC LIMIT $limit OFFSET $offset;";
                query.Parameters.AddWithValue("$kind", kind); query.Parameters.AddWithValue("$session", (object?)sessionId ?? DBNull.Value); query.Parameters.AddWithValue("$limit", limit); query.Parameters.AddWithValue("$offset", offset);
                using var reader = query.ExecuteReader(); while (reader.Read()) items.Add(JsonNode.Parse(reader.GetString(0)));
            }
        }
        return new() { ["schemaVersion"] = 1, ["kind"] = kind, ["total"] = total, ["limit"] = limit, ["offset"] = offset, ["items"] = items };
    }
    public void MarkInterrupted(DateTimeOffset detectedUtc)
    {
        if (!File.Exists(databasePath)) return;
        using var connection = Open(); if (!Exists(connection)) return;
        using var query = connection.CreateCommand(); query.CommandText = "SELECT payload FROM listening_journal WHERE kind='session' AND closed=0;";
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            var session = JsonSerializer.Deserialize<ListeningSessionRecord>(reader.GetString(0), AppStorage.Json)!;
            session.EndedUtc = session.UpdatedUtc; session.State = "ended"; session.EndReason = "interrupted"; var sequence = ++session.EventCount; Stage(session);
            Stage(new ListeningEventRecord { Id = session.Id + "-e-" + sequence.ToString("D8", System.Globalization.CultureInfo.InvariantCulture), SessionId = session.Id, Sequence = sequence, OccurredUtc = detectedUtc, LocalOffsetMinutes = session.LocalOffsetMinutes, Type = "interrupted", Position = session.LastPosition });
        }
    }
}
