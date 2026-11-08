using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Core.Network;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private LanLibraryServer? _lanServer;
    private X509Certificate2? _lanCertificate;
    private readonly ConcurrentDictionary<string, LanLibraryClient> _lanPeers = [];
    private readonly ConcurrentDictionary<string, StoredMusicTrack> _lanTracks = [];
    private readonly ConcurrentDictionary<string, (LanConnectionRequest Request, TaskCompletionSource<bool> Answer, DateTimeOffset Expires)> _lanApprovals = [];
    private LanCatalog _lanCatalog = new("", "", "", [], [], 0, "desktop");
    private IReadOnlyDictionary<string, string> _lanFiles = new Dictionary<string, string>();
    private string LanName => Environment.MachineName + " CLI";
    private (string Id, double Position)? _lanResume;

    private async Task<(JsonNode? Data, bool Save)> ExecuteNetworkAsync(PlayerCommand command, CancellationToken token)
    {
        string Arg(int n) => CommandResults.Required(command, n);
        LanLibraryClient Peer() => _lanPeers.TryGetValue(Arg(0), out var peer) ? peer : throw new InvalidDataException("Unknown paired device.");
        switch (command.Name)
        {
            case "network.status": return (new JsonObject { ["sharing"] = _lanServer is not null, ["connectedDevices"] = _lanPeers.Count, ["pendingRequests"] = _lanApprovals.Count, ["approvalWaitSeconds"] = State.Settings.LanApprovalWaitSeconds }, false);
            case "network.sharing": if (Arg(0) == "on") StartNetwork(); else if (Arg(0) == "off") StopNetwork(); else throw new FormatException("on | off"); return (null, false);
            case "network.invitation":
                if (_lanServer is null) throw new InvalidOperationException("Enable sharing first.");
                var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork && LanAddress.IsPrivate(a)).Select(a => a.ToString()).Distinct().ToArray();
                return (new JsonObject { ["invitations"] = Strings(addresses.Append("127.0.0.1").Distinct().Select(_lanServer.Invitation)) }, false);
            case "network.renew": if (_lanServer is null) throw new InvalidOperationException("Enable sharing first."); _lanServer.RenewInvitation(); return (null, false);
            case "network.requests": return (new JsonArray(_lanApprovals.Select(p => (JsonNode)new JsonObject { ["id"] = p.Key, ["name"] = p.Value.Request.Name, ["address"] = p.Value.Request.Address, ["secondsRemaining"] = Math.Max(0, (int)(p.Value.Expires - DateTimeOffset.UtcNow).TotalSeconds) }).ToArray()), false);
            case "network.accept": case "network.reject":
                if (!_lanApprovals.TryGetValue(Arg(0), out var request) || request.Expires <= DateTimeOffset.UtcNow || !request.Answer.TrySetResult(command.Name == "network.accept")) throw new InvalidOperationException("Pairing request expired."); return (null, false);
            case "network.connect":
                StartNetwork(); var connected = await LanLibraryClient.ConnectDuplexAsync(Arg(0), State.DeviceId, LanName, true, token);
                try
                {
                    AddPeer(connected);
                    if (connected.DuplexSupported) await connected.EstablishReverseAsync(new(_lanServer!.Port, _lanServer.Fingerprint, _lanServer.GrantAccess(connected.DeviceId, connected.Name)), token);
                }
                catch { _lanPeers.TryRemove(connected.DeviceId, out _); connected.Dispose(); throw; }
                return (new JsonObject { ["id"] = connected.DeviceId, ["name"] = connected.Name }, false);
            case "network.peers": return (new JsonArray(_lanPeers.Values.Select(p => (JsonNode)new JsonObject { ["id"] = p.DeviceId, ["name"] = p.Name, ["deviceKind"] = p.Catalog.DeviceKind, ["reachable"] = p.Reachable, ["connectionState"] = p.Reachable ? "available" : "reconnecting", ["tracks"] = p.Catalog.Total }).ToArray()), false);
            case "network.tracks": return (TracksJson(_lanTracks.Values.Where(t => t.ProviderId == "lan:" + Peer().DeviceId)), false);
            case "network.playlists": return (new JsonArray(Peer().Catalog.Playlists.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["count"] = p.TrackIds.Length }).ToArray()), false);
            case "network.play":
                var remote = Peer(); var ids = command.Arguments.Count > 1 ? remote.Catalog.Playlists.FirstOrDefault(p => p.Id == Arg(1))?.TrackIds ?? throw new InvalidDataException("Unknown remote playlist.") : remote.Catalog.Tracks.Select(t => t.Id).ToArray();
                var tracks = ids.Select(id => _lanTracks.GetValueOrDefault("lan:" + remote.DeviceId + ":" + id)).OfType<StoredMusicTrack>().ToArray();
                var selected = command.Arguments.Count > 2 ? tracks.FirstOrDefault(t => t.Id == Arg(2) || t.ProviderTrackId == Arg(2)) : tracks.FirstOrDefault();
                if (selected is null) throw new InvalidOperationException("Remote playlist is empty or selected track was not found.");
                State.LastPlaylistId = null; SelectQueue(tracks, selected); _failed.Clear(); await StartAsync(selected, 0, token); return (TracksJson(_queue), true);
            case "network.disconnect":
                var peers = _lanPeers.Values.Where(p => Arg(0) == "all" || p.DeviceId == Arg(0)).ToArray();
                if (peers.Length == 0) throw new InvalidDataException("Unknown paired device.");
                foreach (var peer in peers) { try { await peer.NotifyDisconnectAsync(); } catch { } RemovePeer(peer); } return (null, false);
            default: throw new PlatformNotSupportedException(Localization.LocalizationCatalog.Get("Commands.HostUnsupported"));
        }
    }
    private void StartNetwork()
    {
        if (_lanServer is not null) return;
        RefreshLanSnapshot(); _lanCertificate = LanLibraryServer.Certificate(Path.Combine(DataDirectory, "lan-identity.pfx"));
        var server = new LanLibraryServer(_lanCertificate, State.DeviceId, LanName, () => Volatile.Read(ref _lanCatalog), (kind, id) =>
        {
            var files = Volatile.Read(ref _lanFiles);
            if (!files.TryGetValue(kind + ":" + id, out var path) || !File.Exists(path)) return Task.FromResult<LanFile?>(null);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Task.FromResult<LanFile?>(new(stream, stream.Length, "application/octet-stream"));
        }, async (request, token) =>
        {
            var id = Guid.NewGuid().ToString("N"); var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _lanApprovals[id] = (request, answer, DateTimeOffset.UtcNow.AddSeconds(State.Settings.LanApprovalWaitSeconds));
            Publish(CommandResults.Completed("network.requests", new JsonObject { ["id"] = id, ["name"] = request.Name, ["address"] = request.Address }));
            try { return await answer.Task.WaitAsync(TimeSpan.FromSeconds(State.Settings.LanApprovalWaitSeconds), token); }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException) { return false; }
            finally { _lanApprovals.TryRemove(id, out _); }
        });
        server.ApprovalWaitSeconds = () => State.Settings.LanApprovalWaitSeconds;
        server.AcceptReverse = async (access, token) => { if (_disposed) return false; AddPeer(await LanLibraryClient.FromApprovedAccessAsync(access, token)); return true; };
        server.PeerDisconnected += id => { if (_lanPeers.TryGetValue(id, out var peer)) RemovePeer(peer); };
        server.Start(); _lanServer = server;
    }
    private void RefreshLanSnapshot()
    {
        var referenced = State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
        var tracks = State.Tracks.Where(t => t.ProviderId is null && referenced.Contains(t.Id) && File.Exists(t.Metadata.FilePath)).Take(20000).ToArray();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            files["audio:" + track.Id] = track.Metadata.FilePath;
            if (track.Metadata.CoverPath is { } cover && File.Exists(cover)) files["artwork:" + track.Id] = cover;
            if (!State.DisabledLyrics.Contains(track.Id)) foreach (var extension in new[] { ".lrc", ".txt" }) { var path = Path.Combine(LyricsDirectory, LyricFileId(track) + extension); if (File.Exists(path)) { files["lyrics:" + track.Id] = path; break; } }
        }
        var included = tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var catalog = new LanCatalog(State.DeviceId, LanName, Guid.NewGuid().ToString("N"), tracks.Select(t => new LanSong(t.Id, t.Metadata.Title, t.Metadata.Artist, t.Metadata.Album, (long)(t.Metadata.DurationSeconds * 1000), files.ContainsKey("artwork:" + t.Id), files.ContainsKey("lyrics:" + t.Id))).ToArray(),
            State.Playlists.Select(p => new LanPlaylist(p.Id, p.Name, p.Description, p.TrackIds.Where(included.Contains).ToArray())).ToArray(), tracks.Length, "desktop");
        // 播放状态落盘不应刷新目录修订号，避免远端分页在切歌时不断失效。
        catalog = catalog with { Revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(catalog with { Revision = "" }, Persistence.CoreJson.Options))) };
        Volatile.Write(ref _lanFiles, files); Volatile.Write(ref _lanCatalog, catalog);
    }
    private void AddPeer(LanLibraryClient peer)
    {
        if (_disposed) { peer.Dispose(); return; }
        if (_lanPeers.TryGetValue(peer.DeviceId, out var old) && !ReferenceEquals(old, peer)) old.Dispose();
        _lanPeers[peer.DeviceId] = peer;
        void Refresh()
        {
            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in peer.Catalog.Tracks)
            {
                var id = "lan:" + peer.DeviceId + ":" + track.Id; existing.Add(id);
                _lanTracks[id] = new() { ProviderId = "lan:" + peer.DeviceId, ProviderTrackId = track.Id,
                    Metadata = new MusicMetadata(id, track.Title, track.Artist, track.Album, peer.TrackUri(track.Id), ".remote", 0, track.DurationMs / 1000d, null) };
            }
            foreach (var entry in _lanTracks.Where(p => p.Value.ProviderId == "lan:" + peer.DeviceId && !existing.Contains(p.Key))) _lanTracks.TryRemove(entry.Key, out _);
        }
        peer.Changed += (_, _) => { Refresh(); _ = ObserveLanPlaybackAsync(peer, false); }; peer.Disconnected += (_, _) => RemovePeer(peer); Refresh();
    }
    private void RemovePeer(LanLibraryClient peer)
    {
        if (!_lanPeers.TryGetValue(peer.DeviceId, out var current) || !ReferenceEquals(current, peer)) return;
        _lanPeers.TryRemove(peer.DeviceId, out _); _lanServer?.RevokeDevice(peer.DeviceId); peer.Dispose();
        foreach (var entry in _lanTracks.Where(p => p.Value.ProviderId == "lan:" + peer.DeviceId)) _lanTracks.TryRemove(entry.Key, out _);
        _ = ObserveLanPlaybackAsync(peer, true);
    }
    /// <summary>暂时断网保留当前队列和位置；只有未发生用户操作时才在恢复后续播。</summary>
    private async Task ObserveLanPlaybackAsync(LanLibraryClient peer, bool disconnected)
    {
        if (_disposed) return; await _gate.WaitAsync();
        try
        {
            if (_disposed || Current?.ProviderId != "lan:" + peer.DeviceId) return;
            if (disconnected || !peer.Reachable)
            {
                if (_loaded && _audio.IsPlaying) { AccountListening(); var position = _audio.Position.TotalSeconds; _audio.Pause(); _loaded = false; State.LastPosition = position; _lanResume = disconnected ? null : (Current.Id, position); Save(); }
            }
            else if (_lanResume is { } pending && pending.Id == Current.Id) { _lanResume = null; await StartAsync(Current, pending.Position, CancellationToken.None, false); }
        }
        catch (Exception error) { Diagnostics.AppLog.Warning("LAN", "CLI 网络音频恢复失败", error); }
        finally { _gate.Release(); }
    }
    private void StopNetwork()
    {
        foreach (var request in _lanApprovals.Values) request.Answer.TrySetResult(false); _lanApprovals.Clear();
        foreach (var peer in _lanPeers.Values) RemovePeer(peer);
        _lanServer?.Dispose(); _lanServer = null; _lanCertificate?.Dispose(); _lanCertificate = null;
    }
}
