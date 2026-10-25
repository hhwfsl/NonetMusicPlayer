using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    /// <summary>首页有界总览：只实现少量卡片，完整集合仍交给既有虚拟化页面。</summary>
    private Control MusicHome()
    {
        if (_vm is null) return Ui.Text("");
        var referenced = _vm.State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
        var tracks = _vm.State.Tracks.Where(t => referenced.Contains(t.Id)).ToArray();
        var albums = tracks.Select(t => t.Album).Distinct(StringComparer.Ordinal).Count();
        var artists = tracks.Select(t => t.Artist).Distinct(StringComparer.Ordinal).Count();
        var vm = _vm;
        var root = new Grid { Name = "MusicHome", RowDefinitions = new("Auto,*,Auto"), RowSpacing = 16, Margin = new(0, 0, 20, 0), ClipToBounds = true };
        root.Classes.Add("music-overview");
        var metrics = new UniformGrid { Columns = 4, Name = "HomeMetrics" };
        void Metric(string label, int count, IconKind icon, string page)
        {
            var number = Ui.RawText(count.ToString("N0", L10n.Culture), 26); number.FontWeight = FontWeight.SemiBold; number.TextWrapping = TextWrapping.NoWrap;
            var caption = Ui.Text(label, 12, true); caption.TextWrapping = TextWrapping.NoWrap;
            var content = new Grid { ColumnDefinitions = new("*,Auto") };
            var labels = Ui.Stack(number, caption); labels.Spacing = 0; content.Children.Add(labels); var glyph = new VectorIcon { Kind = icon, Width = 24, Height = 24, Brush = Ui.Brush("AccentBrush"), VerticalAlignment = VerticalAlignment.Center };
            glyph.Classes.Add("overview-metric-icon");
            Grid.SetColumn(glyph, 1); content.Children.Add(glyph);
            var button = Ui.Button("", () => vm.Navigate(page)); button.Content = content; button.Name = "HomeMetric" + page.Replace(":", "_");
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new(0, 0, 10, 0); button.Padding = new(16, 8);
            button.Classes.Add("overview-metric"); ToolTip.SetTip(button, L10n.T(label) + " · " + number.Text); metrics.Children.Add(button);
        }
        Metric("Home.Songs", tracks.Length, IconKind.Music, "songs");
        Metric("Library.Albums", albums, IconKind.Album, "albums");
        Metric("Library.Artists", artists, IconKind.Artist, "artists");
        Metric("Playlists.LikedSongs", vm.LikedPlaylist.TrackIds.Count, IconKind.Heart, "playlist:" + Playlist.LikedId);
        root.Children.Add(metrics);
        var main = new Grid { ColumnDefinitions = new("1.05*,*"), ColumnSpacing = 16 };
        var listening = new Grid { RowDefinitions = new("*,*"), RowSpacing = 14 };
        var hourCard = OverviewCard("Statistics.RecentTwentyFourHoursDuration", new HomeListeningChart(vm));
        // 保留原区域名称，使既有方案仍可重组这一首页区域。
        hourCard.Name = "HomeNowPlaying"; hourCard.Classes.Add("statistics-chart-card"); listening.Children.Add(hourCard);
        var byId = vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).DistinctBy(t => t.Id).ToDictionary(t => t.Id);
        var history = vm.State.History.Select(id => byId.GetValueOrDefault(id)).OfType<TrackItem>().Take(3).ToArray();
        var recent = new StackPanel { Spacing = 4 }; var recentRows = new List<Control>();
        foreach (var track in history)
        {
            var row = new Grid { ColumnDefinitions = new("36,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new(7), ClipToBounds = true, Background = Ui.Brush("SurfaceRaisedBrush"), Child = new Grid { Children = { new VectorIcon { Kind = IconKind.Music, Width = 18, Height = 18 }, new Image { Source = track.Artwork, Stretch = Stretch.UniformToFill } } } });
            var name = Ui.RawText(track.Title, 13); name.TextWrapping = TextWrapping.NoWrap; name.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(name, 1); row.Children.Add(name);
            var duration = Ui.RawText(track.DurationText, 11, true); duration.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(duration, 2); row.Children.Add(duration);
            var button = Ui.AsyncButton("", () => vm.PlayTrackAsync(track, history)); button.Content = row; button.Padding = new(6, 3); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Classes.Add("quiet"); ToolTip.SetTip(button, track.Title + " · " + track.Artist); recent.Children.Add(button); recentRows.Add(button);
        }
        if (history.Length == 0) recent.Children.Add(Ui.Text("Home.NoRecentTracks", 13, true));
        var recentCard = OverviewCard("Playback.RecentlyPlayed", recent, () => vm.Navigate("history")); recentCard.Name = "HomeRecent"; Grid.SetRow(recentCard, 1); listening.Children.Add(recentCard); main.Children.Add(listening);
        var playlists = HomePlaylists(); Grid.SetColumn(playlists, 1); main.Children.Add(playlists);
        Grid.SetRow(main, 1); root.Children.Add(main);
        var extra = ExtensionHomeCards(); extra.MaxHeight = 72; extra.ClipToBounds = true; Grid.SetRow(extra, 2); root.Children.Add(extra);
        // 根据可用面积缩减预览数量和封面尺寸，不依赖滚动条或缩放整页文字。
        root.SizeChanged += (_, _) =>
        {
            var small = root.Bounds.Height < 480;
            var tiny = root.Bounds.Height < 320;
            root.RowSpacing = small ? 10 : 16; metrics.Height = small ? 66 : 84;
            foreach (var glyph in metrics.GetVisualDescendants().OfType<VectorIcon>().Where(c => c.Classes.Contains("overview-metric-icon"))) glyph.IsVisible = root.Bounds.Width >= 640;
            listening.RowDefinitions = tiny ? new("*,0") : small ? new("1.3*,0.7*") : new("*,*");
            recentCard.IsVisible = !tiny; listening.RowSpacing = tiny ? 0 : 14;
            foreach (var card in new[] { hourCard, recentCard, playlists }.OfType<Border>()) card.Padding = new(tiny ? 12 : 16);
        };
        recentCard.SizeChanged += (_, _) => { var count = Math.Clamp((int)((recentCard.Bounds.Height - 66) / 42), 1, 3); for (var i = 0; i < recentRows.Count; i++) recentRows[i].IsVisible = i < count; };
        return root;
    }

    private Control HomePlaylists()
    {
        var vm = _vm!;
        var items = vm.Playlists.Take(4).ToArray();
        var columns = items.Length < 2 ? 1 : 2; var rows = Math.Max(1, (int)Math.Ceiling(items.Length / (double)columns));
        var grid = new UniformGrid { Columns = columns, Rows = rows };
        var covers = new List<Control>(); var buttons = new List<Button>();
        foreach (var playlist in items)
        {
            var button = BuildPlaylistNavigationButton(playlist); button.Classes.Remove("nav"); button.Classes.Add("quiet"); button.Classes.Add("overview-playlist"); button.Name = "HomePlaylistCard";
            var cover = PlaylistArtwork(playlist, 96); cover.HorizontalAlignment = HorizontalAlignment.Center; cover.Classes.Add("overview-playlist-cover"); covers.Add(cover);
            var name = Ui.RawText(PlaylistName(playlist), 13); name.TextWrapping = TextWrapping.NoWrap; name.HorizontalAlignment = HorizontalAlignment.Center;
            var count = Ui.RawText(L10n.Format("Common.Tracks", playlist.TrackIds.Count), 11, true); count.TextWrapping = TextWrapping.NoWrap; count.HorizontalAlignment = HorizontalAlignment.Center;
            var content = Ui.Stack(cover, name, count); content.Spacing = 4;
            button.Content = content; button.Height = double.NaN; button.VerticalAlignment = VerticalAlignment.Center; button.Padding = new(6); button.Margin = new(4); grid.Children.Add(button); buttons.Add(button);
        }
        // 方案提供歌单页时沿用方案，否则打开内置的虚拟化歌单页。
        var card = OverviewCard("Playlists.Playlists", grid, () => vm.Navigate(FindInterfacePage("playlists").Node is not null ? "interface:playlists" : "playlists"));
        card.Name = "HomePlaylists";
        // 每个封面固定为同边长；按单元格可用高度缩小，不拉伸成矩形。
        card.SizeChanged += (_, _) =>
        {
            var compact = card.Bounds.Height < 270;
            var count = compact ? Math.Min(items.Length, card.Bounds.Width < 240 ? 1 : 2) : items.Length;
            grid.Columns = count < 2 ? 1 : 2; grid.Rows = compact ? 1 : rows;
            for (var i = 0; i < buttons.Count; i++) buttons[i].IsVisible = i < count;
            var side = Math.Clamp(Math.Min((card.Bounds.Width - 2 * card.Padding.Left) / grid.Columns - 20, (card.Bounds.Height - 2 * card.Padding.Top - 40) / grid.Rows - 58), 24, 144);
            foreach (var cover in covers) { cover.Width = cover.Height = side; foreach (var icon in cover.GetVisualDescendants().OfType<VectorIcon>()) icon.Width = icon.Height = side * .55; }
        };
        return card;
    }
    private static Border OverviewCard(string title, Control body, Action? viewAll = null)
    {
        var header = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        var heading = Ui.Text(title, 15); heading.FontWeight = FontWeight.SemiBold; heading.TextWrapping = TextWrapping.NoWrap; heading.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(heading);
        if (viewAll is not null) { var more = Ui.IconButton(IconKind.More, "Home.ViewAll", viewAll); more.Name = "HomeSectionMore"; more.Width = more.Height = 30; more.MinHeight = 28; more.Classes.Add("quiet"); Grid.SetColumn(more, 1); header.Children.Add(more); }
        var content = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 10 }; content.Children.Add(header); Grid.SetRow(body, 1); content.Children.Add(body);
        var card = new Border { Padding = new(16), CornerRadius = new(14), Background = Ui.Brush("SurfaceBrush"), BorderThickness = new(0), Child = content, ClipToBounds = true };
        card.Classes.Add("surface-card"); card.Classes.Add("overview-card"); return card;
    }
}
