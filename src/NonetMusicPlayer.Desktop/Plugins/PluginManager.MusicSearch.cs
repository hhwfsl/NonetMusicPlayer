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
        var answer = await CallMediaAsync(plugin, "music.search", new() { ["query"] = query.Trim(), ["cursor"] = cursor, ["limit"] = plugin.MusicSearch!.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), ["includeArtwork"] = plugin.MusicSearch.DeferredArtwork ? "false" : "true" }, token);
        var page = answer.Deserialize(MusicSourceJson.Default.MusicSearchPage) ?? throw new InvalidDataException("Empty music search response.");
        MusicSearchContract.ValidatePage(page, plugin.MusicSearch.PageSize);
        var tracks = page.Tracks.Select(t => CreateSourceTrack(plugin, t)).ToArray();
        await CacheSearchArtworkAsync(plugin, page, tracks, token);
        return (page, tracks);
    }
    /// <summary>可选批量封面协议限定为发起音源自己的稳定 ID，不接受任意网址。</summary>
    public async Task<IReadOnlyList<TrackItem>> ReadMusicArtworkAsync(PluginManifest plugin, IReadOnlyList<TrackItem> tracks)
    {
        if (tracks.Count is 0 or > 30 || tracks.Any(t => t.ProviderId != plugin.Id)) throw new InvalidDataException("Invalid artwork batch.");
        var ids = new JsonArray(tracks.Select(t => (JsonNode?)JsonValue.Create(t.ProviderTrackId)).ToArray()).ToJsonString();
        var answer = await CallMediaAsync(plugin, "music.artwork", new() { ["trackIds"] = ids }, default);
        var page = answer.Deserialize(MusicSourceJson.Default.MusicSearchPage) ?? throw new InvalidDataException("Empty artwork batch.");
        MusicSearchContract.ValidatePage(page, tracks.Count);
        if (page.Tracks.Any(t => !tracks.Any(s => s.ProviderTrackId == t.Id))) throw new InvalidDataException("Unexpected artwork identity.");
        var result = page.Tracks.Select(t => CreateSourceTrack(plugin, t)).ToArray();
        await CacheSearchArtworkAsync(plugin, page, result, default); return result;
    }
    private async Task CacheSearchArtworkAsync(PluginManifest plugin, MusicSearchPage page, TrackItem[] tracks, CancellationToken token)
    {
        // 搜索不建立音乐库引用。缩略图在独立临时缓存内，最多 256 张 / 32 MB。
        await Task.Run(() =>
        {
            var folder = Path.Combine(_storage.Root, "Temp", "MusicSearch"); PluginPathPolicy.RejectLinkedAncestors(folder); Directory.CreateDirectory(folder);
            for (var i = 0; i < tracks.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (page.Tracks[i].CoverBase64 is not { Length: > 0 } data) continue;
                try
                {
                    var path = Path.Combine(folder, MusicLibraryScanner.StableId(tracks[i].Id) + ".png"); PluginPathPolicy.RejectLinkedAncestors(path);
                    using var image = BoundedPluginImage.Decode(data, 160); image.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    tracks[i].CoverPath = path; tracks[i].HasSearchCover = true;
                }
                catch (Exception error) when (error is InvalidDataException or FormatException or ArgumentException or IOException) { AppLog.Warning("MusicSource", "Search artwork unavailable: " + plugin.Id); }
            }
            long bytes = 0; var count = 0;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc))
            {
                bytes += file.Length; count++;
                if (count <= 256 && bytes <= 32 * 1024 * 1024 || tracks.Any(t => t.CoverPath == file.FullName)) continue;
                PluginPathPolicy.RejectLinkedAncestors(file.FullName); file.Delete(); ArtworkCache.Shared.Invalidate(file.FullName);
            }
        }, token);
    }
    private static TrackItem CreateSourceTrack(PluginManifest plugin, ProviderTrack item)
    {
        MusicSearchContract.ValidateTrack(item);
        return new(plugin.Id + ":" + item.Id, item.Title, item.Artist, item.Album, "", "." + item.Format, 0)
        { ProviderId = plugin.Id, ProviderTrackId = item.Id, DurationSeconds = item.DurationSeconds };
    }
    /// <summary>搜索缩略图在加入歌单时转存，临时缓存清理不影响持久引用。</summary>
    public void PersistSearchCover(TrackItem track)
    {
        if (!track.HasSearchCover || track.CoverPath is not { } source) return;
        var folder = Path.Combine(_storage.Root, "Temp", "MusicSearch");
        if (!DataDirectoryService.Contains(folder, source) || !File.Exists(source)) { track.HasSearchCover = false; return; }
        PluginPathPolicy.RejectLinkedAncestors(source);
        var target = Path.Combine(_storage.ArtworkFolder, MusicLibraryScanner.StableId(track.Id) + ".image"); PluginPathPolicy.RejectLinkedAncestors(target);
        if (!File.Exists(target)) File.Copy(source, target); track.CoverPath = target; track.HasSearchCover = false;
    }
    /// <summary>按稳定 ID 读取封面和歌词，不保存过期播放 URL。</summary>
    public async Task EnrichSourceTrackAsync(TrackItem track, LyricsService lyrics)
        => await EnrichSourceTrackAsync(track, lyrics, CancellationToken.None);
    public async Task EnrichSourceTrackAsync(TrackItem track, LyricsService lyrics, CancellationToken token)
    {
        var plugin = MusicSearchSources.FirstOrDefault(p => p.Id == track.ProviderId);
        if (plugin is null) return;
        try
        {
            var answer = await CallMediaAsync(plugin, "music.describe", new() { ["trackId"] = track.ProviderTrackId! }, token);
            token.ThrowIfCancellationRequested();
            var item = answer.Deserialize(MusicSourceJson.Default.ProviderTrack) ?? throw new InvalidDataException("Empty music metadata.");
            MusicSearchContract.ValidateTrack(item); if (item.Id != track.ProviderTrackId) throw new InvalidDataException("Music identity changed.");
            var path = Path.Combine(_storage.ArtworkFolder, MusicLibraryScanner.StableId(track.Id) + ".image");
            var thumbnail = false;
            if (track.CoverPath == path && File.Exists(path))
            {
                PluginPathPolicy.RejectLinkedAncestors(path);
                using var probe = File.OpenRead(path); using var codec = SkiaSharp.SKCodec.Create(probe);
                thumbnail = codec is not null && Math.Max(codec.Info.Width, codec.Info.Height) <= 160;
            }
            // 收藏过的搜索缩略图仍应在播放时升级；不覆盖用户手工指定的其他封面路径。
            if ((track.CoverPath is null || track.HasSearchCover || thumbnail) && item.CoverBase64 is { Length: > 0 } cover)
            {
                PluginPathPolicy.RejectLinkedAncestors(path);
                using var image = BoundedPluginImage.Decode(cover, 512);
                using var output = new MemoryStream(); image.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                await File.WriteAllBytesAsync(path, output.ToArray(), token); track.CoverPath = path; track.HasSearchCover = false; track.RefreshArtwork();
            }
            if (!track.LyricsDisabled && item.Lyrics is { Length: > 0 } text && (lyrics.ExistingPath(track.Id) is null
                || track.LyricsSourcePath is null && item.LyricsTranslation is { Length: > 0 } && !lyrics.Read(track.Id).Contains("[nonet:translation]", StringComparison.Ordinal)))
                lyrics.SaveProvider(track.Id, text, item.LyricsTranslation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) { AppLog.Warning("MusicSource", "Optional metadata unavailable: " + plugin.Id, error); }
        PersistSearchCover(track);
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
