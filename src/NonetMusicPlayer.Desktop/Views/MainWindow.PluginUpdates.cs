using System.Net;
using Avalonia.Controls;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private Func<PluginReleaseDownloader> _pluginDownloaderFactory = () => new PluginReleaseDownloader();
    private readonly Dictionary<string, PluginReleaseInfo> _pluginUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _checkingPlugins = new(StringComparer.Ordinal);
    private readonly HashSet<string> _downloadingPlugins = new(StringComparer.Ordinal);
    internal event EventHandler? PluginUpdatesChanged;
    internal PluginReleaseInfo? PluginUpdate(string id) => _pluginUpdates.GetValueOrDefault(id);
    internal void ForgetPluginUpdate(string id) { _pluginUpdates.Remove(id); PluginUpdatesChanged?.Invoke(this, EventArgs.Empty); }

    /// <summary>后台只检查元数据，限制并发；缺失仓库/离线不打断启动，也不自动下载插件。</summary>
    internal async Task CheckPluginUpdatesAsync()
    {
        if (_vm is null) return;
        using var throttle = new SemaphoreSlim(2);
        var installed = _vm.Plugins.Installed.ToArray();
        await Task.WhenAll(installed.Where(p => p.OriginRepository.Length > 0 || p.RepositoryName.Length > 0).Select(async plugin =>
        {
            try
            {
                await throttle.WaitAsync(_updateStop.Token);
                try { await CheckPluginUpdateAsync(plugin, false); }
                finally { throttle.Release(); }
            }
            catch (OperationCanceledException) { }
        }));
    }

    internal async Task CheckPluginUpdateAsync(PluginManifest plugin, bool manual = true)
    {
        if (_vm is null || !_checkingPlugins.Add(plugin.Id)) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateStop.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var downloader = _pluginDownloaderFactory();
            var info = await downloader.ResolveReleaseAsync(PluginRepository.ForUpdate(plugin).LatestReleaseUrl, cancellation.Token);
            // Release tag 是发现提示，不是包身份；真正更新仍审查包内稳定 ID 与数字版本。
            if (info.Version.Length > 0 && PluginUpdatePolicy.CompareVersions(info.Version, plugin.Version) > 0)
            {
                // 仅向仍安装的同一实例发布检查结果。
                if (_vm.Plugins.Installed.Contains(plugin)) _pluginUpdates[plugin.Id] = info;
            }
            else _pluginUpdates.Remove(plugin.Id);
            PluginUpdatesChanged?.Invoke(this, EventArgs.Empty);
            if (manual)
            {
                if (_pluginUpdates.TryGetValue(plugin.Id, out var found)) await ConfirmPluginUpdateAsync(plugin, found);
                else if (info.Version.Length == 0) await ConfirmPluginUpdateAsync(plugin, info with { Version = "?" });
                else _vm.ReportWarning(L10n.T("Plugins.AlreadyCurrent"));
            }
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        { if (manual) _vm.ReportWarning(L10n.T("Plugins.RepositoryUnavailable")); }
        catch (OperationCanceledException) { if (manual && !_updateStop.IsCancellationRequested) _vm.ReportWarning(L10n.T("Plugins.PluginDownloadCanceledOrTimedOut")); }
        catch (Exception error)
        {
            AppLog.Warning("PluginUpdate", "Plugin release check failed: " + plugin.Id, error);
            if (manual) _vm.ReportError(L10n.T("Plugins.PluginDownloadFailed"), error);
        }
        finally { _checkingPlugins.Remove(plugin.Id); }
    }

    internal async Task ConfirmPluginUpdateAsync(PluginManifest plugin, PluginReleaseInfo info)
    {
        if (_vm is null || _downloadingPlugins.Contains(plugin.Id)) return;
        var accepted = await PlayerDialog.ConfirmRelease(this, L10n.Format("Plugins.UpdateTitle", plugin.Name),
            plugin.Version + " → " + info.Version, info.Notes);
        if (!accepted || !_vm.Plugins.Installed.Contains(plugin)) return;
        if (_vm.IsBusy || _vm.IsMigratingData) { _vm.ReportWarning(L10n.T("Plugins.WaitForTheCurrentOperationBeforeInstallingAPlugin")); return; }
        _downloadingPlugins.Add(plugin.Id);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateStop.Token);
        using var notification = ShowPluginDownload(cancellation.Cancel);
        try
        {
            var selected = info.Assets[0];
            if (info.Assets.Count > 1)
            {
                var name = await PlayerDialog.Choose(this, L10n.T("Plugins.ChoosePluginAsset"), L10n.T("Plugins.ChooseAnImppPackageForThisOperatingSystem"), info.Assets.Select(a => a.Name).ToArray());
                if (name is null) return; selected = info.Assets.First(a => a.Name == name);
            }
            notification.Downloading(selected.Name);
            using var downloader = _pluginDownloaderFactory();
            using var package = await downloader.DownloadAsync(selected, _vm.Storage.PluginsFolder, notification, cancellation.Token);
            var candidate = await Task.Run(() => Plugins.PluginManager.Inspect(package.Path), cancellation.Token);
            if (Version.TryParse(info.Version, out _) && PluginUpdatePolicy.CompareVersions(candidate.Version, info.Version) != 0)
                throw new InvalidDataException(L10n.T("Plugins.ReleaseVersionMismatch"));
            if (candidate.Id != plugin.Id || PluginUpdatePolicy.Evaluate(plugin, candidate) != PluginInstallKind.Upgrade)
                throw new InvalidDataException(L10n.T("Plugins.UpdateIdentityMismatch"));
            // 远程包权限有变化时必须再次显示新的权限，不能利用更新绕过原安装授权。
            if (!candidate.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(plugin.Permissions)
                && !await PlayerDialog.Confirm(this, L10n.Format("Plugins.UpdateTitle", plugin.Name),
                    L10n.Format("Plugins.UpdateDetails", plugin.Version, candidate.Version, string.Join(" / ", candidate.Permissions), candidate.Description), L10n.T("Plugins.TrustAndUpdate"))) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (_vm.IsBusy || _vm.IsMigratingData) throw new InvalidOperationException(L10n.T("Plugins.WaitForTheCurrentOperationBeforeInstallingAPlugin"));
            _vm.IsBusy = true;
            try { await _vm.Plugins.InstallAsync(package.Path, PluginRepository.ForUpdate(plugin).Coordinate); }
            finally { _vm.IsBusy = false; }
            _vm.ApplySettings(); RefreshPluginNavigation(); ForgetPluginUpdate(plugin.Id);
            notification.Complete(candidate.Name); _vm.StatusText = L10n.T("Plugins.PluginUpdated");
            if (_vm.Page == "plugins") ShowPage();
        }
        catch (OperationCanceledException) { if (!_updateStop.IsCancellationRequested) _vm.ReportWarning(L10n.T("Plugins.PluginDownloadCanceledOrTimedOut")); }
        catch (Exception error) { _vm.ReportError(L10n.T("Plugins.PluginDownloadFailed"), error); }
        finally { _downloadingPlugins.Remove(plugin.Id); }
    }
}
