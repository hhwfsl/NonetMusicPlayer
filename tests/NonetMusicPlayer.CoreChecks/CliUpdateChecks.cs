using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Core.Updates;

internal static class CliUpdateChecks
{
    public static async Task RunAsync(string root)
    {
        var output = Path.Combine(root, "cli-update-" + Guid.NewGuid().ToString("N"));
        var installation = Path.Combine(output, "installed");
        var data = Path.Combine(installation, "Data");
        Directory.CreateDirectory(data);
        const string version = "0.4.0-beta.34";
        const string current = "0.4.0-beta.33";
        var rid = ReleaseUpdateService.PlatformRid;
        var executable = OperatingSystem.IsWindows() ? "nonet.exe" : "nonet";
        var files = new Dictionary<string, string> { [executable] = "new CLI executable", ["CLI.md"] = "new manual" };
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in files) Write(archive, name, content);
            Write(archive, ReleaseUpdateService.CliManifestName, JsonSerializer.Serialize(new { schema = 1, version, rid,
                files = files.ToDictionary(entry => entry.Key, entry => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Value)))) }));
        }
        var bytes = buffer.ToArray();
        var nameForRid = "NonetMusicPlayerCli-" + version + "-" + rid + ".zip";
        var asset = new GitHubAsset { Name = nameForRid, BrowserDownloadUrl = ReleaseUpdateService.Repository + "/releases/download/v0.4.0-beta.58/" + nameForRid,
            Size = bytes.Length, Digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)) };
        var releaseJson = JsonSerializer.Serialize(new[]
        {
            new GitHubRelease { TagName = "v0.4.0-beta.58", Assets = [asset,
                new() { Name = "NonetMusicPlayer.Desktop-0.4.0-beta.58-" + rid + ".zip", Size = asset.Size, BrowserDownloadUrl = asset.BrowserDownloadUrl, Digest = asset.Digest }] },
            new GitHubRelease { TagName = "android-v9.0.0", Assets = [new() { Name = "Nonet-9.0.0.apk" }] },
            new GitHubRelease { TagName = "v9.0.0", Draft = true, Assets = [new() { Name = "NonetMusicPlayerCli-9.0.0-" + rid + ".zip" }] }
        });
        using var http = new HttpClient(new ReleaseFixture(releaseJson, bytes));
        var service = new ReleaseUpdateService(http, ReleaseProduct.Cli);
        var release = await service.CheckAsync(current, rid);
        Require(release?.Version == version && release.Asset?.Name == nameForRid, "CLI selects its asset version independently of the combined desktop tag");
        Require(await service.CheckAsync(version, rid) is null && await service.CheckAsync(current, "other-arch") is null, "CLI rejects equal version and wrong architecture");
        var prepared = await service.DownloadAsync(release!, data);
        Require(prepared.Product == ReleaseProduct.Cli && prepared.Executable == executable, "CLI package type and validated entry point");
        File.WriteAllText(Path.Combine(installation, executable), "old CLI executable");
        File.WriteAllText(Path.Combine(data, "cli-state.json"), "user state");
        var plan = new UpdateApplyPlan { Product = prepared.Product, Installation = installation, Executable = executable,
            DataRoot = data, BackupRoot = Path.Combine(data, "Backups"), Payload = prepared.Payload, Manifest = prepared.Manifest };
        var rollback = Path.Combine(prepared.StageRoot, "rollback");
        UpdateInstaller.Apply(plan, rollback);
        Require(File.ReadAllText(Path.Combine(installation, executable)) == files[executable]
            && File.ReadAllText(Path.Combine(rollback, executable)) == "old CLI executable"
            && File.ReadAllText(Path.Combine(data, "cli-state.json")) == "user state", "CLI application files change atomically, saved state and rollback survive");
        using var wrongBytes = new HttpClient(new ReleaseFixture(releaseJson, bytes.Select(b => (byte)(b ^ 1)).ToArray()));
        try
        {
            await new ReleaseUpdateService(wrongBytes, ReleaseProduct.Cli).DownloadAsync(release!, data);
            throw new InvalidOperationException("Tampered CLI package was accepted.");
        }
        catch (InvalidDataException) { }
        Require(Directory.EnumerateDirectories(Path.Combine(data, "Updates")).Count() == 1, "Failed digest leaves no installation or staging changes");
        Console.WriteLine("PASS CLI update selects its own version/RID, verifies digests and preserves data and rollback.");
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ReleaseFixture(string releases, byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request,
                Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(releases) : new ByteArrayContent(archive) });
    }
}
