using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager
{
    public IReadOnlyList<PluginManifest> MusicSearchSources => Installed.Where(p => p.Enabled && p.MusicSearch is not null && HasMediaSource(p)).ToArray();
    private async Task<JsonElement> CallMediaAsync(PluginManifest plugin, string method, Dictionary<string, string> args, CancellationToken token)
    {
        RequireInstalled(plugin);
        if (!plugin.Enabled || !HasMediaSource(plugin) || !plugin.Permissions.Contains("network")) throw new InvalidOperationException("Music source unavailable.");
        if (plugin.Type != "extension") return await (await GetClientAsync(plugin, token)).CallAsync(method, args, token);
        var request = new JsonObject { ["method"] = method };
        foreach (var (key, value) in args) request[key] = value;
        return await Extension(plugin).InvokeProvidedServiceAsync("media-source", request, token);
    }
    /// <summary>查询不写音乐库；只有加入歌单才建立持久引用。</summary>
    public async Task<(MusicSearchPage Page, IReadOnlyList<TrackItem> Tracks)> SearchMusicAsync(string sourceId, string query, string cursor = "", CancellationToken token = default)
    {
        var plugin = MusicSearchSources.FirstOrDefault(p => p.Id == sourceId) ?? throw new InvalidOperationException("Music search source unavailable.");
        if (query.Trim().Length is 0 or > 300 || cursor.Length > 512) throw new InvalidDataException("Invalid music query.");
        var answer = await CallMediaAsync(plugin, "music.search", new() { ["query"] = query.Trim(), ["cursor"] = cursor, ["limit"] = plugin.MusicSearch!.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) }, token);
        var page = answer.Deserialize(MusicSourceJson.Default.MusicSearchPage) ?? throw new InvalidDataException("Empty music search response.");
        MusicSearchContract.ValidatePage(page, plugin.MusicSearch.PageSize);
        return (page, page.Tracks.Select(t => CreateSourceTrack(plugin, t)).ToArray());
    }
    private static TrackItem CreateSourceTrack(PluginManifest plugin, ProviderTrack item)
    {
        MusicSearchContract.ValidateTrack(item);
        return new(plugin.Id + ":" + item.Id, item.Title, item.Artist, item.Album, "", "." + item.Format, 0)
        { ProviderId = plugin.Id, ProviderTrackId = item.Id, DurationSeconds = item.DurationSeconds };
    }
    /// <summary>按稳定 ID 读取封面和歌词，不保存过期播放 URL；搜索页面不产生无引用文件。</summary>
    public async Task EnrichSourceTrackAsync(TrackItem track, LyricsService lyrics)
    {
        var plugin = MusicSearchSources.FirstOrDefault(p => p.Id == track.ProviderId);
        if (plugin is null) return;
        try
        {
            var answer = await CallMediaAsync(plugin, "music.describe", new() { ["trackId"] = track.ProviderTrackId! }, default);
            var item = answer.Deserialize(MusicSourceJson.Default.ProviderTrack) ?? throw new InvalidDataException("Empty music metadata.");
            MusicSearchContract.ValidateTrack(item); if (item.Id != track.ProviderTrackId) throw new InvalidDataException("Music identity changed.");
            if (track.CoverPath is null && item.CoverBase64 is { Length: > 0 } cover)
            {
                var path = Path.Combine(_storage.ArtworkFolder, MusicLibraryScanner.StableId(track.Id) + ".image"); PluginPathPolicy.RejectLinkedAncestors(path);
                var bytes = Convert.FromBase64String(cover);
                using var input = new MemoryStream(bytes); using var image = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(input, 512);
                using var output = new MemoryStream(); image.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                await File.WriteAllBytesAsync(path, output.ToArray()); track.CoverPath = path;
            }
            if (!track.LyricsDisabled && lyrics.ExistingPath(track.Id) is null && item.Lyrics is { Length: > 0 } text) lyrics.Save(track.Id, text);
        }
        catch (Exception error) { AppLog.Warning("MusicSource", "Optional metadata unavailable: " + plugin.Id, error); }
    }
    /// <summary>先完整校验目录，再一次性建立歌单，失败不留下半个歌单。</summary>
    public async Task<IReadOnlyList<TrackItem>> ReadMusicCatalogAsync(PluginManifest plugin, string catalogId, CancellationToken token = default)
    {
        if (catalogId.Length is 0 or > 300) throw new InvalidDataException("Invalid catalog ID.");
        var tracks = new List<TrackItem>(); var seen = new HashSet<string>(StringComparer.Ordinal); var cursor = "";
        do
        {
            if (!seen.Add(cursor)) throw new InvalidDataException("Repeated catalog cursor.");
            var result = await CallMediaAsync(plugin, "catalog.list", new() { ["catalogId"] = catalogId, ["cursor"] = cursor }, token);
            var page = result.Deserialize(MusicSourceJson.Default.ProviderCatalog) ?? throw new InvalidDataException("Empty music catalog.");
            if (page.Tracks.Count > 1000 || tracks.Count + page.Tracks.Count > 50000 || page.NextCursor?.Length > 512) throw new InvalidDataException("Music catalog exceeds limit.");
            tracks.AddRange(page.Tracks.Select(t => CreateSourceTrack(plugin, t))); cursor = page.NextCursor ?? "";
        } while (cursor.Length > 0);
        return tracks.DistinctBy(t => t.Id).ToArray();
    }
}
