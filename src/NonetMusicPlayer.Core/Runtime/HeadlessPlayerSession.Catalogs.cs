using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private readonly CancellationTokenSource _catalogStop = new();
    private readonly Dictionary<string, long> _nextCatalogSync = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _catalogMetadataRetry = new(StringComparer.Ordinal);
    private List<StoredPlaylist>? _requestedCatalogs;
    private long _nextCatalogScan;

    private PluginManifest? FollowedCatalogSource(string id) => Plugins.Installed.FirstOrDefault(plugin => plugin.Id == id && plugin.Enabled
        && (plugin.Type == "provider" || plugin.ProvidedServices.Contains("media-source"))
        && plugin.Permissions.Contains("network") && plugin.Permissions.Contains("library-write"));

    private StoredPlaylist FollowCatalog(PluginManifest plugin, string catalogId, string name)
    {
        MusicCatalogReader.ValidateId(catalogId);
        name = name.Trim();
        if (name.Length is 0 or > 200 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid catalog name.");
        var existing = State.Playlists.FirstOrDefault(p => p.SourcePluginId == plugin.Id && p.SourceCatalogId == catalogId);
        if (existing is not null) return existing;
        var playlist = new StoredPlaylist { Name = CatalogName(name), SourcePluginId = plugin.Id, SourceCatalogId = catalogId };
        State.Playlists.Add(playlist);
        Save();
        return playlist;
    }

    private static string CatalogName(string name) => name.Length > 80 ? name[..79] + "…" : name;

    private static void RequireEditablePlaylist(StoredPlaylist playlist)
    {
        if (playlist.IsSourceLinked) throw new InvalidOperationException(LocalizationCatalog.Get("MusicSearch.CatalogReadOnly"));
    }

    private StoredPlaylist FindEditablePlaylist(string id)
    {
        var playlist = FindPlaylist(id);
        RequireEditablePlaylist(playlist);
        return playlist;
    }

    private async Task<JsonObject> SyncCatalogAsync(StoredPlaylist playlist, CancellationToken token)
    {
        if (!playlist.IsSourceLinked) throw new InvalidOperationException(LocalizationCatalog.Get("MusicSearch.CatalogNotFollowed"));
        var source = FollowedCatalogSource(playlist.SourcePluginId!) ?? throw new InvalidOperationException(LocalizationCatalog.Get("MusicSearch.CatalogSourceUnavailable"));
        _nextCatalogSync[playlist.Id] = Environment.TickCount64 + 5 * 60_000;
        var snapshot = await Plugins.ReadFollowedCatalogAsync(source, playlist.SourceCatalogId!, token);
        token.ThrowIfCancellationRequested();
        var tracks = snapshot.Tracks.Select(track => StoreSourceTrack(source.Id, track)).ToArray();
        var order = tracks.Select(track => track.Id).ToList();
        var title = string.IsNullOrWhiteSpace(snapshot.Name) ? playlist.Name : CatalogName(snapshot.Name.Trim());
        var existing = State.Tracks.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
        if (playlist.Name != title || !playlist.TrackIds.SequenceEqual(order) || order.Any(id => !existing.Contains(id)))
        {
            // 完整快照到齐才修改已保存歌单；命中稳定 ID 时复用用户已有的曲库引用。
            var removed = playlist.TrackIds.Except(order, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var newTracks = tracks.Where(track => !existing.Contains(track.Id)).ToArray();
            foreach (var track in newTracks) track.NeedsSourceMetadata = true;
            EnsureStored(newTracks);
            playlist.Name = title;
            playlist.TrackIds = order;
            PruneCatalogTracks(removed);
            RefreshQueue();
            Save();
        }
        return new() { ["id"] = playlist.Id, ["name"] = playlist.Name, ["count"] = order.Count, ["unavailable"] = snapshot.UnavailableCount, ["readOnly"] = true };
    }

    private void UnfollowCatalog(StoredPlaylist playlist)
    {
        if (!playlist.IsSourceLinked) throw new InvalidOperationException(LocalizationCatalog.Get("MusicSearch.CatalogNotFollowed"));
        State.Playlists.Remove(playlist);
        _nextCatalogSync.Remove(playlist.Id);
        if (State.LastPlaylistId == playlist.Id) State.LastPlaylistId = null;
        PruneCatalogTracks(playlist.TrackIds.ToHashSet(StringComparer.Ordinal));
    }

    private void PruneCatalogTracks(HashSet<string> removed)
    {
        if (removed.Count == 0) return;
        removed.ExceptWith(State.Playlists.SelectMany(playlist => playlist.TrackIds));
        var keep = State.History.ToHashSet(StringComparer.Ordinal);
        if (Current is not null) keep.Add(Current.Id);
        var recent = State.RecentTemporaryTracks.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var track in State.Tracks.Where(track => removed.Contains(track.Id) && keep.Contains(track.Id)))
            if (recent.Add(track.Id)) State.RecentTemporaryTracks.Add(track);
        State.Tracks.RemoveAll(track => removed.Contains(track.Id));
    }

    private async Task TickCatalogsAsync()
    {
        var now = Environment.TickCount64;
        if (now >= _nextCatalogScan)
        {
            var due = State.Playlists.Where(p => p.IsSourceLinked && _nextCatalogSync.GetValueOrDefault(p.Id) <= now && FollowedCatalogSource(p.SourcePluginId!) is not null)
                .OrderBy(p => _nextCatalogSync.GetValueOrDefault(p.Id)).FirstOrDefault();
            _nextCatalogScan = now + (due is null ? 30_000 : 500);
            if (due is not null)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_catalogStop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                try { await SyncCatalogAsync(due, deadline.Token); }
                catch (Exception error) when (!_catalogStop.IsCancellationRequested)
                { AppLog.Warning("MusicSource", "CLI 平台歌单同步失败，保留上次快照。", error); }
            }
        }
        var pending = State.Tracks.FirstOrDefault(track => track.NeedsSourceMetadata && track.ProviderId is { } id
            && _catalogMetadataRetry.GetValueOrDefault(id) <= now && FollowedCatalogSource(id) is not null);
        if (pending?.ProviderId is { } provider) await EnrichCatalogBatchAsync(provider, _catalogStop.Token);
    }

    private async Task EnrichCatalogBatchAsync(string provider, CancellationToken token)
    {
        var plugin = FollowedCatalogSource(provider);
        if (plugin is null) return;
        var batch = State.Tracks.Where(track => track.ProviderId == provider && track.NeedsSourceMetadata && track.ProviderTrackId is not null)
            .Take(12).ToArray();
        if (batch.Length == 0) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var details = (await Plugins.ReadCatalogMetadataAsync(plugin, batch.Select(track => track.ProviderTrackId!).ToArray(), timeout.Token))
                .ToDictionary(track => track.Id, StringComparer.Ordinal);
            foreach (var track in batch)
            {
                if (details.TryGetValue(track.ProviderTrackId!, out var detail)) ApplySourceDetails(track, detail);
                track.NeedsSourceMetadata = false;
            }
            Save();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _catalogMetadataRetry[provider] = Environment.TickCount64 + 60_000;
            AppLog.Warning("MusicSource", "CLI 可选歌单元数据暂不可用。", error);
        }
    }

    private async Task<JsonObject> InvokeInteractiveExtensionAsync(PluginManifest plugin, string action, JsonObject values, bool confirmed, CancellationToken token)
    {
        _requestedCatalogs = [];
        try
        {
            var frame = await Plugins.InvokeExtensionAsync(plugin, action, values, token, confirmed);
            // RPC 完成后才读取歌单，避免在同一插件的回调中重入其会话锁。
            if (_requestedCatalogs.Count > 0)
            {
                var subscriptions = new JsonArray();
                foreach (var playlist in _requestedCatalogs) subscriptions.Add(await SyncCatalogAsync(playlist, token));
                await EnrichCatalogBatchAsync(plugin.Id, token);
                frame["subscriptions"] = subscriptions;
            }
            return frame;
        }
        finally { _requestedCatalogs = null; }
    }

    private JsonObject RequestCatalogFollow(PluginManifest plugin, JsonObject args, bool userGesture)
    {
        if (!userGesture || _requestedCatalogs is null || _requestedCatalogs.Count >= 4
            || args["operation"]?.GetValue<string>() != "follow-catalog" || !ReferenceEquals(FollowedCatalogSource(plugin.Id), plugin))
            return new() { ["success"] = false, ["reason"] = "Confirmed terminal action and media source permissions required." };
        var playlist = FollowCatalog(plugin, args["catalogId"]?.GetValue<string>() ?? "", args["name"]?.GetValue<string>() ?? plugin.Name);
        if (!_requestedCatalogs.Contains(playlist)) _requestedCatalogs.Add(playlist);
        return new() { ["success"] = true, ["pending"] = true, ["playlistId"] = playlist.Id };
    }
}
