using System.ComponentModel;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Plugins;
public sealed partial class PluginManager
{
    private readonly Dictionary<string, ExtensionSession> _extensions = [];
    internal ViewModels.MainViewModel ExtensionViewModel => _uiVm ?? throw new InvalidOperationException("Host unavailable.");
    internal HashSet<string> ExtensionTracks => (_uiVm?.State.Tracks.Select(t => t.Id) ?? []).Concat(_uiVm?.State.RecentTemporaryTracks.Select(t => t.Id) ?? []).ToHashSet();
    internal MainWindow? ExtensionHost => _uiHost as MainWindow;
    internal string ExtensionDirectory(PluginManifest manifest) => Path.Combine(_storage.PluginsFolder, manifest.Id);
    internal string ExtensionStorage(PluginManifest manifest)
    {
        // 数据独立于发行包，不随同 ID 更新丢失。
        var path = Path.Combine(_storage.PluginsFolder, "Storage", manifest.Id);
        PluginPathPolicy.RejectLinkedAncestors(path); Directory.CreateDirectory(path); return path;
    }
    public ExtensionPage LoadExtensionPage(PluginManifest manifest)
    {
        RequireInstalled(manifest); manifest.Validate();
        var path = Path.Combine(ExtensionDirectory(manifest), manifest.PageEntry); PluginPathPolicy.RejectLinkedAncestors(path);
        using var input = File.OpenRead(path); return ExtensionContract.ReadPage(input);
    }
    public ExtensionSession Extension(PluginManifest manifest)
    {
        RequireInstalled(manifest);
        if (!manifest.Enabled || manifest.Type != "extension") throw new InvalidOperationException("Extension is disabled.");
        if (!_extensions.TryGetValue(manifest.Id, out var session) || session.IsDisposed) _extensions[manifest.Id] = session = new(this, manifest);
        return session;
    }
    private void StopExtension(string id, string reason = "lifecycle.disable") { if (_extensions.Remove(id, out var session)) session.Stop(reason); }
    internal void ReportExtensionError(Exception error) => _uiVm?.ReportError(L10n.T("Plugins.UnableToOpenPluginPage"), error);
    public IEnumerable<(PluginManifest Plugin, ExtensionContribution Contribution)> Contributions(string slot)
        => Installed.Where(p => p.Enabled && p.Type == "extension" && p.Permissions.Contains("ui-extend")).OrderBy(p => p.Id)
            .SelectMany(p => p.Contributions.Where(c => c.Slot == slot).Select(c => (p, c)));
    private void AttachExtensionEvents()
    {
        if (_uiVm is null) return;
        _uiVm.PropertyChanged += ExtensionPropertyChanged; _uiVm.OperationCompleted += ExtensionOperation;
    }
    private void DetachExtensionEvents()
    {
        if (_uiVm is not null) { _uiVm.PropertyChanged -= ExtensionPropertyChanged; _uiVm.OperationCompleted -= ExtensionOperation; }
        foreach (var session in _extensions.Values) session.Dispose(); _extensions.Clear();
    }
    private void ExtensionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "CurrentTrack") BroadcastExtensionEvent("player.track-changed", new()
        { ["id"] = _uiVm?.CurrentTrack?.Id, ["title"] = _uiVm?.CurrentTrack?.Title, ["artist"] = _uiVm?.CurrentTrack?.Artist });
        else if (e.PropertyName == "IsPlaying") BroadcastExtensionEvent("player.state-changed", new() { ["playing"] = _uiVm?.IsPlaying });
    }
    private void ExtensionOperation(object? sender, CommandResult result) => BroadcastExtensionEvent("operation.completed",
        new() { ["operation"] = result.Operation, ["success"] = result.Success, ["data"] = PluginCommandPolicy.Sanitize(result.Data) });
    public void BroadcastExtensionEvent(string name, JsonObject data)
    {
        foreach (var plugin in Installed.Where(p => p.Enabled && p.Type == "extension" && p.Events.Contains(name)).ToArray())
            _ = Extension(plugin).EventAsync(new(name, (JsonObject)data.DeepClone()));
    }
}
