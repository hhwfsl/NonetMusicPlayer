using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Core.Commands;

internal static class LanChecks
{
    public static async Task RunAsync(string root)
    {
        var id = Guid.NewGuid().ToString("N");
        var bytes = Enumerable.Range(0, 1500000).Select(i => (byte)(i % 251)).ToArray();
        var catalog = new LanCatalog(id, "Desktop fixture", "one", Enumerable.Range(0, 501).Select(i => new LanSong("song" + i, "光の道標 " + i, "鹿乃", "Album", 5000, false, true)).ToArray(), [new("liked", "我喜欢", "", ["song0", "song500"])], 501);
        using var certificate = LanLibraryServer.Certificate(Path.Combine(root, "lan", "identity.pfx"));
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource<LanConnectionRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new LanLibraryServer(certificate, id, "Desktop fixture", () => Volatile.Read(ref catalog), (kind, track) => Task.FromResult<LanFile?>(track == "song0" && kind == "audio" ? new(new MemoryStream(bytes, false), bytes.Length, "audio/wav") : null),
            (request, token) => { requested.TrySetResult(request); return approval.Task.WaitAsync(token); });
        server.Start(); var invitation = server.Invitation("127.0.0.1");
        var connecting = LanLibraryClient.ConnectAsync(invitation, Guid.NewGuid().ToString("N"), "Phone fixture");
        var incoming = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(incoming.Name == "Phone fixture" && !connecting.IsCompleted, "Pairing must wait for explicit receiver approval.");
        Check(incoming.WaitSeconds == 30, "Default approval deadline is thirty seconds.");
        approval.SetResult(false);
        try { using var rejected = await connecting; throw new InvalidDataException("Rejected pairing issued a session."); } catch (HttpRequestException) { }
        approval = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connecting = LanLibraryClient.ConnectAsync(invitation, Guid.NewGuid().ToString("N"), "Phone fixture");
        await Task.Delay(100); Check(!connecting.IsCompleted, "Rejected request cannot approve the next request."); approval.SetResult(true);
        using var peer = await connecting;
        Check(peer.Catalog.Tracks.Length == 501 && peer.Catalog.Playlists.Single().TrackIds.Length == 2, "Paged catalog preserves playlists and Unicode.");
        using (var stream = LanLibraryClient.OpenAudio(new Uri(peer.TrackUri("song0"))))
        {
            Check(stream.Length == bytes.Length, "HEAD reports byte length."); var result = new byte[400]; stream.ReadExactly(result); Check(result.SequenceEqual(bytes.Take(400)), "Initial incremental audio read.");
            stream.Position = 1200000; stream.ReadExactly(result); Check(result.SequenceEqual(bytes.Skip(1200000).Take(400)), "Far Range seek does not download the complete audio.");
            stream.Position = 0; stream.ReadExactly(result); Check(result.SequenceEqual(bytes.Take(400)), "Backward seek.");
        }
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); peer.Disconnected += (_, _) => disconnected.TrySetResult();
        catalog = catalog with { Revision = "two", Tracks = catalog.Tracks.Select(t => t with { Title = "Changed" }).ToArray() };
        await peer.RefreshAsync(); Check(peer.Catalog.Tracks[0].Title == "Changed", "Refresh remains read-only and preserves stable IDs.");
        Check(LanLibraryServer.TryRange("bytes=-3", 10, out var start, out var end) && start == 7 && end == 9, "Suffix Range.");
        Check(!LanLibraryServer.TryRange("bytes=10-20", 10, out _, out _) && !LanLibraryServer.TryRange("bytes=1-2,4-5", 10, out _, out _), "Invalid and multipart ranges rejected.");
        foreach (var host in new[] { "8.8.8.8", "example.com" })
        {
            try { LanAddress.ParseInvitation(invitation.Replace("127.0.0.1", host)); throw new Exception("Public address accepted."); } catch (InvalidDataException) { }
        }
        var wrongCertificate = invitation.Replace(server.Fingerprint, new string('0', 64));
        try { using var ignored = await LanLibraryClient.ConnectAsync(wrongCertificate, Guid.NewGuid().ToString("N"), "Reject"); throw new Exception("Wrong certificate accepted."); } catch (HttpRequestException) { }
        var serialized = JsonSerializer.Serialize(peer.Catalog, LanJson.Default.LanCatalog); Check(!serialized.Contains(root, StringComparison.Ordinal) && !serialized.Contains("token", StringComparison.OrdinalIgnoreCase), "Catalog contains neither filesystem paths nor credentials.");
        server.Dispose(); await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(40)); Check(!peer.Connected, "Sustained heartbeat failure disconnects a dead peer, not a single delayed probe.");
        await DuplexAsync(root);
        await IdleDuplexAsync(root);
        Console.WriteLine("PASS LAN HTTPS pinning, private invitation validation, 501-item paging, Unicode, HEAD/Range/seek, bounded audio, refresh, privacy and disconnection.");
    }
    private static async Task DuplexAsync(string root)
    {
        foreach (var kinds in new[] { ("desktop", "desktop"), ("mobile", "mobile"), ("desktop", "mobile"), ("mobile", "desktop") })
        {
            var firstId = Guid.NewGuid().ToString("N"); var secondId = Guid.NewGuid().ToString("N");
            using var cert = LanLibraryServer.Certificate(Path.Combine(root, Guid.NewGuid() + ".pfx"));
            LanCatalog Catalog(string id, string kind) => new(id, kind, "one", [], [new("p", "List", "", [])], 0, kind);
            LanLibraryClient? reverse = null;
            using var first = new LanLibraryServer(cert, firstId, "First", () => Catalog(firstId, kinds.Item1), (_, _) => Task.FromResult<LanFile?>(null), (_, _) => Task.FromResult(true));
            using var second = new LanLibraryServer(cert, secondId, "Second", () => Catalog(secondId, kinds.Item2), (_, _) => Task.FromResult<LanFile?>(null));
            first.AcceptReverse = async (access, token) => { reverse = await LanLibraryClient.FromApprovedAccessAsync(access, token); return true; };
            first.Start(); second.Start();
            using var connected = await LanLibraryClient.ConnectDuplexAsync(first.Invitation("127.0.0.1"), secondId, "Second", true);
            var grant = new LanDuplexGrant(second.Port, second.Fingerprint, second.GrantAccess(firstId, "First"));
            await connected.EstablishReverseAsync(grant);
            using (reverse)
            {
                Check(connected.DuplexSupported && reverse is { Connected: true } && connected.Catalog.DeviceKind == kinds.Item1 && reverse.Catalog.DeviceKind == kinds.Item2, "A single approved connection gives both devices correctly typed catalogs.");
                try { await connected.EstablishReverseAsync(grant); throw new InvalidDataException("Reverse grant reused"); } catch (HttpRequestException) { }
                var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                first.PeerDisconnected += device => ended.TrySetResult(device);
                await connected.NotifyDisconnectAsync();
                Check(await ended.Task.WaitAsync(TimeSpan.FromSeconds(3)) == secondId, "Normal exit explicitly notifies the other device.");
                try { await connected.RefreshAsync(); throw new InvalidDataException("Revoked session accepted"); } catch (HttpRequestException) { }
            }
        }
        Check(CommandSyntax.IsPrivate("nonet network connect 'nonet://connect?key=private'"), "Invitation is excluded from command history.");
        Check(!NonetMusicPlayer.Core.Diagnostics.AppLog.Redact("nonet://connect?key=private").Contains("private"), "Invitation cannot enter disk logs.");
        Console.WriteLine("PASS desktop↔desktop, mobile↔mobile and cross-kind duplex negotiation, one-use reverse grants, explicit disconnect, revocation and invitation secrecy.");
    }
    /// <summary>单向响应持续延迟时，反向认证心跳仍证明对端在线；空闲不应撤销整个配对。</summary>
    private static async Task IdleDuplexAsync(string root)
    {
        var firstId = Guid.NewGuid().ToString("N"); var secondId = Guid.NewGuid().ToString("N"); var slow = 0;
        using var cert = LanLibraryServer.Certificate(Path.Combine(root, "idle-" + Guid.NewGuid() + ".pfx"));
        LanCatalog Catalog(string id) => new(id, "Idle fixture", "one", [], [], 0, "desktop");
        LanLibraryClient? reverse = null;
        using var first = new LanLibraryServer(cert, firstId, "First", () => { if (Volatile.Read(ref slow) != 0) Thread.Sleep(5500); return Catalog(firstId); },
            (_, _) => Task.FromResult<LanFile?>(null), (_, _) => Task.FromResult(true));
        using var second = new LanLibraryServer(cert, secondId, "Second", () => Catalog(secondId), (_, _) => Task.FromResult<LanFile?>(null));
        first.AcceptReverse = async (access, token) => { reverse = await LanLibraryClient.FromApprovedAccessAsync(access, token); return true; };
        first.Start(); second.Start();
        using var peer = await LanLibraryClient.ConnectDuplexAsync(first.Invitation("127.0.0.1"), secondId, "Second", true);
        await peer.EstablishReverseAsync(new(second.Port, second.Fingerprint, second.GrantAccess(firstId, "First")));
        try
        {
            Volatile.Write(ref slow, 1);
            for (var i = 0; i < 35; i++)
            {
                await Task.Delay(1000);
                Check(peer.Connected && reverse is { Connected: true }, "Authenticated reverse traffic must retain an idle pair beyond the single-direction timeout.");
            }
            Volatile.Write(ref slow, 0);
            await peer.RefreshAsync(); Check(peer.Connected, "Delayed peer recovers without a new invitation or pairing.");
            var revoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.Disconnected += (_, _) => revoked.TrySetResult();
            first.RevokeDevice(secondId);
            await revoked.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(!peer.Connected, "Explicit credential revocation is not delayed by reverse liveness.");
            Console.WriteLine("PASS idle duplex: sustained one-direction latency, authenticated reverse liveness, same-session recovery and immediate revocation.");
        }
        finally { Volatile.Write(ref slow, 0); reverse?.Dispose(); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
