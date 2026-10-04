using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private ContextMenu? _activeMenu;
    private Control? _menuAnchor;
    internal void OpenMenu(Control anchor, ContextMenu menu)
    {
        _activeMenu?.Close();
        if (_menuAnchor is not null) _menuAnchor.ContextMenu = null;
        PlayerVolume.Flyout?.Hide();
        _activeMenu = menu; _menuAnchor = anchor; anchor.ContextMenu = menu;
        menu.Opened += (_, _) => { var popup = menu.GetLogicalAncestors().OfType<Popup>().FirstOrDefault(); if (popup is not null) popup.OverlayDismissEventPassThrough = true; };
        menu.Closed += (_, _) =>
        {
            if (_activeMenu != menu) return;
            _activeMenu = null; _menuAnchor = null; anchor.ContextMenu = null;
        };
        menu.Open(anchor);
    }
    private void OpenPlaylistMenu(Control anchor, Playlist playlist)
    {
        if (_vm is null) return;
        var menu = new ContextMenu { Placement = PlacementMode.Right };
        menu.Items.Add(Menu(L10n.T("Playlists.OpenPlaylist"), () => { _vm.Navigate("playlist:" + playlist.Id, playlist.Name); return Task.CompletedTask; }, IconKind.Playlist));
        menu.Items.Add(Menu(L10n.T("Playlists.PlayPlaylist"), async () =>
        {
            _vm.Navigate("playlist:" + playlist.Id, playlist.Name);
            var tracks = _vm.VisibleTracks.ToArray();
            await _vm.PlayTrackAsync(tracks.FirstOrDefault(), tracks);
        }, IconKind.Play));
        menu.Items.Add(Menu(L10n.T("Common.EditDescriptionAndCover"), () => EditPlaylistAsync(playlist), IconKind.Edit));
        if (!playlist.IsSystem)
        {
            menu.Items.Add(Menu(L10n.T("Playlists.RenamePlaylist"), () => RenameAsync(playlist), IconKind.Edit));
            menu.Items.Add(new Separator());
            menu.Items.Add(Menu(L10n.T("Playlists.DeletePlaylist"), () => DeleteAsync(playlist), IconKind.Trash));
        }
        OpenMenu(anchor, menu);
    }
}
