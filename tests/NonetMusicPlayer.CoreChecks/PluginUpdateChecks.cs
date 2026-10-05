using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Core.Plugins;

/// <summary>使用内存 HTTP 模拟仓库和 Release，不上传插件，也不连接真实私人仓库。</summary>
internal static class PluginUpdateChecks
{
    public static async Task RunAsync(string root)
    {
        var work = Path.Combine(root, "plugin-updates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var source = Path.Combine(work, "source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "manifest.json"), """{"id":"checks.update","name":"Update check","type":"theme","version":"1.1.0","repositoryOwner":"example","repositoryName":"nonet_plugin_check","originRepository":"untrusted/fake","tokens":{"Accent":"#6699CC"}}""");
        var package = PluginPackageBuilder.Pack(source, Path.Combine(work, "arbitrary-filename.impp"));
        var inspected = PluginPackageInspector.Inspect(package);
        Check(inspected.Id == "checks.update" && inspected.OriginRepository == "", "Package filename is not identity; origin cannot be forged");
        Check(PluginRepository.ForUpdate(inspected).LatestReleaseUrl == "https://github.com/example/nonet_plugin_check/releases/latest", "Local import builds request from source metadata");
        inspected.OriginRepository = "actual/source";
        Check(PluginRepository.ForUpdate(inspected).Coordinate == "actual/source", "Remote provenance takes priority");
        var old = new PluginManifest { Id = inspected.Id, Name = "Old", Type = "theme", Version = "1.0" };
        Check(PluginUpdatePolicy.Evaluate(null, inspected) == PluginInstallKind.New && PluginUpdatePolicy.Evaluate(old, inspected) == PluginInstallKind.Upgrade, "Same ID upgrades only");
        inspected.Version = "1.0.0"; Check(PluginUpdatePolicy.Evaluate(old, inspected) == PluginInstallKind.SameVersion, "Numeric version normalization");
        inspected.Version = "0.9.9"; Check(PluginUpdatePolicy.Evaluate(old, inspected) == PluginInstallKind.Downgrade, "Downgrade rejected independently of tag/name");
        inspected.Id = "other.id"; Reject(() => PluginUpdatePolicy.Evaluate(old, inspected));
        Reject(() => PluginRepository.Parse("../repo")); Reject(() => PluginRepository.Parse("owner/repo/extra"));
        Reject(() => PluginReleaseDownloader.RepositoryFromRelease("http://github.com/example/repo/releases/latest"));
        Reject(() => PluginReleaseDownloader.RepositoryFromRelease("https://github.com/example/repo/releases/latest?token=secret"));
        var bytes = File.ReadAllBytes(package); var requests = new List<string>();
        using var downloader = new PluginReleaseDownloader(new FakeHttp(request =>
        {
            var uri = request.RequestUri!; requests.Add(uri.AbsoluteUri);
            if (uri.Host == "api.github.com")
            {
                var json = JsonSerializer.Serialize(new { assets = new[] { new { name = "release.impp", browser_download_url = "https://github.com/example/nonet_plugin_check/releases/download/v99/release.impp", size = bytes.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)) } } });
                return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        var assets = await downloader.ResolveAsync("https://github.com/example/nonet_plugin_check/releases/latest");
        var progress = new List<double>();
        string downloaded;
        using (var result = await downloader.DownloadAsync(assets.Single(), work, new ImmediateProgress(progress)))
        {
            downloaded = result.Path;
            Check(PluginPackageInspector.Inspect(downloaded).Version == "1.1.0", "Actual manifest wins over v99 release tag");
            Check(progress.Last() == 100 && progress.All(p => p is >= 0 and <= 100), "Download progress and digest verification");
        }
        Check(!File.Exists(downloaded) && requests[0] == "https://api.github.com/repos/example/nonet_plugin_check/releases/latest", "Temporary download removed; correct API request");
        using var missing = new PluginReleaseDownloader(new FakeHttp(_ => new(HttpStatusCode.NotFound)));
        try { await missing.ResolveAsync("https://github.com/example/nonexistent/releases/latest"); throw new InvalidOperationException("Missing repo must fail"); }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
        Console.WriteLine("PASS plugin repository coordinates/provenance, ID/version policy, mocked Release, real package validation, progress/hash, missing repo, no uploads");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Unsafe metadata accepted"); }
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request)); }
    private sealed class ImmediateProgress(List<double> values) : IProgress<double> { public void Report(double value) => values.Add(value); }
}
