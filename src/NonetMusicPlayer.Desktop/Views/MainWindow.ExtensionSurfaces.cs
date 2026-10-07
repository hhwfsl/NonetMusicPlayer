using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>替换的是表现区域，播放与数据仍属于宿主；保留原控件以便停用或失败时立即恢复。</summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, (Control Original, Border Overlay)> _extensionSurfaces = [];
    private readonly HashSet<string> _failedSurfaces = [];
    private bool _surfaceEventsAttached;
    private HashSet<string> _surfaceRegistrations = [];
    private void ResetChangedSurfaceFailures()
    {
        if (_vm is null) return;
        var current = _vm.Plugins.Installed.Where(p => p.Enabled && p.Type == "extension").Select(p => p.Id + ":" + p.Version).ToHashSet(StringComparer.Ordinal);
        // 只清除重新启用或升级的注册，故障视图不会因播放进度刷新而反复重试。
        foreach (var registration in current.Except(_surfaceRegistrations))
        {
            var id = registration[..registration.LastIndexOf(':')];
            _failedSurfaces.RemoveWhere(key => key.StartsWith(id + ":", StringComparison.Ordinal));
        }
        _surfaceRegistrations = current;
    }
    private Control ContributionView(PluginManifest plugin, ExtensionContribution contribution)
    {
        var view = contribution.Native ? (Control)new NativeExtensionView(_vm!.Plugins, plugin, contribution.Slot)
            : new ExtensionPageView(_vm!.Plugins, plugin, contribution.View);
        void Unavailable()
        {
            if (contribution.Slot.StartsWith("page.", StringComparison.Ordinal))
                Dispatcher.UIThread.Post(() => { if (IsVisible && _vm is not null) ShowPage(); });
        }
        void Failed() { _failedSurfaces.Add(plugin.Id + ":" + contribution.Slot); Unavailable(); }
        if (view is NativeExtensionView native) { native.Failed += Failed; native.UnavailableView += Unavailable; }
        if (view is ExtensionPageView data) { data.Failed += Failed; data.UnavailableView += Unavailable; }
        return view;
    }
    private void BuildExtensionSurfaces()
    {
        if (_vm is null) return;
        ResetChangedSurfaceFailures();
        var root = (Grid)Content!;
        if (!_surfaceEventsAttached)
        {
            _surfaceEventsAttached = true; LayoutUpdated += (_, _) => PositionExtensionSurfaces();
            Closed += (_, _) => ClearExtensionSurfaces();
        }
        ClearExtensionSurfaces();
        // 设置、插件管理与终端始终使用原始区域，防止替换界面堵塞恢复入口。
        if (_vm.Page is "settings" or "plugins" or "terminal") return;
        var slots = new (string Slot, Control Region)[] { ("shell.workspace", Workspace), ("shell.navigation", SidebarPanel), ("shell.player", PlayerSurface), ("shell.titlebar", TitleBrand) };
        foreach (var (slot, region) in slots)
        {
            if (_extensionSurfaces.ContainsKey("shell.workspace") && slot is not "shell.titlebar") continue;
            var selected = _vm.Plugins.Contributions(slot).FirstOrDefault(c => (c.Contribution.View is not null || c.Contribution.Native) && !_failedSurfaces.Contains(c.Plugin.Id + ":" + slot));
            if (selected.Plugin is null) continue;
            try
            {
                var view = ContributionView(selected.Plugin, selected.Contribution);
                var overlay = new Border { Child = view, ZIndex = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
                var key = selected.Plugin.Id + ":" + slot;
                void Restore(bool failed)
                {
                    // 迟到的旧视图回调不应拆除同插槽的新视图；配置重载不是运行错误。
                    if (!_extensionSurfaces.TryGetValue(slot, out var current) || current.Overlay != overlay) return;
                    if (failed) _failedSurfaces.Add(key); region.Opacity = 1; region.IsHitTestVisible = true;
                    root.Children.Remove(overlay); _extensionSurfaces.Remove(slot);
                }
                if (view is NativeExtensionView native) { native.Failed += () => Restore(true); native.UnavailableView += () => Restore(false); }
                if (view is ExtensionPageView data) { data.Failed += () => Restore(true); data.UnavailableView += () => Restore(false); }
                region.Opacity = 0; region.IsHitTestVisible = false; _extensionSurfaces.Add(slot, (region, overlay)); root.Children.Add(overlay);
            }
            catch (Exception error) { _failedSurfaces.Add(selected.Plugin.Id + ":" + slot); _vm.ReportError(selected.Plugin.Name, error); }
        }
        PositionExtensionSurfaces();
    }
    private void PositionExtensionSurfaces()
    {
        var root = (Grid)Content!;
        foreach (var (original, overlay) in _extensionSurfaces.Values.ToArray())
        {
            var point = original.TranslatePoint(default, root);
            overlay.IsVisible = original.IsVisible && point is not null;
            if (point is not { } origin) continue;
            var margin = new Thickness(origin.X, origin.Y, 0, 0);
            if (overlay.Margin != margin) overlay.Margin = margin;
            if (overlay.Width != original.Bounds.Width) overlay.Width = original.Bounds.Width;
            if (overlay.Height != original.Bounds.Height) overlay.Height = original.Bounds.Height;
        }
    }
    private void ClearExtensionSurfaces()
    {
        foreach (var (original, overlay) in _extensionSurfaces.Values.ToArray())
        {
            original.Opacity = 1; original.IsHitTestVisible = true;
            if (overlay.Child is IDisposable disposable) disposable.Dispose();
            ((Grid)Content!).Children.Remove(overlay);
        }
        _extensionSurfaces.Clear();
    }
    private void RecoverExtensionInterface(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || !e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _vm is null) return;
        e.Handled = true;
        foreach (var plugin in _vm.Plugins.Installed.Where(p => p.Enabled && p.Type == "extension" && p.Permissions.Contains("ui-extend")).ToArray()) _vm.DisablePlugin(plugin);
        ClearExtensionSurfaces(); _vm.Navigate("plugins");
    }
}
