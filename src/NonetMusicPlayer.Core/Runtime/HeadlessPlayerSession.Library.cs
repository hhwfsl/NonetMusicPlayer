using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private StoredMusicTrack FindTrack(string id) => State.Tracks.Concat(State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == id) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.TrackNotFound"));
    private StoredPlaylist FindPlaylist(string id) => State.Playlists.FirstOrDefault(p => p.Id == id) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaylistNotFound"));
    private async Task<IReadOnlyList<StoredMusicTrack>> ReadPathsAsync(IEnumerable<string> inputs, CancellationToken token)
    {
        var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var pending = new Stack<string>(inputs.Select(Path.GetFullPath));
        while (pending.TryPop(out var path))
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) { if (!MusicFileReader.SupportedExtensions.Contains(Path.GetExtension(path))) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnsupportedAudio")); files.Add(path); }
            else if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path)) if (MusicFileReader.SupportedExtensions.Contains(Path.GetExtension(file))) files.Add(file);
                foreach (var child in Directory.EnumerateDirectories(path)) if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
            else throw new FileNotFoundException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.AudioMissing"));
            if (files.Count > 100000) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ImportTooLarge"));
        }
        var reader = new MusicFileReader(ArtworkDirectory, LyricsDirectory);
        var tracks = new System.Collections.Concurrent.ConcurrentBag<StoredMusicTrack>();
        await Parallel.ForEachAsync(files, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) }, (file, _) =>
        {
            tracks.Add(new StoredMusicTrack { Metadata = reader.Read(file) }); return ValueTask.CompletedTask;
        });
        return tracks.OrderBy(t => t.Metadata.FilePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private void EnsureStored(IEnumerable<StoredMusicTrack> tracks)
    {
        var known = State.Tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var track in tracks) if (known.Add(track.Id)) State.Tracks.Insert(0, track);
    }
    private void AddToPlaylist(StoredPlaylist playlist, IEnumerable<StoredMusicTrack> tracks)
    {
        var items = tracks.DistinctBy(t => t.Id).ToArray(); EnsureStored(items);
        var existing = playlist.TrackIds.ToHashSet(StringComparer.Ordinal);
        playlist.TrackIds.InsertRange(0, items.Select(t => t.Id).Where(existing.Add));
        RefreshQueue();
    }
    private void RefreshQueue()
    {
        if (State.LastPlaylistId is { } id && State.Playlists.FirstOrDefault(p => p.Id == id) is { } playlist) _queue = PlaylistTracks(playlist).ToList();
        _shuffle.Retain(State.Tracks.Concat(State.RecentTemporaryTracks).Select(t => t.Id));
    }
    private void AddHistory(StoredMusicTrack track)
    {
        State.History.Remove(track.Id); State.History.Insert(0, track.Id);
        if (!State.Tracks.Any(t => t.Id == track.Id))
        {
            State.RecentTemporaryTracks.RemoveAll(t => t.Id == track.Id); State.RecentTemporaryTracks.Add(track);
        }
        TrimHistory();
    }
    private void TrimHistory()
    {
        State.History = State.History.Distinct(StringComparer.Ordinal).Take(State.Settings.HistoryLimit).ToList();
        var keep = State.History.ToHashSet(StringComparer.Ordinal); if (Current is not null) keep.Add(Current.Id);
        State.RecentTemporaryTracks.RemoveAll(t => !keep.Contains(t.Id));
    }
    private void AccountListening()
    {
        var now = Stopwatch.GetTimestamp(); var elapsed = Math.Clamp(Stopwatch.GetElapsedTime(_listeningAnchor, now).TotalSeconds, 0, 2); _listeningAnchor = now;
        if (!_loaded || !_audio.IsPlaying || Current is null || !State.Tracks.Any(t => t.Id == Current.Id)) return;
        var entry = ListeningEntry(); entry.Seconds += elapsed; _sessionSeconds += elapsed;
        if (_sessionSeconds >= 30) CountSession();
    }
    private StoredListeningEntry ListeningEntry()
    {
        var date = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var entry = State.ListeningEntries.FirstOrDefault(e => e.TrackId == Current!.Id && e.Date == date);
        if (entry is null) { entry = new() { Date = date, TrackId = Current!.Id, Title = Current.Metadata.Title, Artist = Current.Metadata.Artist }; State.ListeningEntries.Add(entry); }
        return entry;
    }
    private void CountSession()
    {
        if (_sessionCounted || _sessionSeconds <= 0 || Current is null || !State.Tracks.Any(t => t.Id == Current.Id)) return;
        ListeningEntry().PlayCount++; _sessionCounted = true;
    }
    private JsonObject Statistics(DateOnly? selected, string period)
    {
        if (period is not ("all" or "day" or "month" or "year")) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PeriodInvalid"));
        var date = selected ?? DateOnly.FromDateTime(DateTime.Today);
        var prefix = date.ToString(period switch { "month" => "yyyy-MM", "year" => "yyyy", _ => "yyyy-MM-dd" }, System.Globalization.CultureInfo.InvariantCulture);
        var entries = State.ListeningEntries.Where(e => period == "all" || e.Date.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        var songs = entries.GroupBy(e => e.TrackId).Select(g => new { Id = g.Key, Name = g.First().Title, Seconds = g.Sum(e => e.Seconds), Plays = g.Sum(e => e.PlayCount) }).ToArray();
        var artists = entries.GroupBy(e => e.Artist).Select(g => new { Name = g.Key, Seconds = g.Sum(e => e.Seconds), Plays = g.Sum(e => e.PlayCount) }).ToArray();
        var longest = songs.OrderByDescending(s => s.Seconds).FirstOrDefault(); var most = songs.OrderByDescending(s => s.Plays).FirstOrDefault();
        var artist = artists.OrderByDescending(s => s.Seconds).FirstOrDefault();
        return new JsonObject { ["date"] = date.ToString("yyyy-MM-dd"), ["period"] = period, ["totalSeconds"] = entries.Sum(e => e.Seconds), ["totalPlays"] = entries.Sum(e => e.PlayCount), ["activeDays"] = entries.Select(e => e.Date).Distinct().Count(),
            ["longestSong"] = longest is null ? null : new JsonObject { ["id"] = longest.Id, ["name"] = longest.Name, ["seconds"] = longest.Seconds },
            ["mostPlayedSong"] = most is null ? null : new JsonObject { ["id"] = most.Id, ["name"] = most.Name, ["plays"] = most.Plays },
            ["longestArtist"] = artist is null ? null : new JsonObject { ["name"] = artist.Name, ["seconds"] = artist.Seconds } };
    }
    private static string LyricFileId(StoredMusicTrack track) => track.ProviderId is null ? track.Id : MusicFileReader.StableId(track.Id);
    private string? LyricPath() => Current is null ? null : new[] { ".lrc", ".txt" }.Select(ext => Path.Combine(LyricsDirectory, LyricFileId(Current) + ext)).FirstOrDefault(File.Exists);
    private string StoreCover(string file)
    {
        if (!File.Exists(file) || new FileInfo(file).Length is <= 0 or > 20 * 1024 * 1024) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.CoverSize"));
        if (Path.GetExtension(file).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp")) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.CoverFormat"));
        using var input = File.OpenRead(file); var target = Path.Combine(ArtworkDirectory, Convert.ToHexString(SHA256.HashData(input)) + Path.GetExtension(file).ToLowerInvariant());
        if (!File.Exists(target)) File.Copy(file, target, false); return target;
    }
    private static string ValidName(string name) => name.Trim() is { Length: > 0 and <= 80 } value ? value : throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaylistName"));
    private static JsonObject TrackJson(StoredMusicTrack t) => new() { ["id"] = t.Id, ["title"] = t.Metadata.Title, ["artist"] = t.Metadata.Artist, ["album"] = t.Metadata.Album, ["path"] = t.Metadata.FilePath, ["duration"] = t.Metadata.DurationSeconds, ["coverPath"] = t.Metadata.CoverPath, ["providerId"] = t.ProviderId };
    private static JsonArray TracksJson(IEnumerable<StoredMusicTrack> tracks) => new(tracks.Select(t => (JsonNode)TrackJson(t)).ToArray());
    private static JsonArray Strings(IEnumerable<string> strings) => new(strings.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
}
