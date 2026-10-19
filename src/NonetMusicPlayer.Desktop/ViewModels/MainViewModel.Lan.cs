using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private LanLibraryServer? _lanServer;
    private string? _lanId;
    private LanCatalog _lanCatalog = new("", "", "", [], [], 0);
    private Dictionary<string, TrackItem> _lanLocal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LanLibraryClient> _lanPeers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TrackItem>> _lanTracks = new(StringComparer.Ordinal);
    private bool _lanArtworkBusy;
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
                _lanServer.ApprovalWaitSeconds = () => Settings.LanApprovalWaitSeconds;
                _lanServer.AcceptReverse = async (access, token) =>
                {
                    var peer = await LanLibraryClient.FromApprovedAccessAsync(access, token);
                    await Dispatcher.UIThread.InvokeAsync(() => AttachLanPeer(peer)); return true;
                };
                _lanServer.PeerDisconnected += id => Dispatcher.UIThread.Post(() => { if (_lanPeers.TryGetValue(id, out var peer)) DisconnectLan(peer); });
                _lanServer.Start();
            }
            else if (!enabled) { foreach (var peer in _lanPeers.Values.ToArray()) DisconnectLan(peer); _lanServer?.Dispose(); _lanServer = null; }
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
        var local = State.Tracks.Where(t => t.ProviderId is null && referenced.Contains(t.Id) && File.Exists(t.FilePath)).Take(20000).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var songs = local.Values.Select(t => new LanSong(t.Id, t.Title, t.Artist, t.Album, (long)(t.DurationSeconds * 1000), File.Exists(t.CoverPath), !t.LyricsDisabled)).ToArray();
        var lists = State.Playlists.Take(2000).Select(p => new LanPlaylist(p.Id, p.IsSystem ? L10n.T("Playlists.LikedSongs") : p.Name, p.Description, p.TrackIds.Where(local.ContainsKey).ToArray())).ToArray();
        var catalog = new LanCatalog(LanDeviceId, Environment.MachineName, "", songs, lists, songs.Length, "desktop");
        catalog = catalog with { Revision = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(catalog, LanJson.Default.LanCatalog))) };
        Volatile.Write(ref _lanLocal, local); Volatile.Write(ref _lanCatalog, catalog);
    }
    private Task<LanFile?> OpenLocalLanFileAsync(string kind, string id) => Task.Run<LanFile?>(() =>
    {
        if (!Volatile.Read(ref _lanLocal).TryGetValue(id, out var track)) return null;
        if (kind == "lyrics")
        {
            if (track.LyricsDisabled) return null; var text = Lyrics.ReadForTrack(track.Id, track.FilePath);
            if (string.IsNullOrWhiteSpace(text)) return null; var bytes = Encoding.UTF8.GetBytes(text); if (bytes.Length > 2_000_000) return null;
            return new(new MemoryStream(bytes, false), bytes.Length, "text/plain; charset=utf-8");
        }
        var path = kind == "audio" ? track.FilePath : track.CoverPath; if (path is null || !File.Exists(path)) return null;
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (kind == "artwork" && file.Length > 5_000_000) { file.Dispose(); return null; }
        return new(file, file.Length, kind == "audio" ? "application/octet-stream" : path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
    });
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
        RefreshLanPeer(peer);
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
    private async Task PrepareLanTrackAsync(TrackItem track, bool lyrics = true)
    {
        if (track.ProviderId is null || !_lanPeers.TryGetValue(track.ProviderId[4..], out var peer)) throw new IOException(L10n.T("Lan.Disconnected"));
        if (track.CoverPath is null)
        {
            try { var image = await peer.ReadAssetAsync("artwork", track.ProviderTrackId!); var path = LanArtworkPath(track.Id); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, image); track.CoverPath = path; track.RefreshArtwork(); }
            catch (Exception e) when (e is HttpRequestException or IOException) { }
        }
        if (lyrics && !track.LyricsDisabled && Lyrics.ExistingPath(track.Id) is null)
        {
            try { var text = Encoding.UTF8.GetString(await peer.ReadAssetAsync("lyrics", track.ProviderTrackId!)); Directory.CreateDirectory(Lyrics.Folder); await File.WriteAllTextAsync(Lyrics.PathFor(track.Id), text); }
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
        var peers = _lanPeers.Values.ToArray();
        // 正常退出最多等待短暂发送窗口；异常断电由对端五秒心跳检测，不阻塞无限退出。
        try { Task.WhenAll(peers.Select(p => p.NotifyDisconnectAsync())).Wait(TimeSpan.FromMilliseconds(1400)); } catch (Exception) { }
        _lanServer?.Dispose(); _lanServer = null; foreach (var peer in peers) peer.Dispose(); _lanPeers.Clear(); _lanTracks.Clear();
    }
}
