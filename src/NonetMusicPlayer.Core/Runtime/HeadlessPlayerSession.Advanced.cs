using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private readonly List<StoredMusicTrack> _musicResults = [];
    private readonly HashSet<string> _searchCursors = [];
    private string _searchSource = "", _searchQuery = "", _searchCursor = "";
    private readonly Stack<string> _undoLibrary = [];
    private static bool Undoable(string name) => name.StartsWith("playlist.", StringComparison.Ordinal) && name is not ("playlist.list" or "playlist.tracks" or "playlist.export")
        || name is "music.remove" or "favorite.add" or "favorite.remove" or "history.remove" or "history.clear" or "album.cover" or "artist.cover";

    private async Task<(JsonNode? Data, bool Save)> ExecuteAdvancedAsync(PlayerCommand command, CancellationToken token)
    {
        string Arg(int n) => CommandResults.Required(command, n);
        switch (command.Name)
        {
            case "player.stop": RecordPlaybackEvent("player.stop"); EndListeningRecord("stopped"); _audio.Stop(); _loaded = false; State.LastPosition = 0; return (null, true);
            case "queue.select":
                var selected = _queue.FirstOrDefault(t => t.Id == Arg(0)) ?? throw new InvalidDataException("Track is not in the queue.");
                if (State.Settings.PlayMode == Playback.PlaybackMode.Shuffle) SelectQueue(_queue, selected, true);
                _failed.Clear(); await StartAsync(selected, 0, token); return (null, true);
            case "queue.move":
                var moving = _queue.FirstOrDefault(t => t.Id == Arg(0)) ?? throw new InvalidDataException("Track is not in the queue.");
                var at = CommandResults.Integer(command, 1, 1, _queue.Count) - 1; _queue.Remove(moving); _queue.Insert(at, moving); State.ManualQueueOrder = true;
                return (TracksJson(_queue), true);
            case "playlist.tracks": return (TracksJson(PlaylistTracks(FindPlaylist(Arg(0)))), false);
            case "playlist.from-folders":
                var created = new JsonArray();
                var prepared = new List<(StoredPlaylist Playlist, IReadOnlyList<StoredMusicTrack> Tracks)>();
                foreach (var folder in command.Arguments)
                {
                    var full = Path.GetFullPath(folder); if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
                    var tracks = await ReadPathsAsync([full], token); var playlist = new StoredPlaylist { Name = ValidName(Path.GetFileName(Path.TrimEndingDirectorySeparator(full))) };
                    prepared.Add((playlist, tracks));
                }
                foreach (var item in prepared) { State.Playlists.Add(item.Playlist); AddToPlaylist(item.Playlist, item.Tracks); created.Add(new JsonObject { ["id"] = item.Playlist.Id, ["name"] = item.Playlist.Name, ["count"] = item.Tracks.Count }); }
                return (created, true);
            case "music.sources": return (new JsonArray(new[] { new JsonObject { ["id"] = "local", ["name"] = "Local" } }.Concat(Plugins.MusicSearchSources.Select(p => new JsonObject { ["id"] = p.Id, ["name"] = p.MusicSearch!.Label })).Cast<JsonNode>().ToArray()), false);
            case "music.query": await QueryMusicAsync(Arg(0), Arg(1), false, token); return (MusicResultsJson(), false);
            case "music.more": if (_searchCursor.Length > 0) await QueryMusicAsync(_searchSource, _searchQuery, true, token); return (MusicResultsJson(), false);
            case "music.results": return (MusicResultsJson(), false);
            case "music.search": return (TracksJson(State.Tracks.Where(t => (t.Metadata.Title + " " + t.Metadata.Artist + " " + t.Metadata.Album).Contains(Arg(0), StringComparison.CurrentCultureIgnoreCase))), false);
            case "album.list": case "artist.list":
                var kind = command.Name.Split('.')[0];
                return (new JsonArray(State.Tracks.GroupBy(t => kind == "album" ? t.Metadata.Album : t.Metadata.Artist).OrderBy(g => g.Key).Select(g => (JsonNode)new JsonObject
                    { ["name"] = g.Key, ["count"] = g.Count(), ["coverPath"] = State.GroupCovers.TryGetValue(kind + ":" + g.Key, out var custom) ? custom : g.Select(t => t.Metadata.CoverPath).FirstOrDefault(p => p is not null) }).ToArray()), false);
            case "album.tracks": case "artist.tracks": return (TracksJson(State.Tracks.Where(t => (command.Name.StartsWith("album.") ? t.Metadata.Album : t.Metadata.Artist) == Arg(0))), false);
            case "album.cover": case "artist.cover":
                var key = command.Name.Split('.')[0] + ":" + Arg(0);
                if (Arg(1) == "auto") State.GroupCovers.Remove(key); else State.GroupCovers[key] = Arg(1) == "default" ? "" : StoreCover(Arg(1)); return (null, true);
            case "playlist.import":
                var file = Path.GetFullPath(Arg(0)); if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("M3U is too large.");
                var paths = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => Path.IsPathRooted(l) ? l : Path.Combine(Path.GetDirectoryName(file)!, l));
                var imported = await ReadPathsAsync(paths, token);
                var target = command.Arguments.Count > 1 ? FindPlaylist(Arg(1)) : new StoredPlaylist { Name = ValidName(Path.GetFileNameWithoutExtension(file)) };
                if (!State.Playlists.Contains(target)) State.Playlists.Add(target); AddToPlaylist(target, imported); return (new JsonObject { ["id"] = target.Id, ["count"] = imported.Count }, true);
            case "playlist.export":
                var destination = Path.GetFullPath(Arg(0)); if (File.Exists(destination)) throw new IOException("Output already exists.");
                var export = FindPlaylist(command.Arguments.Count > 1 ? Arg(1) : State.LastPlaylistId ?? throw new InvalidOperationException("Specify a playlist id."));
                File.WriteAllText(destination, "#EXTM3U\n" + string.Join('\n', PlaylistTracks(export).Where(t => t.ProviderId is null).Select(t => "#EXTINF:" + (int)t.Metadata.DurationSeconds + "," + t.Metadata.Title + "\n" + t.Metadata.FilePath)) + "\n", new UTF8Encoding(false)); return (JsonValue.Create(destination), false);
            case "undo":
                if (!_undoLibrary.TryPop(out var snapshot)) throw new InvalidOperationException("Nothing to undo.");
                var old = JsonSerializer.Deserialize<HeadlessPlayerState>(snapshot, CoreJson.Options)!;
                State.Tracks = old.Tracks; State.Playlists = old.Playlists; State.History = old.History; State.RecentTemporaryTracks = old.RecentTemporaryTracks; State.GroupCovers = old.GroupCovers; RefreshQueue(); return (null, true);
            case "lyrics.reload": return (JsonValue.Create(LyricPath() is { } path ? File.ReadAllText(path) : ""), false);
            case "lyrics.clear":
                if (Current is null) throw new InvalidOperationException("Select a track first.");
                // 取消软件内关联，保留原歌词和音频文件，重新导入即可恢复。
                State.DisabledLyrics.Add(Current.Id);
                return (null, true);
            case "lyrics.export": var lyrics = LyricPath() ?? throw new FileNotFoundException("No associated lyrics."); File.Copy(lyrics, Path.GetFullPath(Arg(0)), false); return (null, false);
            case "statistics.hours": return (HourlyStatistics(command.Arguments.Count == 0 ? 24 : CommandResults.Integer(command, 0, 1, 72)), false);
            case "statistics.history": return (ListeningHistory(command), false);
            case "plugins.info":
                var plugin = Plugins.Find(Arg(0)); return (new JsonObject { ["id"] = plugin.Id, ["name"] = plugin.Name, ["version"] = plugin.Version, ["runtime"] = plugin.Runtime, ["enabled"] = plugin.Enabled, ["permissions"] = Strings(plugin.Permissions), ["services"] = Strings(plugin.ProvidedServices), ["search"] = plugin.MusicSearch is not null }, false);
            case "plugins.trust": Plugins.TrustManaged(Plugins.Find(Arg(0))); return (null, false);
            case "plugins.reload": Plugins.Reload(Plugins.Find(Arg(0))); return (null, false);
            case "plugins.state": return (await Plugins.ExtensionStateAsync(Plugins.Find(Arg(0)), token), false);
            case "plugins.invoke": return (await Plugins.InvokeExtensionAsync(Plugins.Find(Arg(0)), Arg(1), command.Arguments.Count > 2 ? JsonNode.Parse(Arg(2)) as JsonObject ?? throw new InvalidDataException("JSON object required.") : new(), token), false);
            default: return await ExecuteNetworkAsync(command, token);
        }
    }
    private async Task QueryMusicAsync(string source, string query, bool more, CancellationToken token)
    {
        query = query.Trim(); if (query.Length is 0 or > 300) throw new InvalidDataException("Search query must contain 1–300 characters.");
        if (!more) { _musicResults.Clear(); _searchCursors.Clear(); _searchCursor = ""; _searchSource = source; _searchQuery = query; }
        if (source is "" or "local") { _musicResults.AddRange(State.Tracks.Where(t => (t.Metadata.Title + " " + t.Metadata.Artist + " " + t.Metadata.Album).Contains(query, StringComparison.CurrentCultureIgnoreCase))); return; }
        var page = await Plugins.SearchMusicAsync(source, query, _searchCursor, token); _searchCursors.Add(_searchCursor);
        var known = _musicResults.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var track in page.Tracks)
        {
            var id = source + ":" + track.Id; if (!known.Add(id)) continue;
            _musicResults.Add(StoreSourceTrack(source, track));
        }
        var next = page.NextCursor ?? ""; _searchCursor = page.Tracks.Count == 0 || _searchCursors.Contains(next) ? "" : next;
    }
    private JsonObject MusicResultsJson() => new() { ["source"] = _searchSource, ["query"] = _searchQuery, ["hasMore"] = _searchCursor.Length > 0, ["items"] = TracksJson(_musicResults) };
    /// <summary>搜索和目录采用同一身份与资源命名，加入歌单不会丢失封面或歌词。</summary>
    private StoredMusicTrack StoreSourceTrack(string provider, ProviderTrack track)
    {
        var id = provider + ":" + track.Id; var resource = MusicFileReader.StableId(id); string? cover = null;
        if (track.CoverBase64 is { Length: > 0 } image) { cover = Path.Combine(ArtworkDirectory, resource + ".image"); File.WriteAllBytes(cover, Convert.FromBase64String(image)); }
        if (track.Lyrics is { Length: > 0 } lyrics) { Directory.CreateDirectory(LyricsDirectory); AtomicFile.Write(Path.Combine(LyricsDirectory, resource + ".lrc"), lyrics); }
        return new() { ProviderId = provider, ProviderTrackId = track.Id, Metadata = new MusicMetadata(id, track.Title, track.Artist, track.Album, "", "." + track.Format, 0, track.DurationSeconds, cover) };
    }
    private void RecordListeningHours(double seconds)
    {
        var end = DateTimeOffset.UtcNow; var start = end.AddSeconds(-seconds);
        while (start < end)
        {
            var hour = start.ToUnixTimeSeconds() / 3600; var next = DateTimeOffset.FromUnixTimeSeconds((hour + 1) * 3600); if (next > end) next = end;
            var credit = (next - start).TotalSeconds; var index = State.ListeningHours.FindIndex(h => h.UtcHour == hour && h.TrackId == Current!.Id);
            if (index < 0) State.ListeningHours.Add(new(hour, Current!.Id, credit)); else State.ListeningHours[index] = State.ListeningHours[index] with { Seconds = State.ListeningHours[index].Seconds + credit };
            start = next;
        }
    }
    private JsonArray HourlyStatistics(int hours)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
        return new JsonArray(Enumerable.Range(0, hours).Select(i => now - hours + 1 + i).Select(hour => (JsonNode)new JsonObject
            { ["utcHour"] = hour, ["start"] = DateTimeOffset.FromUnixTimeSeconds(hour * 3600).ToString("o"), ["seconds"] = State.ListeningHours.Where(h => h.UtcHour == hour).Sum(h => h.Seconds) }).ToArray());
    }
    private JsonArray ListeningHistory(PlayerCommand command)
    {
        var kind = command.Arguments.FirstOrDefault() ?? "session";
        var limit = command.Arguments.Count > 1 ? CommandResults.Integer(command, 1, 1, 500) : 100;
        var offset = command.Arguments.Count > 2 ? CommandResults.Integer(command, 2, 0, int.MaxValue) : 0;
        IEnumerable<JsonNode> records = kind == "hour" ? State.ListeningHours.OrderByDescending(h => h.UtcHour).Select(h => JsonSerializer.SerializeToNode(h, CoreJson.Options)!)
            : ListeningRecords(kind, command.Arguments.Count > 3 ? command.Arguments[3] : null);
        return new JsonArray(records.Skip(offset).Take(limit).ToArray());
    }
}
