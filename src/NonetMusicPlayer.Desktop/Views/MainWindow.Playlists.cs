using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private int _playlistHeaderProfile = -1;
    private Border PlaylistArtwork(Playlist playlist, double size, bool original = false)
    {
        var artwork = new Grid();
        artwork.Children.Add(new VectorIcon { Kind = playlist.IsSystem ? IconKind.Heart : IconKind.Playlist, Filled = playlist.IsSystem, Width = size * .55, Height = size * .55, Brush = playlist.IsSystem ? new SolidColorBrush(Color.Parse("#F0526C")) : Ui.Brush("TextSecondaryBrush") });
        var image = _vm?.GetPlaylistArtwork(playlist) ?? playlist.Artwork;
        var path = _vm?.GetPlaylistArtworkPath(playlist) ?? playlist.CoverPath;
        artwork.Children.Add(original ? new OriginalArtworkImage(path, image) { Name = "PlaylistOriginalCover" }
            : new Image { Source = image, Stretch = Stretch.UniformToFill, IsVisible = image is not null });
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Math.Min(12, size / 5)), ClipToBounds = true, Background = Ui.Brush("SurfaceRaisedBrush"), Child = artwork };
    }
    private void BuildPlaylistHeader()
    {
        PlaylistHeader.Content = null; PlaylistHeader.IsVisible = _vm?.CurrentPlaylist is not null;
        PageHeadingLabel.IsVisible = _vm?.CurrentPlaylist is null;
        if (_vm?.CurrentPlaylist is not { } playlist) { BuildClassificationHeader(); return; }
        _playlistHeaderProfile = Bounds.Height < 620 ? 0 : Bounds.Width < 1000 || Bounds.Height < 650 ? 1 : 2;
        var small = Bounds.Width < 1000 || Bounds.Height < 650; var coverSize = small ? 112 : 180;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions((coverSize + 24) + ",*"), Margin = new(0, 0, 0, 6) };
        grid.Children.Add(PlaylistArtwork(playlist, coverSize, original: true));
        var title = Ui.RawText(PlaylistName(playlist), small ? 25 : 30); title.FontWeight = FontWeight.SemiBold; title.MaxLines = 1; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var description = string.IsNullOrWhiteSpace(playlist.Description) ? Ui.Text(L10n.T("Playlists.AddPlaylistDescription"), 13, true) : Ui.RawText(playlist.Description, 13, true);
        description.MaxLines = 2; description.TextTrimming = TextTrimming.CharacterEllipsis;
        var note = Ui.Text(L10n.Format("Common.Tracks", playlist.TrackIds.Count), 12, true);
        var edit = Ui.AsyncButton(L10n.T("Playlists.EditPlaylist"), () => EditPlaylistAsync(playlist));
        edit.Content = Ui.Actions(new VectorIcon { Kind = IconKind.Edit, Width = 18, Height = 18 }, Ui.Text(L10n.T("Playlists.EditPlaylist")));
        var play = Ui.Button(L10n.T("Playback.Play"), () => { if (_vm is not null) _ = _vm.PlayTrackAsync(_vm.VisibleTracks.FirstOrDefault(), _vm.VisibleTracks.ToArray()); });
        play.Content = Ui.Actions(new VectorIcon { Kind = IconKind.Play }, Ui.Text(L10n.T("Playback.Play")));
        var details = Ui.Stack(title, description, note, Ui.Actions(play, edit)); details.Spacing = small ? 5 : 10; details.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(details, 1); grid.Children.Add(details);
        PlaylistHeader.Margin = new(0, Bounds.Height < 620 ? 8 : 18, 0, 0);
        PlaylistHeader.Content = _playlistHeaderProfile == 0 ? new Expander { Header = Ui.RawText(PlaylistName(playlist), 18), Content = grid, IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch } : grid;
        grid.PointerPressed += (_, e) => { if (e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) { OpenPlaylistMenu(grid, playlist); e.Handled = true; } };
    }
    private void BuildClassificationHeader()
    {
        if (_vm is null || !(_vm.Page.StartsWith("album:") || _vm.Page.StartsWith("artist:"))) return;
        var artist = _vm.Page.StartsWith("artist:"); var name = _vm.Page[(artist ? 7 : 6)..];
        var cover = _vm.ResolveGroupCover(artist ? "artist" : "album", name);
        var image = new Border { Width = 140, Height = 140, CornerRadius = new(12), ClipToBounds = true, Background = Ui.Brush("SurfaceRaisedBrush"),
            Child = new Grid { Children = { new VectorIcon { Kind = artist ? IconKind.Artist : IconKind.Album, Width = 52, Height = 52 }, new Image { Source = cover, Stretch = Stretch.UniformToFill } } } };
        var play = Ui.Button(L10n.T("Playback.Play"), () => _ = _vm.PlayTrackAsync(_vm.VisibleTracks.FirstOrDefault(), _vm.VisibleTracks.ToArray()));
        play.Content = Ui.Actions(new VectorIcon { Kind = IconKind.Play }, Ui.Text(L10n.T("Playback.Play")));
        var more = Ui.Button("", () => OpenGroupMenu(image, artist ? "artist" : "album", name)); more.Name = "ClassificationMore"; more.Content = new VectorIcon { Kind = IconKind.More }; ToolTip.SetTip(more, L10n.T("Common.More9F07D2"));
        var details = Ui.Stack(Ui.RawText(name, 28), Ui.Text(L10n.Format("Common.Tracks", _vm.VisibleTracks.Count), 12, true), Ui.Actions(play, more)); details.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new("164,*") }; grid.Children.Add(image); Grid.SetColumn(details, 1); grid.Children.Add(details);
        PlaylistHeader.Content = grid; PlaylistHeader.IsVisible = true; PageHeadingLabel.IsVisible = false;
    }
    private async Task EditPlaylistAsync(Playlist playlist)
    {
        if (_vm is null) return;
        var result = await PlaylistEditDialog.Show(this, playlist);
        if (result is not null) _vm.UpdatePlaylistDetails(playlist, result.Name, result.Description, result.CoverPath);
    }
}
