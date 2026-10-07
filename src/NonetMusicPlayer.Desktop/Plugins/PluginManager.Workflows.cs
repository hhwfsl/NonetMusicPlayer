using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>流程扩展默认零开销；确定顺序、有界等待、重入隔离，失败保留宿主原流程。</summary>
public sealed partial class PluginManager
{
    private readonly HashSet<string> _failedHooks = [];
    private async Task StartAudioExtensionAsync(PluginManifest plugin)
    {
        try { await Extension(plugin).AttachAudioAsync(); }
        catch (Exception error) { ReportExtensionError(error); }
    }
    internal void ResetHookFailures(string id) => _failedHooks.RemoveWhere(key => key.StartsWith(id + ":", StringComparison.Ordinal));
    public bool HasHook(string name) => Installed.Any(p => p.Enabled && p.Type == "extension" && p.Hooks.Contains(name) && p.Id != ExtensionSession.Caller.Value && !_failedHooks.Contains(p.Id + ":" + name));
    public async Task<ExtensionHookDecision> EvaluateHooksAsync(string name, JsonObject data, CancellationToken cancellationToken = default)
    {
        var result = new ExtensionHookDecision { Data = (JsonObject)data.DeepClone() };
        var participants = Installed.Where(p => p.Enabled && p.Type == "extension" && p.Hooks.Contains(name) && p.Id != ExtensionSession.Caller.Value && !_failedHooks.Contains(p.Id + ":" + name)).OrderBy(p => p.Id).ToArray();
        foreach (var plugin in participants)
        {
            try
            {
                var decision = await Extension(plugin).EvaluateAsync(new(name, (JsonObject)result.Data.DeepClone()), cancellationToken).WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
                if (decision.Cancel) { result.Cancel = true; break; }
                // 决策不包含权限或确认字段；宿主在对应业务边界再次校验 IDs 和值。
                foreach (var pair in decision.Data) result.Data[pair.Key] = pair.Value?.DeepClone();
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                _failedHooks.Add(plugin.Id + ":" + name);
                AppLog.Warning("Extensions", "Workflow disabled for this session: " + plugin.Id + "/" + name, error);
                _uiVm?.ReportWarning(plugin.Name + " · " + L10n.T("Plugins.UnableToOpenPluginPage"));
            }
        }
        return result;
    }
}
