using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>通用插槽由清单驱动；宿主不识别具体插件名称或功能。</summary>
public sealed partial class MainWindow
{
    private MenuFlyout PlayerExtensionActions => (MenuFlyout)PlayerExtensionsButton.Flyout!;
    private MenuFlyout TitleExtensionActions => (MenuFlyout)TitleExtensionsButton.Flyout!;
    private readonly Dictionary<string, Window> _extensionOverlays = [];
    private async Task InvokeContributionAsync(PluginManifest plugin, ExtensionContribution contribution, JsonObject? context = null)
    {
        if (_vm is null) return;
        if (contribution.Action == "open-page")
        {
            if (!plugin.Permissions.Contains("navigation")) throw new InvalidDataException("Navigation permission required.");
            _vm.Navigate("plugin:" + plugin.Id, plugin.NavigationLabel); return;
        }
        await _vm.Plugins.Extension(plugin).InvokeAsync(contribution.Action, context ?? new(), userGesture: true);
    }
    internal void AddExtensionMenu(ContextMenu menu, string slot, JsonObject? context = null)
    {
        if (_vm is null) return;
        foreach (var (plugin, contribution) in _vm.Plugins.Contributions(slot))
        {
            var item = new MenuItem { Header = contribution.Label,
                Icon = new VectorIcon { Kind = Enum.TryParse<IconKind>(contribution.Icon, out var icon) ? icon : IconKind.Plugins, Width = 18, Height = 18 } };
            item.Click += async (_, _) =>
            { try { await InvokeContributionAsync(plugin, contribution, context); } catch (Exception error) { _vm.ReportError(plugin.Name, error); } };
            menu.Items.Add(item);
        }
    }
    private void BuildExtensionSlots()
    {
        if (_vm is null) return;
        BuildExtensionSurfaces(); BuildExtensionTray();
        PlayerExtensionActions.Items.Clear(); TitleExtensionActions.Items.Clear();
        void Fill(MenuFlyout flyout, string slot)
        {
            foreach (var (plugin, contribution) in _vm.Plugins.Contributions(slot))
            {
                var item = new MenuItem { Header = contribution.Label };
                item.Click += async (_, _) =>
                { try { await InvokeContributionAsync(plugin, contribution, new() { ["trackId"] = _vm.CurrentTrack?.Id }); } catch (Exception error) { _vm.ReportError(plugin.Name, error); } };
                flyout.Items.Add(item);
            }
        }
        Fill(PlayerExtensionActions, "player.actions"); Fill(TitleExtensionActions, "titlebar.actions");
        PlayerExtensionsButton.IsVisible = PlayerExtensionActions.Items.Count > 0;
        TitleExtensionsButton.IsVisible = TitleExtensionActions.Items.Count > 0;
        var overlays = _vm.Plugins.Contributions("overlay").Where(c => c.Contribution.View is not null || c.Contribution.Native).ToArray();
        var active = overlays.Select(c => c.Plugin.Id).ToHashSet();
        foreach (var id in _extensionOverlays.Keys.Where(id => !active.Contains(id)).ToArray())
        { _extensionOverlays[id].Close(); _extensionOverlays.Remove(id); }
        foreach (var (plugin, contribution) in overlays)
        {
            if (_extensionOverlays.ContainsKey(plugin.Id)) continue;
            var content = ContributionView(plugin, contribution);
            var options = contribution.Overlay ?? new ExtensionOverlayOptions();
            var window = new Window { Title = contribution.Label, Width = options.Width, Height = options.Height, MinWidth = 80, MinHeight = 80,
                WindowDecorations = options.Transparent ? Avalonia.Controls.WindowDecorations.None : Avalonia.Controls.WindowDecorations.Full,
                Background = options.Transparent ? Avalonia.Media.Brushes.Transparent : Ui.Brush("SurfaceBrush"),
                TransparencyLevelHint = options.Transparent ? [WindowTransparencyLevel.Transparent] : [],
                ShowInTaskbar = options.ShowInTaskbar, ShowActivated = false,
                Content = content, Topmost = options.Topmost, DataContext = _vm };
            _extensionOverlays.Add(plugin.Id, window); window.Closed += (_, _) => { if (content is IDisposable disposable) disposable.Dispose(); _extensionOverlays.Remove(plugin.Id); };
            // 普通独立窗口可以移动和调整大小；停用时立即关闭，非 OS 沙箱。
            window.Show();
        }
    }
    private Control? ExtensionReplacement(string slot)
    {
        if (_vm is null) return null;
        var candidate = _vm.Plugins.Contributions(slot).FirstOrDefault(c => (c.Contribution.View is not null || c.Contribution.Native) && !_failedSurfaces.Contains(c.Plugin.Id + ":" + slot));
        return candidate.Plugin is null ? null : ContributionView(candidate.Plugin, candidate.Contribution);
    }
    internal IEnumerable<(Border Card, string Title)> ExtensionSettingsSections()
    {
        if (_vm is null) yield break;
        foreach (var (plugin, contribution) in _vm.Plugins.Contributions("settings.sections"))
            if (contribution.View is not null || contribution.Native)
                yield return (Ui.Card(contribution.Label, ContributionView(plugin, contribution)), contribution.Label);
    }
    private void BuildExtensionTray()
    {
        if (_tray is null || _trayOpen is null || _trayExit is null || _vm is null) return;
        var menu = _tray.Menu ?? new NativeMenu(); menu.Items.Clear(); menu.Items.Add(_trayOpen); menu.Items.Add(_trayExit);
        foreach (var (plugin, contribution) in _vm.Plugins.Contributions("tray.actions"))
        {
            var item = new NativeMenuItem(contribution.Label);
            item.Click += async (_, _) => { try { await InvokeContributionAsync(plugin, contribution); } catch (Exception error) { _vm.ReportError(plugin.Name, error); } };
            menu.Items.Add(item);
        }
        _tray.Menu = menu;
    }
    private Control ExtensionHomeCards()
    {
        var cards = new StackPanel { Spacing = 12 };
        if (_vm is null) return cards;
        foreach (var (plugin, contribution) in _vm.Plugins.Contributions("home.cards"))
        {
            if (contribution.View is not null || contribution.Native) cards.Children.Add(ContributionView(plugin, contribution));
            else
            {
                var button = Ui.AsyncButton(contribution.Label, () => InvokeContributionAsync(plugin, contribution));
                button.HorizontalAlignment = HorizontalAlignment.Left; cards.Children.Add(button);
            }
        }
        return cards;
    }
}
