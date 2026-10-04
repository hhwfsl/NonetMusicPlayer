using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    /// <summary>首页只创建少量推荐卡片；完整分类页继续使用虚拟化列表。</summary>
    private Control MusicHome()
    {
        if (_vm is null) return Ui.Text("");
        var referenced = _vm.State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
        var tracks = _vm.State.Tracks.Where(t => referenced.Contains(t.Id)).ToArray();
        var albums = tracks.GroupBy(t => t.Album).OrderBy(g => g.Key).Select(g => new MusicGroup(g.Key, g.ToArray())).ToArray();
        var artists = tracks.GroupBy(t => t.Artist).OrderBy(g => g.Key).Select(g => new MusicGroup(g.Key, g.ToArray())).ToArray();
        var panel = Ui.Stack(); panel.Spacing = 22; panel.Name = "MusicHome"; panel.Margin = new(0, 0, 20, 0);
        var overview = Ui.Card("Home.Overview", Ui.Text(L10n.Format("Home.CollectionSummary", tracks.Length, albums.Length, artists.Length), 13, true),
            Ui.Actions(Ui.Button("Home.BrowseTracks", () => _vm.Navigate("songs")), Ui.Button("Playback.RecentlyPlayed", () => _vm.Navigate("history"))));
        panel.Children.Add(overview);
        if (tracks.Length == 0) panel.Children.Add(Ui.Card("Home.EmptyTitle", Ui.Text("Home.EmptyHint", 13, true)));
        panel.Children.Add(HomeGroups("Library.Albums", "album", albums));
        panel.Children.Add(HomeGroups("Library.Artists", "artist", artists));
        return Ui.Scroll(panel);
    }

    private Control HomeGroups(string title, string type, MusicGroup[] groups)
    {
        var heading = new Grid { ColumnDefinitions = new("*,Auto") };
        heading.Children.Add(Ui.Text(title, 18));
        var more = Ui.Button("Home.ViewAll", () => { _groupsReturnPage = _vm?.Page ?? "library"; _vm?.Navigate(type + "s"); }); more.Classes.Add("quiet"); Grid.SetColumn(more, 1); heading.Children.Add(more);
        var grid = new UniformGrid { Columns = 4 };
        foreach (var group in groups.Take(6))
        {
            var cover = new Border { Height = 92, CornerRadius = new(9), ClipToBounds = true, Background = Ui.Brush("SurfaceRaisedBrush"),
                Child = new Grid { Children = { new VectorIcon { Kind = type == "album" ? IconKind.Album : IconKind.Artist },
                    new Image { Source = _vm!.ResolveGroupCover(type, group.Name, group.Tracks), Stretch = Stretch.UniformToFill } } } };
            var label = Ui.RawText(group.Name, 13); label.TextWrapping = TextWrapping.NoWrap;
            var button = Ui.Button("", () => _vm!.Navigate(type + ":" + group.Name));
            button.Content = Ui.Stack(cover, label); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new(0, 0, 10, 10);
            ToolTip.SetTip(button, group.Name);
            button.PointerPressed += (_, e) => { if (e.GetCurrentPoint(button).Properties.IsRightButtonPressed) { OpenGroupMenu(button, type, group.Name); e.Handled = true; } };
            grid.Children.Add(button);
        }
        // 每个专辑/艺术家为独立卡片，低分辨率时减少列数，不横向溢出。
        grid.SizeChanged += (_, _) => grid.Columns = Math.Clamp((int)(grid.Bounds.Width / 145), 2, 6);
        return Ui.Stack(heading, grid);
    }
}
