using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Core.Plugins;
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
        await CheckRateLimitFallbackAsync(output, rid, bytes, asset);
        Console.WriteLine("PASS CLI update selects its own version/RID, verifies digests and preserves data and rollback.");
    }

    private static async Task CheckRateLimitFallbackAsync(string output, string rid, byte[] bytes, GitHubAsset cliAsset)
    {
        var desktopName = "NonetMusicPlayer.Desktop-0.4.0-beta.58-" + rid + ".zip";
        var checksums = cliAsset.Digest![7..] + "  " + cliAsset.Name + "\n" + cliAsset.Digest[7..] + "  " + desktopName + "\n";
        var fixture = new RateLimitFixture(checksums, bytes);
        using var client = new HttpClient(fixture);
        var cli = new ReleaseUpdateService(client, ReleaseProduct.Cli);
        var result = await cli.CheckAsync("0.4.0-beta.33", rid);
        Require(result is { Version: "0.4.0-beta.34", Asset.Size: 0 } && result.Asset.Name == cliAsset.Name, "REST 403 falls back to public tag and exact CLI asset checksum");
        Require(await cli.CheckAsync("0.4.0-beta.33", rid) == result && fixture.RestCalls == 1 && fixture.FeedCalls == 1, "Repeated checks reuse successful discovery");
        Require(await cli.CheckAsync("0.4.0-beta.34", rid) is null && fixture.RestCalls == 1, "CLI compares its own version during API cooldown");
        var prepared = await cli.DownloadAsync(result!, Path.Combine(output, "public-no-size"));
        Require(prepared.Product == ReleaseProduct.Cli, "Public download verifies both digests without an API size");
        ReleaseUpdateService.Discard(prepared);

        using var desktopClient = new HttpClient(new RateLimitFixture(checksums, bytes, HttpStatusCode.TooManyRequests));
        var desktop = new ReleaseUpdateService(desktopClient);
        var newer = await desktop.CheckAsync("0.4.0-beta.57", rid);
        Require(newer is { Version: "0.4.0-beta.58" } && newer.Asset?.Name == desktopName, "REST 429 falls back to the same-rid desktop archive");

        using var unverifiedClient = new HttpClient(new RateLimitFixture("broken checksum", bytes));
        var unverified = new ReleaseUpdateService(unverifiedClient);
        Require(await unverified.CheckAsync("0.4.0-beta.57", rid) is { Version: "0.4.0-beta.58", Asset: null }, "Fallback never offers an unverified package");

        using var badClient = new HttpClient(new RateLimitFixture(checksums, bytes.Select(b => (byte)(b ^ 1)).ToArray()));
        try
        {
            await new ReleaseUpdateService(badClient, ReleaseProduct.Cli).DownloadAsync(result!, Path.Combine(output, "public-bad-sha"));
            throw new InvalidOperationException("Corrupt public package was accepted.");
        }
        catch (InvalidDataException) { }

        var pluginFixture = new RateLimitFixture(checksums, bytes);
        using var downloader = new PluginReleaseDownloader(pluginFixture);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await downloader.ResolveReleaseAsync("https://github.com/example/plugin/releases/latest");
                throw new InvalidOperationException("Unexpected success for a rate limited plugin request.");
            }
            catch (GitHubRateLimitException) { }
        }
        Require(pluginFixture.RestCalls == 1, "Plugin metadata checks respect the API cooldown");
        Console.WriteLine("PASS GitHub update fallback for 403/429, cache and cooldown, edition/RID selection, mandatory checksum and untrusted ZIP rejection.");
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
    private sealed class RateLimitFixture(string checksum, byte[] archive, HttpStatusCode status = HttpStatusCode.Forbidden) : HttpMessageHandler
    {
        public int RestCalls { get; private set; }
        public int FeedCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            HttpResponseMessage response;
            if (uri.Host == "api.github.com")
            {
                RestCalls++;
                response = new(status);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
                if (status == HttpStatusCode.Forbidden)
                {
                    response.ReasonPhrase = "rate limit exceeded";
                    response.Headers.Add("X-RateLimit-Remaining", "0");
                }
            }
            else if (uri.AbsolutePath.EndsWith("/releases.atom", StringComparison.Ordinal))
            {
                FeedCalls++;
                response = new(HttpStatusCode.OK) { Content = new StringContent("""
                    <feed xmlns="http://www.w3.org/2005/Atom">
                    <entry><link rel="alternate" href="https://github.com/hhwfsl/NonetMusicPlayer/releases/tag/android-v9.0.0"/></entry>
                    <entry><link rel="alternate" href="https://example.com/hhwfsl/NonetMusicPlayer/releases/tag/v8.0.0"/></entry>
                    <entry><link rel="alternate" href="https://github.com/hhwfsl/NonetMusicPlayer/releases/tag/v0.4.0-beta.58"/></entry>
                    </feed>
                    """) };
            }
            else if (uri.AbsolutePath.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal))
            {
                response = new(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://release-assets.githubusercontent.com/fixture/checksum");
            }
            else if (uri.Host == "release-assets.githubusercontent.com")
                response = new(HttpStatusCode.OK) { Content = new StringContent(checksum) };
            else response = new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(archive)) };
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
