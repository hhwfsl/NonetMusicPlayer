using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NonetMusicPlayer.Core.Network;

internal static class LanChecks
{
    public static async Task RunAsync(string root)
    {
        var id = Guid.NewGuid().ToString("N");
        var bytes = Enumerable.Range(0, 1500000).Select(i => (byte)(i % 251)).ToArray();
        var catalog = new LanCatalog(id, "Desktop fixture", "one", Enumerable.Range(0, 501).Select(i => new LanSong("song" + i, "光の道標 " + i, "鹿乃", "Album", 5000, false, true)).ToArray(), [new("liked", "我喜欢", "", ["song0", "song500"])], 501);
        using var certificate = LanLibraryServer.Certificate(Path.Combine(root, "lan", "identity.pfx"));
        using var server = new LanLibraryServer(certificate, id, "Desktop fixture", () => Volatile.Read(ref catalog), (kind, track) => Task.FromResult<LanFile?>(track == "song0" && kind == "audio" ? new(new MemoryStream(bytes, false), bytes.Length, "audio/wav") : null));
        server.Start(); var invitation = server.Invitation("127.0.0.1");
        using var peer = await LanLibraryClient.ConnectAsync(invitation, Guid.NewGuid().ToString("N"), "Phone fixture");
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
        server.Dispose(); await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(12)); Check(!peer.Connected, "Heartbeat disconnects dead peer.");
        Console.WriteLine("PASS LAN HTTPS pinning, private invitation validation, 501-item paging, Unicode, HEAD/Range/seek, bounded audio, refresh, privacy and disconnection.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
