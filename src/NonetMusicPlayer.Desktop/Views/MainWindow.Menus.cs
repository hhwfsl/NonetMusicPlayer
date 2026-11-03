using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private ContextMenu? _activeMenu;
    private Control? _menuAnchor;
    private Control? _menuToggleAnchor;
    private void InitializeMenuToggle()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            // 在一次按下/释放手势中记住开关意图，避免原生轻触关闭先于 Click 完成时再次打开。
            var anchor = _menuToggleAnchor;
            if (anchor is not null && !new Avalonia.Rect(anchor.Bounds.Size).Contains(e.GetPosition(anchor))) _menuToggleAnchor = null;
        }, RoutingStrategies.Tunnel, true);
    }
    internal void OpenMenu(Control anchor, ContextMenu menu)
    {
        if (ReferenceEquals(_menuToggleAnchor, anchor))
        { _menuToggleAnchor = null; if (ReferenceEquals(_menuAnchor, anchor)) _activeMenu?.Close(); return; }
        // 外部轻触关闭会保留短暂的收起动画；同一按钮不能在这一帧又创建新菜单。
        if (ReferenceEquals(_menuAnchor, anchor) && _activeMenu?.IsOpen == true) { _activeMenu.Close(); return; }
        PopupSurfaceMotion.Attach(menu);
        _activeMenu?.Close();
        if (_menuAnchor is not null) _menuAnchor.ContextMenu = null;
        PlayerVolume.Flyout?.Hide();
        _activeMenu = menu; _menuAnchor = anchor; anchor.ContextMenu = menu;
        _menuToggleAnchor = anchor;
        menu.AddHandler(PointerPressedEvent, (_, e) => { if (Ancestors(e.Source).OfType<MenuItem>().Any()) _menuToggleAnchor = null; }, RoutingStrategies.Tunnel, true);
        menu.AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Escape or Key.Enter or Key.Space) _menuToggleAnchor = null; }, RoutingStrategies.Tunnel, true);
        menu.Opened += (_, _) =>
        {
            PopupSurfaceMotion.AllowAnchorInput(menu, anchor);
            var popup = menu.GetLogicalAncestors().OfType<Popup>().FirstOrDefault();
            if (popup is not null) { popup.OverlayDismissEventPassThrough = true; popup.OverlayInputPassThroughElement = anchor; }
        };
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
        AddExtensionMenu(menu, "playlist.more", new System.Text.Json.Nodes.JsonObject { ["playlistId"] = playlist.Id });
        OpenMenu(anchor, menu);
    }
}
