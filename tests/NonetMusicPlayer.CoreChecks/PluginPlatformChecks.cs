using System.Net;
using System.Text.Json;
using NonetMusicPlayer.Core.Plugins;

internal static class PluginPlatformChecks
{
    public static async Task RunAsync(string root)
    {
        var work = Path.Combine(root, "platform-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var rids = new[] { "win-x64", "osx-x64", "linux-x64" }; var manifest = new PluginManifest
        {
            Id = "checks.platform", Name = "Platform fixture", Type = "provider", Permissions = ["network", "process"],
            EntryPoints = rids.ToDictionary(r => r, r => "bin/" + r + "/worker")
        };
        File.WriteAllText(Path.Combine(work, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var files = new List<string> { "shared.txt" }; File.WriteAllText(Path.Combine(work, "shared.txt"), "shared resource");
        foreach (var rid in rids)
        {
            Directory.CreateDirectory(Path.Combine(work, "bin", rid));
            foreach (var name in new[] { "worker", "native.dll" }) { var file = "bin/" + rid + "/" + name; File.WriteAllText(Path.Combine(work, file), rid); files.Add(file); }
        }
        foreach (var rid in rids)
        {
            var package = PluginPackageBuilder.Pack(work, Path.Combine(work, "release-" + rid + ".impp"), overwrite: false, additionalFiles: files, rid: rid);
            var selected = PluginPackageInspector.Inspect(package);
            Check(selected.Platform == rid && selected.EntryPoints.Keys.SequenceEqual([rid]), "One RID per manifest");
            using var zip = System.IO.Compression.ZipFile.OpenRead(package);
            Check(zip.Entries.Any(e => e.FullName == "shared.txt") && zip.Entries.All(e => PluginPlatformPolicy.IncludesPath(e.FullName, rid)), "Only current native payload and shared files");
        }
        var legacy = PluginPackageBuilder.Pack(work, Path.Combine(work, "legacy.impp"), additionalFiles: files);
        var stage = Path.Combine(work, "legacy-installed"); Directory.CreateDirectory(stage);
        var old = PluginPackageInspector.Inspect(legacy); PluginPlatformPolicy.ExtractCurrent(legacy, old, stage);
        Check(File.Exists(Path.Combine(stage, "shared.txt")) && !Directory.Exists(Path.Combine(stage, "bin", rids.First(r => r != PluginPlatformPolicy.CurrentRid))), "Legacy multiprocess package extracts only current RID");
        var wrong = PluginPackageInspector.Inspect(Path.Combine(work, "release-" + rids.First(r => r != PluginPlatformPolicy.CurrentRid) + ".impp"));
        Reject(() => PluginPlatformPolicy.RequireSupported(wrong, PluginPlatformPolicy.CurrentRid));
        using var downloader = new PluginReleaseDownloader(new Handler(rids));
        var info = await downloader.ResolveReleaseAsync("https://github.com/example/plugin/releases/latest");
        Check(info.Version == "1.1.0" && info.Notes == "更新内容" && info.Assets.Single().Name.EndsWith(PluginPlatformPolicy.CurrentRid + ".impp"), "Release metadata and automatic exact-RID selection");
        using var none = new PluginReleaseDownloader(new Handler(rids.Where(r => r != PluginPlatformPolicy.CurrentRid).ToArray()));
        try { await none.ResolveAsync("https://github.com/example/plugin/releases/latest"); throw new Exception("Foreign asset accepted"); } catch (InvalidDataException) { }
        // CLI 不移动卸载保留的文件，同版本重新导入恢复连接与配置。
        using var host = new HeadlessPluginHost(Path.Combine(work, "cli"));
        var installed = host.Install(legacy); host.Configure(installed, "{\"caption\":\"保留\"}"); host.Uninstall(installed, false);
        Check(Directory.Exists(Path.Combine(work, "cli", installed.Id)) && !Directory.Exists(Path.Combine(work, "cli", "Retained")), "CLI disconnect keeps original folder");
        var reconnected = host.Install(legacy); Check(JsonDocument.Parse(reconnected.Configuration).RootElement.GetProperty("caption").GetString() == "保留" && host.Installed.Count == 1, "CLI same-version reconnect");
        host.Uninstall(reconnected, true); Check(!Directory.Exists(Path.Combine(work, "cli", installed.Id)), "CLI irreversible deletion");
        Console.WriteLine("PASS three separate RID packages, legacy platform pruning, shared resources, wrong-platform rejection, mocked release metadata/platform selection, CLI disconnect/reconnect/delete");
    }
    private sealed class Handler(string[] rids) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                tag_name = "v1.1.0", body = "更新内容", assets = rids.Select(r => new { name = "plugin-1.1.0-" + r + ".impp", size = 100,
                    browser_download_url = "https://github.com/example/plugin/releases/download/v1.1.0/plugin-1.1.0-" + r + ".impp" }).ToArray() })) });
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Platform mismatch accepted"); }
}
