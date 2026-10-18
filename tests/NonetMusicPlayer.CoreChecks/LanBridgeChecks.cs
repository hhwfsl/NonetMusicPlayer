using System.Text;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Network;

/// <summary>跨 Android/桌面的隔离协议夹具，不访问真实音频或使用数据。</summary>
internal static class LanBridgeChecks
{
    public static async Task ServeAsync(string root)
    {
        var wave = Wave(); var id = Guid.NewGuid().ToString("N");
        var catalog = new LanCatalog(id, "Desktop LAN test", "one", [new("test-a", "Desktop via LAN", "Nonet fixture", "LAN", 60000, false, true), new("test-b", "Unretained LAN", "Nonet fixture", "LAN", 60000, false, false)], [new("test-playlist", "Desktop playlist", "Read only", ["test-a", "test-b"])], 2);
        using var certificate = LanLibraryServer.Certificate(Path.Combine(root, "bridge-identity.pfx"));
        using var server = new LanLibraryServer(certificate, id, "Desktop LAN test", () => catalog, (kind, track) =>
        {
            if (track is not ("test-a" or "test-b")) return Task.FromResult<LanFile?>(null);
            var bytes = kind == "audio" ? wave : kind == "lyrics" ? Encoding.UTF8.GetBytes("[00:00.000]光の道標\n[00:00.000]Translation\n[00:10.000]Second line") : [];
            return Task.FromResult<LanFile?>(bytes.Length == 0 ? null : new(new MemoryStream(bytes, false), bytes.Length, kind == "audio" ? "audio/wav" : "text/plain"));
        });
        server.Start(); File.WriteAllText(Path.Combine(root, "desktop-invitation.txt"), server.Invitation("10.0.2.2"));
        Console.WriteLine("READY isolated desktop library; no playback instance created.");
        var deadline = DateTime.UtcNow.AddMinutes(15);
        while (!File.Exists(Path.Combine(root, "stop")) && DateTime.UtcNow < deadline) await Task.Delay(500);
    }
    public static async Task ReverseAsync(string root)
    {
        var invitation = File.ReadAllText(Path.Combine(root, "phone-invitation.txt"));
        using var peer = await LanLibraryClient.ConnectAsync(invitation, Guid.NewGuid().ToString("N"), "Desktop reverse test");
        var song = peer.Catalog.Tracks.First(t => t.Title == "Phone shared track");
        using (var stream = LanLibraryClient.OpenAudio(new Uri(peer.TrackUri(song.Id))))
        {
            var header = new byte[4]; stream.ReadExactly(header); if (Encoding.ASCII.GetString(header) != "RIFF") throw new InvalidDataException("Phone audio response is not the selected file.");
            stream.Position = 150000; stream.ReadExactly(header);
        }
        using var audio = new NativeAudioPlayer { Volume = .001f };
        await audio.LoadAsync(peer.TrackUri(song.Id)); audio.Play(); await Task.Delay(1200);
        if (audio.Duration.TotalSeconds < 50 || audio.Position.TotalMilliseconds < 100) throw new InvalidDataException("Desktop did not independently decode the phone stream.");
        audio.Stop(); Console.WriteLine("PASS actual Windows audio backend: phone HTTPS, Range, decode, independent playback and clock.");
        File.WriteAllText(Path.Combine(root, "reverse-complete"), "PASS");
    }
    private static byte[] Wave()
    {
        var data = new byte[48000 * 2 * 60 + 44]; using var stream = new MemoryStream(data); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(data.Length - 8); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(data.Length - 44); return data;
    }
}
