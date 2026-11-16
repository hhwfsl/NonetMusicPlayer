using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Core.Streaming;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Plugins;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private LanLibraryServer? _lanServer;
    private string? _lanId;
    private LanCatalog _lanCatalog = new("", "", "", [], [], 0);
    private Dictionary<string, TrackItem> _lanLocal = new(StringComparer.Ordinal);
    private Dictionary<string, string> _lanPlaylistCovers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Url, long Until)> _lanPlaybackSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LanLibraryClient> _lanPeers = new(StringComparer.Ordinal);
    private (string Track, long Request, double Position)? _lanPlaybackResume;
    private readonly Dictionary<string, List<TrackItem>> _lanTracks = new(StringComparer.Ordinal);
    private bool _lanArtworkBusy;
    private readonly HashSet<string> _lanPlaylistCoverRequests = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lanAssetLifetime = new();
    private readonly SemaphoreSlim _lanAssetSlots = new(3);
    private sealed record PendingApproval(string Id, LanConnectionRequest Request, long Deadline, TaskCompletionSource<bool> Completion);
    private readonly Dictionary<string, PendingApproval> _lanApprovals = new(StringComparer.Ordinal);
    /// <summary>只公开请求标识、设备与剩余时间；终端和确认框共同完成同一个审批。</summary>
    public IEnumerable<(string Id, string Name, string Address, int Seconds)> LanPendingRequests => _lanApprovals.Values
        .Where(p => p.Deadline > Environment.TickCount64 && !p.Completion.Task.IsCompleted)
        .Select(p => (p.Id, p.Request.Name, p.Request.Address, (int)Math.Max(0, (p.Deadline - Environment.TickCount64 + 999) / 1000)));
    public event EventHandler? LanChanged;
    public event Action<string>? LanDisconnectNotice;
    public Func<LanConnectionRequest, CancellationToken, Task<bool>>? ConfirmLanConnection { get; set; }
    /// <summary>远端歌单是连接期间的只读导航投影，不写入本地歌单，也不会被再次共享。</summary>
    public IEnumerable<(string Page, string Name, string Source)> LanPlaylistNavigation => _lanPeers.Values.Where(p => p.Connected).SelectMany(peer =>
        peer.Catalog.Playlists.Select(list => ($"lan:{peer.DeviceId}:{list.Id}", list.Name,
            L10n.T(peer.Catalog.DeviceKind == "desktop" ? "Lan.SourceDesktop" : peer.Catalog.DeviceKind == "mobile" ? "Lan.SourceMobile" : "Lan.SourceDevice") + " · " + peer.Name)));
    public IReadOnlyCollection<LanLibraryClient> LanPeers => _lanPeers.Values;
    /// <summary>连接歌单只读投影；封面按可见卡片或详情页请求，不预下载整库。</summary>
    public LanPlaylist? LanPlaylistInfo(string page)
    {
        var parts = page.Split(':', 3);
        return parts.Length == 3 && _lanPeers.TryGetValue(parts[1], out var peer) ? peer.Catalog.Playlists.FirstOrDefault(p => p.Id == parts[2]) : null;
    }
    public string? LanPlaylistCoverPath(string page)
    {
        var parts = page.Split(':', 3); var list = LanPlaylistInfo(page);
        if (parts.Length != 3 || list?.Artwork != true || !_lanPeers.TryGetValue(parts[1], out var peer)) return null;
        var path = Path.Combine(Storage.ArtworkFolder, "lan", MusicLibraryScanner.StableId(peer.DeviceId + ":playlist:" + list.Id + ":" + list.ArtworkRevision) + ".image");
        if (File.Exists(path)) return path;
        if (_lanPlaylistCoverRequests.Add(path)) _ = LoadLanPlaylistCoverAsync(peer, list, path);
        return null;
    }
    private async Task LoadLanPlaylistCoverAsync(LanLibraryClient peer, LanPlaylist list, string path)
    {
        var acquired = false; var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await _lanAssetSlots.WaitAsync(_lanAssetLifetime.Token); acquired = true;
            if (_disposed || !_lanPeers.TryGetValue(peer.DeviceId, out var active) || !ReferenceEquals(peer, active)) return;
            var bytes = await peer.ReadPlaylistArtworkAsync(list.Id, _lanAssetLifetime.Token);
            if (_disposed || !_lanPeers.TryGetValue(peer.DeviceId, out var current) || !ReferenceEquals(peer, current)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temporary, bytes, _lanAssetLifetime.Token); File.Move(temporary, path, true);
            if (!_disposed && _lanPeers.TryGetValue(peer.DeviceId, out current) && ReferenceEquals(peer, current)) LanChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            if (acquired) _lanAssetSlots.Release();
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            _lanPlaylistCoverRequests.Remove(path);
        }
    }
    public bool LanSharing => _lanServer is not null;
    /// <summary>通用扩展仅获得歌曲引用，不获得配对邀请、会话令牌或媒体 URL。</summary>
    internal IReadOnlyList<TrackItem> LanPeerTracks(string peerId) => _lanTracks.GetValueOrDefault(peerId) ?? [];
    internal IEnumerable<TrackItem> CommandTracks => State.Tracks.Concat(State.RecentTemporaryTracks).Concat(_lanTracks.Values.SelectMany(t => t)).DistinctBy(t => t.Id);
    public string LanDeviceId
    {
        get
        {
            if (_lanId is not null) return _lanId;
            var path = Path.Combine(Storage.Root, "Network", "device-id"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _lanId = File.Exists(path) && LanAddress.IsHex(File.ReadAllText(path).Trim(), 32) ? File.ReadAllText(path).Trim() : Guid.NewGuid().ToString("N");
            AppStorage.AtomicWrite(path, _lanId); return _lanId;
        }
    }
    public string[] LanInvitations => _lanServer is null ? [] : NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(n => n.Address).Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) && LanAddress.IsPrivate(ip))
        .Distinct().Select(ip => _lanServer.Invitation(ip.ToString())).ToArray();
    /// <summary>共享由用户主动开启；退出后关闭监听，下次启动仍默认关闭。</summary>
    public void SetLanSharing(bool enabled)
    {
        try
        {
            if (enabled && _lanServer is null)
            {
                RefreshLocalLanCatalog(); var cert = LanLibraryServer.Certificate(Path.Combine(Storage.Root, "Network", "identity.pfx"));
                _lanServer = new(cert, LanDeviceId, Environment.MachineName, () => Volatile.Read(ref _lanCatalog), OpenLocalLanFileAsync,
                    AwaitLanApprovalAsync);
                _lanServer.OpenFileAsync = OpenLocalLanFileAsync;
                _lanServer.ApprovalWaitSeconds = () => Settings.LanApprovalWaitSeconds;
                _lanServer.AcceptReverse = async (access, token) =>
                {
                    var peer = await LanLibraryClient.FromApprovedAccessAsync(access, token);
                    await Dispatcher.UIThread.InvokeAsync(() => AttachLanPeer(peer)); return true;
                };
                _lanServer.PeerDisconnected += id => Dispatcher.UIThread.Post(() => { if (_lanPeers.TryGetValue(id, out var peer)) DisconnectLan(peer); });
                _lanServer.Start();
            }
            else if (!enabled) { foreach (var peer in _lanPeers.Values.ToArray()) DisconnectLan(peer); _lanServer?.Dispose(); _lanServer = null; _lanPlaybackSources.Clear(); }
            LanChanged?.Invoke(this, EventArgs.Empty);
            CompleteOperation("network.sharing", data: new System.Text.Json.Nodes.JsonObject { ["sharing"] = LanSharing });
        }
        catch (Exception e) { _lanServer?.Dispose(); _lanServer = null; ReportError(L10n.T("Lan.StartFailed"), e); LanChanged?.Invoke(this, EventArgs.Empty); }
    }
    public void RenewLanInvitation() { _lanServer?.RenewInvitation(); LanChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("network.renew"); }
    private async Task<bool> AwaitLanApprovalAsync(LanConnectionRequest request, CancellationToken token)
        => await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var pending = new PendingApproval(Guid.NewGuid().ToString("N"), request, Environment.TickCount64 + request.WaitSeconds * 1000L,
                new(TaskCreationOptions.RunContinuationsAsynchronously));
            _lanApprovals.Add(pending.Id, pending); LanChanged?.Invoke(this, EventArgs.Empty);
            using var prompt = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var cancel = token.Register(() => pending.Completion.TrySetResult(false));
            async Task ShowPrompt()
            {
                try { if (ConfirmLanConnection is not null) pending.Completion.TrySetResult(await ConfirmLanConnection(request, prompt.Token)); }
                catch (OperationCanceledException) { pending.Completion.TrySetResult(false); }
                catch (Exception error) { AppLog.Warning("LAN", "Connection prompt failed", error); pending.Completion.TrySetResult(false); }
            }
            _ = ShowPrompt();
            try
            {
                var accepted = await pending.Completion.Task && !token.IsCancellationRequested && Environment.TickCount64 <= pending.Deadline;
                CompleteOperation(accepted ? "network.accept" : "network.reject"); return accepted;
            }
            finally { prompt.Cancel(); _lanApprovals.Remove(pending.Id); LanChanged?.Invoke(this, EventArgs.Empty); }
        });
    public bool AnswerLanRequest(string id, bool accept)
    {
        if (!_lanApprovals.TryGetValue(id, out var pending) || pending.Deadline < Environment.TickCount64 || !pending.Completion.TrySetResult(accept)) return false;
        return true;
    }
    private void RefreshLocalLanCatalog()
    {
        var referenced = State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
        // 本机歌单中的插件引用也进入目录；其他设备引用不再次转发，避免设备间递归代理。
        var local = State.Tracks.Where(t => referenced.Contains(t.Id) && (t.ProviderId is null ? File.Exists(t.FilePath) : !t.ProviderId.StartsWith("lan:", StringComparison.Ordinal))).Take(20000).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var songs = local.Values.Select(t => new LanSong(t.Id, t.Title, t.Artist, t.Album, (long)(t.DurationSeconds * 1000), File.Exists(t.CoverPath) || t.ProviderId is not null, !t.LyricsDisabled) { ArtworkRevision = LanAssetRevision(t.CoverPath) }).ToArray();
        var covers = new Dictionary<string, string>(StringComparer.Ordinal);
        var lists = State.Playlists.Take(2000).Select(p =>
        {
            var path = GetPlaylistArtworkPath(p); var revision = LanAssetRevision(path);
            if (revision.Length > 0) covers[p.Id] = path!;
            return new LanPlaylist(p.Id, p.IsSystem ? L10n.T("Playlists.LikedSongs") : p.Name, p.Description, p.TrackIds.Where(local.ContainsKey).ToArray()) { Artwork = revision.Length > 0, ArtworkRevision = revision };
        }).ToArray();
        var catalog = new LanCatalog(LanDeviceId, Environment.MachineName, "", songs, lists, songs.Length, "desktop");
        catalog = catalog with { Revision = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(catalog, LanJson.Default.LanCatalog))) };
        Volatile.Write(ref _lanLocal, local); Volatile.Write(ref _lanPlaylistCovers, covers); Volatile.Write(ref _lanCatalog, catalog);
    }
    private static string LanAssetRevision(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { var file = new FileInfo(path); return file.Exists && file.Length is > 0 and <= 5_000_000 ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks))) : ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }
    private Task<LanFile?> OpenLocalLanFileAsync(string kind, string id) => OpenLocalLanFileAsync(kind, id, CancellationToken.None);
    private async Task<LanFile?> OpenLocalLanFileAsync(string kind, string id, CancellationToken token)
    {
        if (kind == "playlist-artwork") return Volatile.Read(ref _lanPlaylistCovers).TryGetValue(id, out var cover) ? await Task.Run(() => OpenLanAsset(cover, false), token) : null;
        if (!Volatile.Read(ref _lanLocal).TryGetValue(id, out var track)) return null;
        if (track.ProviderId is not null)
        {
            // 解析和插件状态属于界面线程；不改变本机播放器、播放队列或账户配置。
            var source = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var plugin = Plugins.Installed.FirstOrDefault(p => p.Id == track.ProviderId && p.Enabled && PluginManager.HasMediaSource(p));
                if (plugin is null) return null;
                if (kind != "audio") { await Plugins.EnrichSourceTrackAsync(track, Lyrics, token); return ""; }
                if (_lanPlaybackSources.TryGetValue(id, out var cached) && cached.Until > Environment.TickCount64) return cached.Url;
                var url = await Plugins.ResolveAsync(track, token);
                token.ThrowIfCancellationRequested();
                foreach (var key in _lanPlaybackSources.Where(p => p.Value.Until <= Environment.TickCount64).Select(p => p.Key).ToArray()) _lanPlaybackSources.Remove(key);
                if (_lanPlaybackSources.Count >= 64) _lanPlaybackSources.Clear();
                _lanPlaybackSources[id] = (url, Environment.TickCount64 + 30_000); return url;
            });
            if (source is null) return null;
            if (kind == "audio") return await Task.Run<LanFile?>(() =>
            {
                // 使用原有严格 loopback 验证及 Range 流，手机只看到电脑的已认证 HTTPS 音频端点。
                var stream = new LoopbackRangeStream(new Uri(source), token);
                return new(stream, stream.Length, "application/octet-stream");
            }, token);
        }
        return await Task.Run<LanFile?>(() =>
        {
        if (kind == "lyrics")
        {
            if (track.LyricsDisabled) return null; var text = Lyrics.ReadForTrack(track.Id, track.FilePath);
            if (string.IsNullOrWhiteSpace(text)) return null; var bytes = Encoding.UTF8.GetBytes(text); if (bytes.Length > 2_000_000) return null;
            return new(new MemoryStream(bytes, false), bytes.Length, "text/plain; charset=utf-8");
        }
        return OpenLanAsset(kind == "audio" ? track.FilePath : track.CoverPath, kind == "audio");
        }, token);
    }
    private static LanFile? OpenLanAsset(string? path, bool audio)
    {
        if (path is null || !File.Exists(path)) return null;
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (!audio && file.Length > 5_000_000) { file.Dispose(); return null; }
        return new(file, file.Length, audio ? "application/octet-stream" : path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
    }
    public async Task ConnectLanAsync(string invitation)
    {
        try
        {
            SetLanSharing(true); var local = _lanServer ?? throw new IOException("Local sharing unavailable");
            var peer = await LanLibraryClient.ConnectDuplexAsync(invitation, LanDeviceId, Environment.MachineName, true);
            try
            {
                if (!peer.DuplexSupported) throw new IOException(L10n.T("Lan.PeerUpgradeRequired"));
                var reverseToken = local.GrantAccess(peer.DeviceId, peer.Name);
                await peer.EstablishReverseAsync(new(local.Port, local.Fingerprint, reverseToken));
                AttachLanPeer(peer);
                CompleteOperation("network.connect");
            }
            catch { local.RevokeDevice(peer.DeviceId); await peer.NotifyDisconnectAsync(); peer.Dispose(); throw; }
        }
        catch (Exception e) { ReportError(L10n.T("Lan.ConnectFailed"), new IOException("Pairing or connection failed.", e)); }
    }
    private void AttachLanPeer(LanLibraryClient peer)
    {
        if (_disposed || !peer.Connected) { peer.Dispose(); return; }
        if (_lanPeers.TryGetValue(peer.DeviceId, out var previous)) previous.Dispose();
        _lanPeers[peer.DeviceId] = peer;
        peer.Changed += (_, _) => Dispatcher.UIThread.Post(() => RefreshLanPeer(peer));
        peer.Disconnected += (_, _) => Dispatcher.UIThread.Post(() => DisconnectLan(peer));
        peer.ReachabilityChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_lanPeers.TryGetValue(peer.DeviceId, out var current) || !ReferenceEquals(current, peer)) return;
            ObserveLanPlaybackReachability(peer);
            LanChanged?.Invoke(this, EventArgs.Empty);
        });
        RefreshLanPeer(peer);
    }
    /// <summary>网络暂停与坏音频分开：不跳过远端队列，恢复后续播；用户暂停/切歌可取消自动继续。</summary>
    private void ObserveLanPlaybackReachability(LanLibraryClient peer)
    {
        if (_loadingAudio || CurrentTrack?.ProviderId != "lan:" + peer.DeviceId) return;
        if (!peer.Reachable && IsPlaying && _audioLoaded)
        {
            UpdateListeningStatistics(); _lanPlaybackResume = (CurrentTrack.Id, Interlocked.Read(ref _playRequest), PlaybackPosition);
            _audio.Pause(); IsPlaying = false; _audioLoaded = false; _resumePending = true; Save();
        }
        else if (peer.Reachable && _lanPlaybackResume is { } pending && pending.Track == CurrentTrack.Id && pending.Request == Interlocked.Read(ref _playRequest))
        { _lanPlaybackResume = null; _ = ResumeLanPlaybackAsync(pending.Position); }
    }
    private async Task ResumeLanPlaybackAsync(double position)
    {
        try { await StartTrackAsync(CurrentTrack, _playingList.ToArray(), PlayingSourcePage, position, true); }
        catch (Exception error) { ReportError(L10n.T("Playback.UnableToChangePlaybackState"), error); }
    }
    private void RefreshLanPeer(LanLibraryClient peer)
    {
        if (!peer.Connected || _disposed) return;
        var stored = State.Tracks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _lanTracks[peer.DeviceId] = peer.Catalog.Tracks.Select(t =>
        {
            var id = "lan_" + MusicLibraryScanner.StableId(peer.DeviceId + ":" + t.Id);
            var track = stored.GetValueOrDefault(id) ?? new TrackItem(id, t.Title, t.Artist, t.Album, peer.TrackUri(t.Id), ".lan", 0)
            { DurationSeconds = t.DurationMs / 1000d, ProviderId = "lan:" + peer.DeviceId, ProviderTrackId = t.Id, CoverPath = File.Exists(LanArtworkPath(id)) ? LanArtworkPath(id) : null };
            track.SourceDeviceKind = peer.Catalog.DeviceKind; return track;
        }).ToList();
        ApplyFilter(); LanChanged?.Invoke(this, EventArgs.Empty);
    }
    public void DisconnectLan(LanLibraryClient peer)
    {
        if (_disposed || !_lanPeers.TryGetValue(peer.DeviceId, out var active) || !ReferenceEquals(active, peer)) return;
        _lanPeers.Remove(peer.DeviceId); _lanTracks.Remove(peer.DeviceId); _lanServer?.RevokeDevice(peer.DeviceId);
        if (CurrentTrack?.ProviderId == "lan:" + peer.DeviceId) _lanPlaybackResume = null;
        _ = Task.Run(async () => { await peer.NotifyDisconnectAsync(); peer.Dispose(); });
        var provider = "lan:" + peer.DeviceId;
        if (CurrentTrack?.ProviderId == provider) { _audio.Pause(); IsPlaying = false; _audioLoaded = false; _resumePending = true; }
        var retained = State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
        var removed = State.RecentTemporaryTracks.Where(t => t.ProviderId == provider && !retained.Contains(t.Id)).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        State.RecentTemporaryTracks.RemoveAll(t => removed.Contains(t.Id)); State.History.RemoveAll(removed.Contains);
        _playingList.RemoveAll(t => t.ProviderId == provider && !retained.Contains(t.Id));
        ApplyFilter(); Save(); LanChanged?.Invoke(this, EventArgs.Empty); ReportWarning(peer.Name + " · " + L10n.T("Lan.Disconnected")); CompleteOperation("network.disconnect");
        LanDisconnectNotice?.Invoke(peer.Name + " · " + L10n.T("Lan.Disconnected"));
    }
    private IEnumerable<TrackItem> LanSourceTracks(string page)
    {
        var parts = page.Split(':', 3);
        if (parts.Length != 3 || !_lanPeers.TryGetValue(parts[1], out var peer) || !_lanTracks.TryGetValue(parts[1], out var tracks)) return [];
        var list = peer.Catalog.Playlists.FirstOrDefault(p => p.Id == parts[2]); if (list is null) return [];
        var byRemoteId = tracks.ToDictionary(t => t.ProviderTrackId!, StringComparer.Ordinal); return list.TrackIds.Select(id => byRemoteId.GetValueOrDefault(id)).OfType<TrackItem>();
    }
    private string LanArtworkPath(string id) => Path.Combine(Storage.Root, "Artwork", "lan", MusicLibraryScanner.StableId(id) + ".jpg");
    private async Task PrepareLanTrackAsync(TrackItem track, bool lyrics = true, CancellationToken token = default)
    {
        if (track.ProviderId is null || !_lanPeers.TryGetValue(track.ProviderId[4..], out var peer)) throw new IOException(L10n.T("Lan.Disconnected"));
        var description = peer.Catalog.Tracks.FirstOrDefault(t => t.Id == track.ProviderTrackId);
        if (track.CoverPath is null && description?.Artwork == true)
        {
            try { var image = await peer.ReadAssetAsync("artwork", track.ProviderTrackId!, token); var path = LanArtworkPath(track.Id); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, image, token); track.CoverPath = path; track.RefreshArtwork(); }
            catch (Exception e) when (e is HttpRequestException or IOException) { }
        }
        if (lyrics && !track.LyricsDisabled && description?.Lyrics == true && Lyrics.ExistingPath(track.Id) is null)
        {
            try { var text = Encoding.UTF8.GetString(await peer.ReadAssetAsync("lyrics", track.ProviderTrackId!, token)); Directory.CreateDirectory(Lyrics.Folder); await File.WriteAllTextAsync(Lyrics.PathFor(track.Id), text, token); }
            catch (Exception e) when (e is HttpRequestException or IOException) { }
        }
    }
    public async Task LoadLanArtworkAsync(IEnumerable<TrackItem> tracks)
    {
        if (_lanArtworkBusy) return; _lanArtworkBusy = true;
        try { foreach (var track in tracks.Where(t => t.ProviderId?.StartsWith("lan:", StringComparison.Ordinal) == true && t.CoverPath is null).Take(12)) await PrepareLanTrackAsync(track, false); }
        catch (Exception e) when (e is IOException or HttpRequestException or ObjectDisposedException) { }
        finally { _lanArtworkBusy = false; }
    }
    private void CloseLan()
    {
        _lanAssetLifetime.Cancel(); _lanPlaybackSources.Clear();
        var peers = _lanPeers.Values.ToArray();
        // 正常退出有界通知对端；异常断电只能进入不可达状态，不能与系统暂停网络混为一谈。
        try { Task.WhenAll(peers.Select(p => p.NotifyDisconnectAsync())).Wait(TimeSpan.FromMilliseconds(1400)); } catch (Exception) { }
        _lanServer?.Dispose(); _lanServer = null; foreach (var peer in peers) peer.Dispose(); _lanPeers.Clear(); _lanTracks.Clear();
    }
}
