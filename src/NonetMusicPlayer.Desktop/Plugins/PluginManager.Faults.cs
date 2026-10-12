using System.Collections.Concurrent;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>普通插件异常统一归属、停用与通知；不承诺隔离栈溢出或原生致命错误。</summary>
public sealed partial class PluginManager
{
    private readonly ConcurrentDictionary<string, byte> _faultedPlugins = new();
    private readonly ConcurrentDictionary<string, (long Second, int Count)> _logRates = new();
    internal void WritePluginLog(PluginManifest manifest, string level, string message)
    {
        if (level is not ("info" or "warning" or "error") || string.IsNullOrWhiteSpace(message) || message.Length > 4000)
            throw new ArgumentException("Invalid plugin log level or message.");
        var second = Environment.TickCount64 / 1000;
        var rate = _logRates.AddOrUpdate(manifest.Id, (second, 1), (_, old) => old.Second == second ? (second, old.Count + 1) : (second, 1));
        if (rate.Count > 60) return;
        var area = "Plugin " + manifest.Name.Replace('\n', ' ').Replace('\r', ' ') + " (" + manifest.Id + ")";
        // 不能用换行伪造其他插件或系统日志；后台与终端使用同一日志源和脱敏规则。
        message = message.Replace('\n', ' ').Replace('\r', ' ');
        switch (level) { case "info": AppLog.Info(area, message); break; case "warning": AppLog.Warning(area, message); break; case "error": AppLog.Error(area, message); break; }
    }
    internal PluginRuntimeContext RuntimeFor(PluginManifest manifest, CancellationToken lifetime) => new(lifetime,
        (level, message) => WritePluginLog(manifest, level, message), async work =>
        {
            try { await Task.Run(() => work(lifetime), lifetime); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) { if (!lifetime.IsCancellationRequested) ReportPluginFault(manifest, error); }
        });
    internal void ReportPluginFault(PluginManifest manifest, Exception error)
    {
        if (!Installed.Contains(manifest) || error is PluginOperationException or ProviderRemoteException || !_faultedPlugins.TryAdd(manifest.Id, 0)) return;
        AppLog.Error("Plugin " + manifest.Name + " (" + manifest.Id + ")", "Unhandled plugin failure; plugin disabled.", error);
        void Disable()
        {
            // 同 ID 更新后迟到的旧任务不得停用新实例。
            if (!Installed.Contains(manifest)) return;
            try { SetEnabled(manifest, false); }
            catch (Exception stopError) { manifest.Enabled = false; StopExtension(manifest.Id); AppLog.Error("Plugins", "Unable to persist fault disable: " + manifest.Id, stopError); }
            _uiVm?.ReportError(L10n.Format("Plugins.FaultDisabled", manifest.Name, manifest.Id), error);
        }
        if (_uiVm is null || Dispatcher.UIThread.CheckAccess()) Disable(); else Dispatcher.UIThread.Post(Disable);
    }
}
