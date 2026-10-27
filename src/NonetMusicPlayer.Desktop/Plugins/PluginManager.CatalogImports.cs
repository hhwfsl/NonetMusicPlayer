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

    /// <summary>先建立目标歌单，轻量目录到达后一次加入引用，再按小批次补全信息。</summary>
    public CatalogImportJob BeginCatalogImport(PluginManifest plugin, string catalogId, string name, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess(); RequireWritable(); RequireInstalled(plugin);
        if (!plugin.Enabled || !HasMediaSource(plugin) || !plugin.Permissions.Contains("library-write")) throw new InvalidOperationException("Music import unavailable.");
        if (catalogId.Length is 0 or > 300 || string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid catalog import.");
        if (_catalogImports.Count >= 4) throw new InvalidOperationException(L10n.T("Common.AnImportIsInProgressWaitForItTo"));
        var vm = _uiVm ?? throw new InvalidOperationException("Host unavailable.");
        var job = new CatalogImportJob(Guid.NewGuid().ToString("N"), vm.CreatePlaylist(name.Length > 80 ? name[..79] + "…" : name));
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); _catalogImports.Add(job.Id, cancellation);
        // 不继承当前 RPC 的重入标记；后台服务必须正常获取会话锁，不能与插件代码并发调用。
        using (ExecutionContext.SuppressFlow()) _ = Task.Run(async () =>
            await Dispatcher.UIThread.InvokeAsync(() => RunCatalogImportAsync(plugin, catalogId, job, cancellation)));
        return job;
    }

    private async Task RunCatalogImportAsync(PluginManifest plugin, string catalogId, CatalogImportJob job, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            var tracks = await ReadMusicCatalogAsync(plugin, catalogId, token, includeArtwork: false);
            token.ThrowIfCancellationRequested();
            var vm = _uiVm;
            if (vm is null || !vm.State.Playlists.Contains(job.Playlist)) return;
            if (tracks.Count == 0) { vm.ReportWarning(L10n.T("MusicSearch.Empty")); return; }
            vm.AddToPlaylist(job.Playlist, tracks); job.ImportedCount = tracks.Count;
            AppLog.Info("MusicSource", $"Catalog references imported: {plugin.Id}; count={tracks.Count}");
            var lastSave = Environment.TickCount64;
            // 先导入全部引用；每批最多十二首，留出机会处理播放、登录及其他页面操作。
            foreach (var batch in tracks.Chunk(12))
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
            if (_uiVm == vm)
            {
                vm.Save();
                if (job.MetadataFailures > 0) vm.ReportWarning(L10n.Format("MusicSearch.MetadataPartial", job.MetadataFailures));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { _uiVm?.ReportError(L10n.T("MusicSearch.ImportFailed"), error); }
        finally
        {
            _catalogImports.Remove(job.Id); cancellation.Dispose(); job.Done.TrySetResult();
        }
    }
    private void CancelCatalogImports() { foreach (var item in _catalogImports.Values) item.Cancel(); }
}