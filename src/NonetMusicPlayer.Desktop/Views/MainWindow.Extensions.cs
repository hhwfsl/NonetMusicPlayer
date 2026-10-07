using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly List<Avalonia.Media.Imaging.Bitmap> _pluginNavigationAssets = [];
    private void BuildPluginNavigation()
    {
        foreach (var bitmap in _pluginNavigationAssets) bitmap.Dispose(); _pluginNavigationAssets.Clear();
        PluginNavigation.Children.Clear(); if (_vm is null) return;
        foreach (var plugin in _vm.Plugins.Installed.Where(p => p.Enabled && p.Type is "ui" or "widget" or "lyrics" or "agent" or "extension"))
        {
            var label = Ui.RawText(plugin.NavigationLabel); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
            var content = new Grid { ColumnDefinitions = new("30,*") }; content.Children.Add(new VectorIcon { Kind = Enum.TryParse<IconKind>(plugin.NavigationIcon, out var declaredIcon) ? declaredIcon : plugin.Permissions.Contains("lyrics-editor") || plugin.Type == "lyrics" ? IconKind.Lyrics : plugin.Type == "agent" ? IconKind.Plugins : IconKind.Game, Width = 20, Height = 20 }); if (plugin.NavigationImage.Length > 0)
            {
                try
                {
                    var path = Path.Combine(_vm.Plugins.ExtensionDirectory(plugin), plugin.NavigationImage); PluginPathPolicy.RejectLinkedAncestors(path);
                    if (new FileInfo(path).Length > 10_000_000) throw new InvalidDataException("Icon too large.");
                    using var source = File.OpenRead(path);
                    var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(source, 96); _pluginNavigationAssets.Add(bitmap);
                    content.Children.Clear(); content.Children.Add(new Image { Source = bitmap, Width = 22, Height = 22, Stretch = Stretch.Uniform });
                }
                catch (Exception error) { AppLog.Warning("Extensions", "Unable to read plugin icon.", error); }
            }
            Grid.SetColumn(label, 1); content.Children.Add(label);
            var button = new Button { Content = content, Tag = "plugin:" + plugin.Id, Padding = new(10, 0), Height = 44 }; button.Classes.Add("nav");
            ToolTip.SetTip(button, plugin.NavigationLabel); button.Click += (_, _) => _vm.Navigate("plugin:" + plugin.Id, plugin.NavigationLabel); PluginNavigation.Children.Add(button);
        }
        foreach (var (plugin, contribution) in _vm.Plugins.Contributions("navigation.items"))
        {
            var button = Ui.AsyncButton(contribution.Label, () => InvokeContributionAsync(plugin, contribution)); button.Classes.Add("nav");
            ToolTip.SetTip(button, contribution.Label); PluginNavigation.Children.Add(button);
        }
        PluginSection.IsVisible = PluginNavigation.Children.Count > 0;
    }
    private Control PluginPage(string page)
    {
        var plugin = _vm?.Plugins.Installed.FirstOrDefault(p => p.Enabled && p.Type is "ui" or "widget" or "lyrics" or "agent" or "extension" && "plugin:" + p.Id == page);
        if (plugin is null) return Ui.Card(L10n.T("Plugins.PluginIsDisabled"), Ui.Text(L10n.T("Plugins.EnableThisPluginInThePluginManagerFirst"), 13, true));
        try { return plugin.Type == "extension" ? new ExtensionPageView(_vm!.Plugins, plugin) : new PluginPageView(_vm!.Plugins, plugin, includeTitle: false); }
        catch (Exception e) { _vm!.ReportError(L10n.T("Plugins.UnableToOpenPluginPage"), e); return Ui.Card(L10n.T("Common.PageFailedToLoad"), Ui.Text(L10n.T("Plugins.DataIsUnchangedCheckThePluginSPageConfiguration"), 13, true)); }
    }
    private sealed record MusicGroup(string Name, TrackItem[] Tracks) { public int Count => Tracks.Length; }
    private VirtualizedCardGrid? _groupList;
    private MusicGroup[] _groups = [];
    private bool _groupsArtists;
    private string _groupsReturnPage = "library";
    private void UpdateGroupColumns(double width)
    {
        if (_groupList is null) return;
        _groupList.Refresh();
    }
    private Control Groups(bool artists)
    {
        if (_vm is null) return Ui.Text("");
        var groups = _vm.VisibleTracks.GroupBy(t => artists ? t.Artist : t.Album).OrderBy(g => g.Key).Select(g => new MusicGroup(g.Key, g.ToArray())).ToArray();
        var page = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
        var back = Ui.Button("Home.Back", () => _vm.Navigate(_groupsReturnPage));
        back.Name = "GroupsBack"; back.Content = Ui.Actions(new VectorIcon { Kind = IconKind.Back }, Ui.Text("Home.Back"));
        back.HorizontalAlignment = HorizontalAlignment.Left; back.Classes.Add("quiet");
        var header = new Grid { ColumnDefinitions = new("*,Auto") }; header.Children.Add(back);
        var top = Ui.IconButton(IconKind.BackToTop, "Common.BackToTop", () => { if (_groupList is not null) _groupList.Offset = default; }); top.Name = "GroupsBackToTop";
        Grid.SetColumn(top, 1); header.Children.Add(top); page.Children.Add(header);
        if (groups.Length == 0)
        {
            var empty = Ui.Card(L10n.T("Library.NoMusicYet"), Ui.Text(L10n.T("Playlists.AddMusicToAPlaylistToSeeAlbumsAnd"), 13, true));
            Grid.SetRow(empty, 1); page.Children.Add(empty); return page;
        }
        var type = artists ? "artist" : "album";
        _groups = groups; _groupsArtists = artists;
        // 分类卡片按可见行虚拟化，不一次性解码全部封面。
        _groupList = new VirtualizedCardGrid(groups.Length, index =>
            {
                var group = groups[index];
                var cover = new Border { Height = 150, CornerRadius = new(12), Background = Ui.Brush("SurfaceRaisedBrush"), ClipToBounds = true,
                    Child = new Grid { Children = { new VectorIcon { Kind = artists ? IconKind.Artist : IconKind.Album, Width = 30, Height = 30 }, new Image { Name = "MusicGroupCover", Source = _vm.ResolveGroupCover(type, group.Name, group.Tracks), Stretch = Stretch.UniformToFill } } } };
                var title = Ui.RawText(group.Name, 15); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
                var open = Ui.Button("", () => _vm.Navigate(type + ":" + group.Name, group.Name)); open.Classes.Add("quiet"); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                var details = Ui.Stack(cover, title, Ui.Text(L10n.Format("Common.Tracks", group.Count), 12, true)); details.Spacing = 8; open.Content = details;
                open.Name = "OpenMusicGroup";
                var card = new Grid { Name = "MusicGroupCard", Margin = new(0, 0, 12, 0) }; card.Children.Add(open);
                card.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(card).Properties.IsRightButtonPressed) return; OpenGroupMenu(card, type, group.Name); e.Handled = true; };
                return card;
            });
        UpdateGroupColumns(Math.Max(360, Bounds.Width - 210)); Grid.SetRow(_groupList, 1); page.Children.Add(_groupList); return page;
    }
    private void OpenGroupMenu(Control anchor, string type, string name)
    {
        if (_vm is null) return;
        var menu = new ContextMenu();
        menu.Items.Add(Menu(L10n.T("Common.ChangeArtwork"), async () => { var files = await OpenFilesAsync(L10n.T("Common.Choose") + (type == "artist" ? L10n.T("Library.Artists") : L10n.T("Library.Albums")) + L10n.T("Common.Artwork"), ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"], false); if (files.Length == 0) return; var cropped = await CoverCropDialog.Show(this, _vm.Storage, files[0]); if (cropped is null) return; _vm.SetGroupCover(type, name, cropped); ShowPage(); }, IconKind.Edit));
        menu.Items.Add(Menu(L10n.T("Common.RestoreDefaultCover"), () => { _vm.SetGroupCover(type, name, null); ShowPage(); return Task.CompletedTask; }, IconKind.Album));
        menu.Items.Add(Menu(L10n.T("Common.UseApplicationDefaultCover"), () => { _vm.SetGroupSoftwareDefaultCover(type, name); ShowPage(); return Task.CompletedTask; }, IconKind.Music)); AddExtensionMenu(menu, type + ".more", new() { ["name"] = name, ["type"] = type }); OpenMenu(anchor, menu);
    }
}
