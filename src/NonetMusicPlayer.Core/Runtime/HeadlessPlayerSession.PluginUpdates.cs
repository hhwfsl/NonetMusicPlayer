using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private string BackupDirectory => string.IsNullOrWhiteSpace(State.Settings.BackupFolder) ? Path.Combine(DataDirectory, "Backups") : Path.GetFullPath(State.Settings.BackupFolder);
    private Func<PluginReleaseDownloader> _pluginDownloaderFactory = () => new();

    private async Task<JsonArray> CheckPluginUpdatesAsync(string? id, CancellationToken token)
    {
        using var downloader = _pluginDownloaderFactory();
        var plugins = id is null ? Plugins.Installed.Where(plugin => plugin.OriginRepository.Length > 0 || plugin.RepositoryName.Length > 0) : [Plugins.Find(id)];
        var result = new JsonArray();
        foreach (var plugin in plugins)
        {
            try
            {
                var release = await downloader.ResolveReleaseAsync(PluginRepository.ForUpdate(plugin).LatestReleaseUrl, token);
                result.Add(new JsonObject { ["id"] = plugin.Id, ["current"] = plugin.Version,
                    ["available"] = release.Version.Length == 0 ? "unverified" :
                        PluginUpdatePolicy.CompareVersions(release.Version, plugin.Version) > 0 ? release.Version : null });
            }
            catch (Exception error) when (id is null && error is HttpRequestException or InvalidDataException or InvalidOperationException)
            { result.Add(new JsonObject { ["id"] = plugin.Id, ["current"] = plugin.Version, ["error"] = Diagnostics.AppLog.Redact(error.Message) }); }
        }
        return result;
    }

    private async Task<JsonObject> UpdatePluginAsync(PluginManifest plugin, CancellationToken token)
    {
        var repository = PluginRepository.ForUpdate(plugin);
        using var downloader = _pluginDownloaderFactory();
        var release = await downloader.ResolveReleaseAsync(repository.LatestReleaseUrl, token);
        if (release.Version.Length > 0 && PluginUpdatePolicy.CompareVersions(release.Version, plugin.Version) <= 0)
            return new() { ["id"] = plugin.Id, ["version"] = plugin.Version, ["updated"] = false };
        if (release.Assets.Count != 1) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Commands.MultiplePackages"));
        using var package = await downloader.DownloadAsync(release.Assets[0], Path.Combine(DataDirectory, "Plugins"),
            new Progress<double>(percent => PluginDownloadProgress?.Invoke(this, percent)), token);
        var candidate = PluginPackageInspector.Inspect(package.Path);
        // 下载校验之后，再核对稳定 ID、版本和仓库，保留原有配置与平台约束。
        if (candidate.Id != plugin.Id || PluginUpdatePolicy.Evaluate(plugin, candidate) != PluginInstallKind.Upgrade ||
            release.Version.Length > 0 && PluginUpdatePolicy.CompareVersions(candidate.Version, release.Version) != 0)
            throw new InvalidDataException(Localization.LocalizationCatalog.Get("Plugins.UpdateIdentityMismatch"));
        token.ThrowIfCancellationRequested();
        var installed = Plugins.Install(package.Path, repository.Coordinate);
        return new() { ["id"] = installed.Id, ["version"] = installed.Version, ["updated"] = true, ["enabled"] = installed.Enabled };
    }
}
