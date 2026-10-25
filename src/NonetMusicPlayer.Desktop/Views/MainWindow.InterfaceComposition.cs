using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>方案只持有可撤销的表现覆盖；不改写用户设置、音乐数据与插件授权。</summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<string, List<StyleSheet>> _interfaceLayers = [];
    private InterfaceCompositionService? _interfaceComposition;
    private AppSettings? _effectiveInterfaceSettings;
    private bool _committingInterface;
    private readonly Dictionary<string, string> _ownedInterfaceSettings = [];
    public AppSettings InterfaceSettings => _effectiveInterfaceSettings ?? _vm!.Settings;
    public InterfaceMotionProfile? InterfaceMotionProfile
    {
        get
        {
            var profiles = _interfaceLayers.Values.SelectMany(x => x).Select(s => s.Layout?.Motion).OfType<InterfaceMotionProfile>().ToArray();
            return profiles.Length == 0 ? null : new() { LyricsDurationMs = profiles.Select(p => p.LyricsDurationMs).LastOrDefault(n => n.HasValue), LyricsCornerRadius = profiles.Select(p => p.LyricsCornerRadius).LastOrDefault(n => n.HasValue), PageDurationMs = profiles.Select(p => p.PageDurationMs).LastOrDefault(n => n.HasValue) };
        }
    }
    public InterfaceLyricsProfile? InterfaceLyricsProfile
    {
        get
        {
            var profiles = _interfaceLayers.Values.SelectMany(x => x).Select(s => s.Layout?.Lyrics).OfType<InterfaceLyricsProfile>().ToArray();
            return profiles.Length == 0 ? null : new() { EdgeToEdge = profiles.Select(p => p.EdgeToEdge).LastOrDefault(v => v.HasValue), ActiveColor = profiles.Select(p => p.ActiveColor).LastOrDefault(c => c.Length > 0) ?? "", PendingColor = profiles.Select(p => p.PendingColor).LastOrDefault(c => c.Length > 0) ?? "", TranslationColor = profiles.Select(p => p.TranslationColor).LastOrDefault(c => c.Length > 0) ?? "" };
        }
    }
    public bool IsInterfaceSettingOwned(string key) => _ownedInterfaceSettings.ContainsKey(key);
    public string? InterfaceSettingOwner(string key) => _ownedInterfaceSettings.GetValueOrDefault(key);
    private InterfaceCompositionService Composition => _interfaceComposition ??= new(this, BuildInterfaceComponent, MatchInterfacePage);
    private bool MatchInterfacePage(string condition)
    {
        var invert = condition.StartsWith('!'); if (invert) condition = condition[1..];
        var matches = condition.EndsWith('*') ? _vm?.Page.StartsWith(condition[..^1], StringComparison.Ordinal) == true : _vm?.Page == condition;
        return invert ? !matches : matches;
    }
    public void ApplyInterfaceLayers(string owner, IReadOnlyList<StyleSheet> layers)
    {
        foreach (var sheet in layers) StyleSheetContract.Validate(sheet);
        var old = _interfaceLayers.GetValueOrDefault(owner); var originalOrder = _interfaceLayers.ToArray();
        SuspendElementExtensions(); _interfaceLayers.Remove(owner);
        if (layers.Count > 0) _interfaceLayers[owner] = layers.ToList();
        _committingInterface = true;
        try { RebuildInterfaceSettings(); ApplyAppearance(); StyleLayers.Apply(owner, layers); RefreshElementExtensions(); }
        catch
        {
            SuspendElementExtensions(); _interfaceLayers.Clear(); foreach (var pair in originalOrder) _interfaceLayers.Add(pair.Key, pair.Value);
            RebuildInterfaceSettings(); ApplyAppearance(); StyleLayers.Apply(owner, old ?? []); RefreshElementExtensions(); throw;
        }
        finally { _committingInterface = false; }
        if (_vm?.Page.StartsWith("interface:", StringComparison.Ordinal) == true || _vm?.Page is "settings" or "lyrics") ShowPage();
    }
    private void RemoveInterfaceLayers(string owner)
    {
        if (!_interfaceLayers.Remove(owner)) return;
        SuspendElementExtensions(); RebuildInterfaceSettings(); ApplyAppearance(); RefreshElementExtensions();
        if (_vm?.Page.StartsWith("interface:", StringComparison.Ordinal) == true)
        {
            if (FindInterfacePage(_vm.Page[10..]).Node is null) _vm.Navigate("library");
            else ShowPage();
        }
    }
    private void RebuildInterfaceSettings()
    {
        _ownedInterfaceSettings.Clear();
        var values = new JsonObject();
        foreach (var sheets in _interfaceLayers.Values) foreach (var sheet in sheets)
            foreach (var (key, value) in sheet.Layout?.Settings ?? new()) { values[key] = value?.DeepClone(); _ownedInterfaceSettings[key] = sheet.Name; }
        if (_vm is null) { _effectiveInterfaceSettings = null; return; }
        // 即便没有显式设置覆盖，独立 Shell 也使用副本，避免拖动时意外写回持久设置。
        if (_ownedInterfaceSettings.Count == 0 && !_interfaceLayers.Values.SelectMany(x => x).Any(s => s.Layout is not null)) { _effectiveInterfaceSettings = null; return; }
        var node = JsonSerializer.SerializeToNode(_vm.Settings, AppStorage.Json)!.AsObject();
        foreach (var (key, value) in values) node[key] = value?.DeepClone();
        _effectiveInterfaceSettings = node.Deserialize<AppSettings>(AppStorage.Json)!; _effectiveInterfaceSettings.TouchMode = _vm.Settings.TouchMode;
    }
    private void RefreshInterfaceComposition()
    {
        if (_vm is null || _elementExtensionsClosed) return;
        var regions = new Dictionary<string, InterfaceNode>();
        var regionOwners = new Dictionary<string, string>();
        foreach (var (owner, sheets) in _interfaceLayers) foreach (var sheet in sheets) foreach (var (key, tree) in sheet.Layout?.Regions ?? []) { regions[key] = tree; regionOwners[key] = owner; }
        try { Composition.Apply(regions); }
        catch (Exception error)
        {
            if (_committingInterface) throw;
            var owner = regionOwners.GetValueOrDefault(error.Data["Nonet.InterfaceRegion"] as string ?? "");
            var plugin = _vm.Plugins.Installed.FirstOrDefault(p => p.Id == owner);
            if (plugin is not null) _vm.Plugins.ReportPluginFault(plugin, error); else _vm.ReportError(L10n.T("Layout.Title"), error);
        }
        _cachedSettings?.RefreshInterfaceOwnership(this);
        RefreshWindowFrame();
        Workspace.NavigationResizeEnabled = !IsInterfaceSettingOwned("sidebarWidth"); Workspace.PlayerResizeEnabled = !IsInterfaceSettingOwned("playerHeight");
        RefreshNavigationMetrics();
    }
    public JsonObject InterfaceCatalog() => new()
    {
        ["success"] = true, ["capability"] = InterfaceCompositionContract.Capability,
        ["settings"] = new JsonArray(InterfaceCompositionContract.Settings.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
        ["primitives"] = new JsonArray("grid", "stack", "border", "scroll", "canvas", "ref", "navigate", "action", "playlists", "artwork", "text", "icon", "spacer"),
        ["protected"] = new JsonArray(InterfaceCompositionService.ProtectedTargets.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()), ["elements"] = ElementLayouts.Catalog()
    };
    private (string? Owner, InterfaceNode? Node) FindInterfacePage(string key)
    {
        foreach (var (owner, sheets) in _interfaceLayers.Reverse())
            foreach (var sheet in sheets.AsEnumerable().Reverse())
                if (sheet.Layout?.Pages.TryGetValue(key, out var node) == true) return (owner, node);
        return (null, null);
    }
    private Control? InterfacePage(string page)
    {
        if (!page.StartsWith("interface:", StringComparison.Ordinal)) return null;
        var key = page[10..];
        var (owner, node) = FindInterfacePage(key);
        if (node is null) return Ui.Text(L10n.T("Plugins.PluginIsDisabled"));
        if (node.Label.Length > 0 && _vm is not null) _vm.PageTitle = L10n.T(node.Label);
        try { return Composition.BuildPage(node); }
        catch (Exception error)
        {
            // 延迟创建的方案页也必须归属到贡献插件，不能让无效属性进入 Dispatcher 未处理异常。
            var plugin = _vm?.Plugins.Installed.FirstOrDefault(p => p.Id == owner);
            if (plugin is not null) _vm!.Plugins.ReportPluginFault(plugin, error);
            else _vm?.ReportError(L10n.T("Layout.Title"), error);
            return Ui.Text(L10n.T("Plugins.PluginIsDisabled"));
        }
    }
    private Control? BuildInterfaceComponent(InterfaceNode node)
    {
        if (_vm is null) return null;
        switch (node.Kind)
        {
            case "action":
                if (node.Target is not ("playlist.create" or "player.play-pause" or "player.previous" or "player.next" or "lyrics.toggle")) throw new InvalidDataException("Not an existing presentation action.");
                var action = Ui.Button(node.Label, () =>
                {
                    switch (node.Target)
                    {
                        case "playlist.create": CreatePlaylist_Click(null, new()); break;
                        case "player.play-pause": _vm.TogglePlayPauseCommand.Execute(null); break;
                        case "player.previous": _vm.PlayPreviousCommand.Execute(null); break;
                        case "player.next": _vm.PlayNextCommand.Execute(null); break;
                        case "lyrics.toggle": ToggleLyrics(); break;
                    }
                });
                action.Content = Ui.Actions(new VectorIcon { Kind = Enum.TryParse<IconKind>(node.Icon, out var actionIcon) ? actionIcon : IconKind.More }, Ui.Text(node.Label));
                ToolTip.SetTip(action, L10n.T(node.Label)); return action;
            case "text":
                var text = Ui.Text(node.Label); text.TextTrimming = TextTrimming.CharacterEllipsis;
                if (node.Target is "CurrentTitle" or "CurrentArtist" or "PageTitle" or "VersionText") text.Bind(TextBlock.TextProperty, new Binding(node.Target) { Source = _vm });
                return text;
            case "icon": return new VectorIcon { Kind = Enum.TryParse<IconKind>(node.Icon, out var icon) ? icon : IconKind.Music, Width = 22, Height = 22 };
            case "artwork":
                var image = new Image { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
                image.Bind(Image.SourceProperty, new Binding(nameof(_vm.CurrentArtwork)) { Source = _vm }); return image;
            case "navigate":
                var destination = node.Page.StartsWith("view:", StringComparison.Ordinal) ? "interface:" + node.Page[5..] : node.Page;
                if (destination is not ("library" or "songs" or "playlists" or "history" or "albums" or "artists" or "statistics" or "devices" or "settings" or "plugins" or "terminal" or "favorites" or "lyrics" or "back") && !destination.StartsWith("interface:", StringComparison.Ordinal)) throw new InvalidDataException("Not an existing navigation destination.");
                var label = Ui.Text(node.Label); label.TextTrimming = TextTrimming.CharacterEllipsis;
                var body = new Grid { ColumnDefinitions = new("30,*") }; body.Children.Add(new VectorIcon { Kind = Enum.TryParse<IconKind>(node.Icon, out var glyph) ? glyph : IconKind.Music }); Grid.SetColumn(label, 1); body.Children.Add(label);
                var button = new Button { Content = body, Tag = destination == "favorites" ? "playlist:" + Playlist.LikedId : destination, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                button.Classes.Add("nav"); button.Classes.Set("selected", button.Tag?.ToString() == _vm.Page); ToolTip.SetTip(button, L10n.T(node.Label));
                button.Click += (_, _) => _vm.Navigate(destination == "back" ? _beforeLyricsPage : destination, destination.StartsWith("interface:", StringComparison.Ordinal) ? L10n.T(node.Label) : null); return button;
            case "playlists":
                // 复用原歌单菜单与拖动；本组件只有表现和导航，不增加业务功能。
                if (node.Presentation == "cards") return new InterfacePlaylistGallery(_vm, BuildPlaylistCard, BuildRemotePlaylistCard);
                return new InterfacePlaylistList(_vm, BuildPlaylistNavigationButton, BuildRemotePlaylistNavigationButton);
            default: return null;
        }
    }
}
