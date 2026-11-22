using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>导入事务与插件页面独立；Completion 包含后台信息补全，关闭页面不会取消它。</summary>
public sealed class CatalogImportJob(string id, Playlist playlist)
{
    public string Id { get; } = id;
    public Playlist Playlist { get; } = playlist;
    public int ImportedCount { get; internal set; }
    public int MetadataFailures { get; internal set; }
    internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => Done.Task;
}

public sealed partial class PluginManager
{
    private readonly Dictionary<string, CancellationTokenSource> _catalogImports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogImportJob> _followedImports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _nextCatalogSync = new(StringComparer.Ordinal);
    private DispatcherTimer? _catalogSyncTimer;

    /// <summary>先建立目标歌单，轻量目录到达后一次加入引用，再按小批次补全信息。</summary>
    public CatalogImportJob BeginCatalogImport(PluginManifest plugin, string catalogId, string name, CancellationToken token = default, bool follow = false)
    {
        Dispatcher.UIThread.VerifyAccess(); RequireWritable(); RequireInstalled(plugin);
        if (!plugin.Enabled || !HasMediaSource(plugin) || !plugin.Permissions.Contains("library-write")) throw new InvalidOperationException("Music import unavailable.");
        if (catalogId.Length is 0 or > 300 || catalogId.Any(char.IsControl) || string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid catalog import.");
        if (_catalogImports.Count >= 4) throw new InvalidOperationException(L10n.T("Common.AnImportIsInProgressWaitForItTo"));
        var vm = _uiVm ?? throw new InvalidOperationException("Host unavailable.");
        var title = name.Trim(); if (title.Length > 80) title = title[..79] + "…";
        var playlist = follow ? vm.FollowCatalog(plugin.Id, catalogId, title) : vm.CreatePlaylist(title);
        return StartCatalogImport(plugin, catalogId, playlist, token, automatic: false);
    }

    private CatalogImportJob StartCatalogImport(PluginManifest plugin, string catalogId, Playlist playlist, CancellationToken token, bool automatic)
    {
        if (_followedImports.TryGetValue(playlist.Id, out var active)) return active;
        var job = new CatalogImportJob(Guid.NewGuid().ToString("N"), playlist);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); _catalogImports.Add(job.Id, cancellation);
        if (playlist.IsSourceLinked)
        {
            _followedImports.Add(playlist.Id, job);
            _nextCatalogSync[playlist.Id] = Environment.TickCount64 + 5 * 60_000;
        }
        // 不继承当前 RPC 的重入标记；后台服务必须正常获取会话锁，不能与插件代码并发调用。
        using (ExecutionContext.SuppressFlow()) _ = Task.Run(async () =>
            await Dispatcher.UIThread.InvokeAsync(() => RunCatalogImportAsync(plugin, catalogId, job, cancellation, automatic)));
        return job;
    }

    private void StartFollowedCatalogSync()
    {
        if (_catalogSyncTimer is not null) return;
        _catalogSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _catalogSyncTimer.Tick += (_, _) => SyncDueCatalogs();
        _catalogSyncTimer.Start();
    }
    private void StopFollowedCatalogSync()
    {
        _catalogSyncTimer?.Stop(); _catalogSyncTimer = null; _nextCatalogSync.Clear();
        CancelCatalogImports();
    }
    private void SyncDueCatalogs()
    {
        if (_uiVm is not { } vm || _followedImports.Count != 0 || _catalogImports.Count >= 4 || _storage.IsMigrating) return;
        var now = Environment.TickCount64;
        var playlist = vm.State.Playlists.Where(p => p.IsSourceLinked && _nextCatalogSync.GetValueOrDefault(p.Id) <= now)
            .OrderBy(p => _nextCatalogSync.GetValueOrDefault(p.Id)).FirstOrDefault(p => FindCatalogSource(p) is not null);
        if (playlist is not null) _ = RefreshFollowedPlaylistAsync(playlist, automatic: true);
    }
    public Task RefreshFollowedPlaylistAsync(Playlist playlist) => RefreshFollowedPlaylistAsync(playlist, automatic: false);
    private Task RefreshFollowedPlaylistAsync(Playlist playlist, bool automatic)
    {
        if (_uiVm is not { } vm || !playlist.IsSourceLinked || !vm.State.Playlists.Contains(playlist)) return Task.CompletedTask;
        if (_followedImports.TryGetValue(playlist.Id, out var active)) return active.Completion;
        if (_catalogImports.Count >= 4) return Task.CompletedTask;
        if (FindCatalogSource(playlist) is not { } plugin)
        {
            if (!automatic) vm.ReportWarning(L10n.T("MusicSearch.CatalogSourceUnavailable"));
            return Task.CompletedTask;
        }
        RequireWritable();
        return StartCatalogImport(plugin, playlist.SourceCatalogId!, playlist, default, automatic).Completion;
    }
    private PluginManifest? FindCatalogSource(Playlist playlist) => Installed.FirstOrDefault(plugin => plugin.Id == playlist.SourcePluginId
        && plugin.Enabled && HasMediaSource(plugin) && plugin.Permissions.Contains("library-write") && plugin.Permissions.Contains("network"));

    private async Task RunCatalogImportAsync(PluginManifest plugin, string catalogId, CatalogImportJob job, CancellationTokenSource cancellation, bool automatic)
    {
        var token = cancellation.Token;
        try
        {
            var followed = job.Playlist.IsSourceLinked;
            var (tracks, name, unavailable) = await ReadMusicCatalogAsync(plugin, catalogId, token, includeArtwork: false, followed: followed);
            token.ThrowIfCancellationRequested();
            var vm = _uiVm;
            if (vm is null || _storage.IsMigrating || !vm.State.Playlists.Contains(job.Playlist) || !plugin.Enabled || !Installed.Contains(plugin)) return;
            job.ImportedCount = tracks.Count;
            var enrich = tracks;
            if (followed)
            {
                // 同步只在所有分页通过验证后替换歌单，后台补全仅针对新曲目。
                var previous = job.Playlist.TrackIds.ToHashSet(StringComparer.Ordinal);
                vm.ApplyFollowedCatalog(job.Playlist, tracks, name);
                enrich = tracks.Where(track => !previous.Contains(track.Id)).ToArray();
                if (!automatic)
                {
                    vm.StatusText = L10n.Format("Common.SourceUpdatedSongs", tracks.Count);
                    if (unavailable > 0) vm.ReportWarning(L10n.Format("MusicSearch.CatalogUnavailableTracks", unavailable));
                }
            }
            else
            {
                if (tracks.Count == 0) { vm.ReportWarning(L10n.T("MusicSearch.Empty")); return; }
                vm.AddToPlaylist(job.Playlist, tracks);
            }
            AppLog.Info("MusicSource", $"Catalog references imported: {plugin.Id}; count={tracks.Count}");
            var lastSave = Environment.TickCount64;
            // 先导入全部引用；每批最多十二首，留出机会处理播放、登录及其他页面操作。
            foreach (var batch in enrich.Chunk(12))
            {
                token.ThrowIfCancellationRequested();
                if (_uiVm != vm || !plugin.Enabled || !vm.State.Playlists.Contains(job.Playlist)) break;
                var currentIds = vm.State.Tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                var active = batch.Where(t => currentIds.Contains(t.Id)).ToArray(); if (active.Length == 0) continue;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    ProviderTrack[] details;
                    if (plugin.RequiredCapabilities.Contains("media.artwork-batch.v1"))
                    {
                        var ids = JsonSerializer.Serialize(active.Select(t => t.ProviderTrackId).ToArray());
                        var answer = await CallMediaAsync(plugin, "music.artwork", new() { ["trackIds"] = ids }, timeout.Token);
                        var page = answer.Deserialize(MusicSourceJson.Default.MusicSearchPage) ?? throw new InvalidDataException("Empty metadata batch.");
                        MusicSearchContract.ValidatePage(page, active.Length);
                        if (page.Tracks.Any(t => !active.Any(a => a.ProviderTrackId == t.Id))) throw new InvalidDataException("Unexpected metadata identity.");
                        details = page.Tracks.ToArray();
                    }
                    else
                    {
                        var list = new List<ProviderTrack>();
                        foreach (var track in active)
                        {
                            var answer = await CallMediaAsync(plugin, "music.describe", new() { ["trackId"] = track.ProviderTrackId! }, timeout.Token);
                            var item = answer.Deserialize(MusicSourceJson.Default.ProviderTrack) ?? throw new InvalidDataException("Empty metadata.");
                            if (item.Id != track.ProviderTrackId) throw new InvalidDataException("Unexpected metadata identity.");
                            list.Add(item);
                        }
                        details = list.ToArray();
                    }
                    token.ThrowIfCancellationRequested();
                    var enriched = details.Select(item => CreateSourceTrack(plugin, item)).ToArray();
                    await PersistCatalogMetadataAsync(plugin, details, enriched, token);
                    if (_uiVm != vm) break;
                    var stored = new Dictionary<string, TrackItem>(StringComparer.Ordinal);
                    foreach (var track in vm.State.Tracks) stored.TryAdd(track.Id, track);
                    foreach (var detail in enriched)
                    {
                        // 用户在后台任务期间移除了引用时，不重新插入它，也不覆盖自定义封面。
                        if (!stored.TryGetValue(detail.Id, out var track) || track.ProviderId != plugin.Id) continue;
                        track.Title = detail.Title;
                        if (!string.IsNullOrWhiteSpace(detail.Artist)) track.Artist = detail.Artist;
                        if (!string.IsNullOrWhiteSpace(detail.Album)) track.Album = detail.Album;
                        if (detail.DurationSeconds > 0) track.DurationSeconds = detail.DurationSeconds;
                        if (track.CoverPath is null && detail.CoverPath is not null) { track.CoverPath = detail.CoverPath; track.RefreshArtwork(); }
                        track.RefreshMetadata();
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    job.MetadataFailures += active.Length;
                    AppLog.Warning("MusicSource", "Background metadata unavailable: " + plugin.Id, error);
                }
                if (Environment.TickCount64 - lastSave >= 5000) { vm.Save(); lastSave = Environment.TickCount64; }
                await Task.Delay(50, token);
            }
            if (_uiVm == vm && enrich.Count > 0)
            {
                vm.Save();
                if (!automatic && job.MetadataFailures > 0) vm.ReportWarning(L10n.Format("MusicSearch.MetadataPartial", job.MetadataFailures));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (automatic) AppLog.Warning("MusicSource", "Followed catalog refresh failed: " + plugin.Id, error);
            else _uiVm?.ReportError(L10n.T(job.Playlist.IsSourceLinked ? "MusicSearch.CatalogSyncFailed" : "MusicSearch.ImportFailed"), error);
        }
        finally
        {
            _catalogImports.Remove(job.Id);
            if (_followedImports.GetValueOrDefault(job.Playlist.Id) == job) _followedImports.Remove(job.Playlist.Id);
            cancellation.Dispose(); job.Done.TrySetResult();
        }
    }
    private void CancelCatalogImports() { foreach (var item in _catalogImports.Values) item.Cancel(); }
}
