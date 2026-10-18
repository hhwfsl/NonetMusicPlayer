using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>任意命名或运行时控件均可作为插件锚点；自定义控件与布局覆盖共享插件生命周期。</summary>
public sealed partial class MainWindow
{
    private ElementLayoutService? _elementLayouts;
    public ElementLayoutService ElementLayouts => _elementLayouts ??= new(this);
    private readonly Dictionary<string, List<IDisposable>> _elementContributions = [];
    private readonly Dictionary<string, Dictionary<string, ExtensionContribution>> _dynamicElements = [];
    private bool _elementExtensionsClosed;
    private string? _legacyLayoutOwner;
    private void InitializeElementExtensions()
    {
        if (_vm is null) return;
        _vm.Plugins.UiPluginUnavailable += ElementPluginUnavailable;
        _vm.Plugins.LifecycleChanged += ElementLifecycleChanged;
    }
    private void DetachElementExtensions()
    {
        if (_vm is null) return;
        _vm.Plugins.UiPluginUnavailable -= ElementPluginUnavailable;
        _vm.Plugins.LifecycleChanged -= ElementLifecycleChanged;
        SuspendElementExtensions(); _elementLayouts?.Dispose(); _dynamicElements.Clear(); _legacyLayoutOwner = null;
    }
    private bool _elementRefreshQueued;
    private void ElementLifecycleChanged(PluginManifest plugin, string stage)
    {
        if (_elementExtensionsClosed || stage is not ("enabled" or "disabled") || _elementRefreshQueued) return;
        _elementRefreshQueued = true;
        Dispatcher.UIThread.Post(() => { _elementRefreshQueued = false; if (_vm is null || _elementExtensionsClosed) return; SuspendElementExtensions(); BuildExtensionSlots(); RefreshElementExtensions(); });
    }
    private void ElementPluginUnavailable(string id) => Dispatcher.UIThread.Post(() =>
    {
        if (_elementExtensionsClosed) return;
        _dynamicElements.Remove(id);
        ClearElementContributions(id); _elementLayouts?.Remove(id);
        if (_legacyLayoutOwner == id)
        {
            _legacyLayoutOwner = null;
            LayoutConfiguration.Apply(UiLayoutService.DefaultJson);
            RenderLayout(LayoutConfiguration.Current);
        }
    });
    /// <summary>运行时插入也使用相同贡献协议，允许以目录返回的无名控件 ID 作为锚点。</summary>
    public void MountPluginElement(PluginManifest plugin, JsonObject args)
    {
        var key = args["key"]?.GetValue<string>() ?? "";
        var target = args["target"]?.GetValue<string>() ?? "";
        var placement = args["placement"]?.GetValue<string>() ?? "append";
        if (key.Length is 0 or > 100 || target.Length is 0 or > 240 || placement is not ("append" or "prepend" or "before" or "after" or "replace")) throw new InvalidDataException("Invalid mount key, target or placement.");
        var anchor = ElementLayouts.FindTarget(target) ?? throw new InvalidDataException("Mount target not found.");
        if (placement is "append" or "prepend" && anchor is not Panel
            || placement is "before" or "after" && anchor.Parent is not Panel
            || placement == "replace" && anchor.Parent is not (Panel or Decorator or ContentControl)) throw new InvalidDataException("Unsupported insertion position for this target.");
        if (!_dynamicElements.TryGetValue(plugin.Id, out var mounts)) _dynamicElements.Add(plugin.Id, mounts = []);
        if (!mounts.ContainsKey(key) && mounts.Count >= 32) throw new InvalidDataException("At most 32 dynamic controls per plugin.");
        var native = args["native"]?.GetValue<bool>() == true;
        if (native && (plugin.Runtime != "managed" || !plugin.Permissions.Contains("native-ui"))) throw new InvalidDataException("Native UI permission required.");
        ExtensionNode? node = null;
        if (!native)
        {
            var page = new JsonObject { ["schemaVersion"] = 2, ["root"] = args["view"]?.DeepClone() ?? throw new InvalidDataException("Missing view.") };
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(page.ToJsonString()));
            node = ExtensionContract.ReadPage(stream).Root;
        }
        mounts[key] = new("ui.element", key, key) { Target = target, Placement = placement, View = node, Native = native };
        SuspendElementExtensions(); RefreshElementExtensions();
    }
    public void UnmountPluginElement(string owner, string? key)
    {
        if (_dynamicElements.TryGetValue(owner, out var mounts)) { if (key is null) _dynamicElements.Remove(owner); else mounts.Remove(key); }
        SuspendElementExtensions(); RefreshElementExtensions();
    }
    public void ApplyPluginLayout(string id, string json)
    {
        if (_legacyLayoutOwner is not null && _legacyLayoutOwner != id) throw new InvalidDataException("Another plugin owns the legacy layout.");
        ApplyLayoutConfiguration(json); _legacyLayoutOwner = id;
    }
    public void ResetPluginLayout(string id)
    {
        ClearElementContributions(id); ElementLayouts.Remove(id);
        if (_legacyLayoutOwner != id) return;
        _legacyLayoutOwner = null; LayoutConfiguration.Apply(UiLayoutService.DefaultJson); RenderLayout(LayoutConfiguration.Current);
    }
    private void SuspendElementExtensions()
    {
        ClearElementContributions(); _elementLayouts?.Suspend();
    }
    private void RefreshElementExtensions()
    {
        if (_vm is null || _elementExtensionsClosed) return;
        try { _elementLayouts?.Refresh(); }
        catch (Exception error) { _vm.ReportError(L10n.T("Layout.Title"), error); }
        var dynamic = _vm.Plugins.Installed.Where(p => p.Enabled && _dynamicElements.ContainsKey(p.Id))
            .SelectMany(p => _dynamicElements[p.Id].Values.Select(c => (p, c)));
        foreach (var (plugin, contribution) in _vm.Plugins.Contributions("ui.element").Concat(dynamic))
        {
            if (_failedSurfaces.Contains(plugin.Id + ":" + contribution.Slot + ":" + contribution.Target)) continue;
            try
            {
                var target = ElementLayouts.FindTarget(contribution.Target);
                // 名称是跨页面可重建的稳定锚点；不在当前页面的锚点等待下次导航。
                if (target is null) continue;
                var view = ContributionView(plugin, contribution);
                IDisposable? insertion = null;
                try
                {
                    if (contribution.Placement is "append" or "prepend" && target is Panel panel)
                    {
                        panel.Children.Insert(contribution.Placement == "prepend" ? 0 : panel.Children.Count, view);
                        insertion = new ElementLayoutService.ActionLease(() => panel.Children.Remove(view));
                    }
                    else if (target.Parent is Panel parent)
                    {
                        var index = parent.Children.IndexOf(target);
                        if (contribution.Placement == "replace") parent.Children.Remove(target);
                        parent.Children.Insert(index + (contribution.Placement == "after" ? 1 : 0), view);
                        insertion = new ElementLayoutService.ActionLease(() =>
                        {
                            parent.Children.Remove(view);
                            if (contribution.Placement == "replace" && target.Parent is null) parent.Children.Insert(Math.Min(index, parent.Children.Count), target);
                        });
                    }
                    else if (contribution.Placement == "replace" && target.Parent is Decorator decorator)
                    {
                        decorator.Child = null; decorator.Child = view;
                        insertion = new ElementLayoutService.ActionLease(() => { if (decorator.Child == view) { decorator.Child = null; decorator.Child = target; } });
                    }
                    else if (contribution.Placement == "replace" && target.Parent is ContentControl content)
                    {
                        content.Content = null; content.Content = view;
                        insertion = new ElementLayoutService.ActionLease(() => { if (content.Content == view) { content.Content = null; content.Content = target; } });
                    }
                    else throw new InvalidDataException("Use a Panel anchor for insertion, or replace a Panel/Decorator/ContentControl child.");
                    if (!_elementContributions.TryGetValue(plugin.Id, out var leases)) _elementContributions.Add(plugin.Id, leases = []);
                    leases.Add(new ElementLayoutService.ActionLease(() => { insertion.Dispose(); if (view is IDisposable disposable) disposable.Dispose(); }));
                }
                catch { insertion?.Dispose(); if (view is IDisposable disposable) disposable.Dispose(); throw; }
            }
            catch (Exception error)
            {
                _failedSurfaces.Add(plugin.Id + ":" + contribution.Slot + ":" + contribution.Target); _vm.ReportError(plugin.Name, error);
            }
        }
    }
    private void ClearElementContributions(string? id = null)
    {
        foreach (var key in _elementContributions.Keys.Where(k => id is null || k == id).ToArray())
        {
            foreach (var lease in _elementContributions[key].AsEnumerable().Reverse()) lease.Dispose();
            _elementContributions.Remove(key);
        }
    }
}
