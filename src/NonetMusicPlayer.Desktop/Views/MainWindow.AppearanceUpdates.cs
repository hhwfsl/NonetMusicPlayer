using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    /// <summary>只记录需要重排的状态；颜色、字体及业务设置不应重挂接整个界面。</summary>
    private sealed record AppearanceStructure(AppSettings Profile, bool NavigationRight, bool PlayerTop, double SidebarWidth,
        double PlayerHeight, bool TouchMode, string Layout, string Plugins);
    private AppearanceStructure? _appearanceStructure;
    private AppearanceStructure CurrentAppearanceStructure()
    {
        var s = InterfaceSettings;
        var plugins = string.Join(";", _vm!.Plugins.Installed.Select(p => $"{p.Id}:{p.Version}:{p.Enabled}:{p.HasPage}:{p.Type}"));
        return new(_vm.Settings, s.NavigationRight, s.PlayerTop, s.SidebarWidth, s.PlayerHeight, s.TouchMode,
            LayoutConfiguration?.AppliedJson ?? "", plugins);
    }
    private void SettingsChanged(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        RebuildInterfaceSettings();
        var preserve = _appearanceStructure == CurrentAppearanceStructure();
        Terminal?.Configure(GetTerminalOptions());
        if (preserve)
        {
            // 保留输入焦点、滚动视口、扩展控件与布局租约，避免先还原再组装产生闪帧。
            ApplyAppearance(preserveLayout: true);
            _cachedSettings?.RefreshInterfaceOwnership(this);
            return;
        }
        SuspendElementExtensions(); ApplyAppearance();
        BuildPluginNavigation(); BuildExtensionSlots(); BuildMusicSearchHeader(); RefreshElementExtensions();
    }
}
