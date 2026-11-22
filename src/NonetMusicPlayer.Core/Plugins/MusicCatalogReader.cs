using System.Text.Json;

namespace NonetMusicPlayer.Core.Plugins;

public sealed record MusicCatalogSnapshot(IReadOnlyList<ProviderTrack> Tracks, string? Name, int UnavailableCount);

/// <summary>桌面与 CLI 先校验完整平台快照，再按 ID 去重并保留平台顺序。</summary>
public static class MusicCatalogReader
{
    public static string ValidateId(string id)
        => string.IsNullOrWhiteSpace(id) || id.Length > 300 || id.Any(char.IsControl)
            ? throw new InvalidDataException("Invalid catalog ID.") : id;

    public static async Task<MusicCatalogSnapshot> ReadAsync(
        Func<string, CancellationToken, Task<JsonElement>> readPage, CancellationToken token)
    {
        var tracks = new List<ProviderTrack>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var cursor = "";
        var received = 0;
        var unavailable = 0;
        int? total = null;
        string? name = null;
        do
        {
            token.ThrowIfCancellationRequested();
            if (!cursors.Add(cursor)) throw new InvalidDataException("Repeated catalog cursor.");
            var response = await readPage(cursor, token);
            if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("tracks", out var rows)
                || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 1000)
                throw new InvalidDataException("Incomplete music catalog.");
            var page = response.Deserialize(MusicSourceJson.Default.ProviderCatalog) ?? throw new InvalidDataException("Empty music catalog.");
            if (page.NextCursor?.Length > 512 || page.Total is < 0 or > 50000
                || page.UnavailableCount is < 0 or > 1000 || (total is not null && page.Total is not null && total != page.Total))
                throw new InvalidDataException("Incomplete music catalog.");
            total ??= page.Total;
            var count = page.Tracks.Count + page.UnavailableCount;
            received += count;
            if (count > 1000 || received > (total ?? 50000) || (count == 0 && !string.IsNullOrEmpty(page.NextCursor)))
                throw new InvalidDataException("Incomplete music catalog.");
            unavailable += page.UnavailableCount;
            if (name is null && page.Name is { Length: > 0 and <= 200 } title && !title.Any(char.IsControl)) name = title;
            foreach (var track in page.Tracks)
            {
                if (track is null) throw new InvalidDataException("Missing track in music catalog.");
                MusicSearchContract.ValidateTrack(track);
                // 订阅使用轻量目录，保持有界内存，封面与歌词由宿主另按曲目读取。
                track.CoverBase64 = track.Lyrics = track.LyricsTranslation = null;
                if (ids.Add(track.Id)) tracks.Add(track);
            }
            cursor = page.NextCursor ?? "";
        } while (cursor.Length > 0);
        if (total is not null && received != total) throw new InvalidDataException("Incomplete music catalog.");
        return new(tracks, name, unavailable);
    }
}
