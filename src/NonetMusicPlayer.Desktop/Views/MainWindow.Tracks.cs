using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _locateTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private int _locateAttempts;
    private int _locateStableFrames, _locateRequest;
    private string? _locatePage, _locateTrackId;
    private bool _resetToTop;
    private bool CanLocatePlayingTrack => _vm?.CurrentPlaylist is not null && _vm.Page == _vm.PlayingSourcePage;
    private void InitializeTrackLocation()
    {
        _locateTimer.Tick += (_, _) =>
        {
            if (--_locateAttempts <= 0 || _vm?.Page != _locatePage || (!_resetToTop && (_vm?.CurrentTrack?.Id != _locateTrackId || !CanLocatePlayingTrack))) { _locateTimer.Stop(); return; }
            _locateStableFrames = (_resetToTop ? EnsureTrackListAtTop() : EnsurePlayingTrackVisible()) ? 0 : _locateStableFrames + 1;
            if (_locateStableFrames >= 2) _locateTimer.Stop();
        };
        // 用户主动滚动或选择优先于尚未完成的自动定位。
        void CancelLocation() { _locateTimer.Stop(); ++_locateRequest; }
        TracksList.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (_, _) => CancelLocation(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        TracksList.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, _) => CancelLocation(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }
    private TrackItem[] SelectedTracks => TracksList.SelectedItems?.OfType<TrackItem>().ToArray() ?? [];
    private string? _selectionPage;
    private bool _updatingSelection;
    private bool _changingBatchMode;
    private void SetBatchMode(bool enabled)
    {
        var changed = TracksList.BatchMode != enabled;
        _changingBatchMode = true;
        try
        {
        TracksList.BatchMode = enabled; SelectionToolbar.IsVisible = enabled;
        Application.Current!.Resources["TrackSelectionColumnWidth"] = new GridLength(enabled ? 32 : 0);
        TrackHeader.Children[0].IsVisible = enabled;
        BatchEntryButton.Classes.Set("selected", enabled);
        BatchEntryIcon.Kind = enabled ? IconKind.Close : IconKind.SelectAll; BatchEntryLabel.IsVisible = enabled;
        BatchEntryButton.Width = enabled ? double.NaN : 44; BatchEntryButton.Padding = enabled ? new Thickness(6, 0) : default;
        ToolTip.SetTip(BatchEntryButton, L10n.T(enabled ? L10n.T("Playlists.DoneSelecting") : L10n.T("Playlists.SelectTracks")));
        Avalonia.Automation.AutomationProperties.SetName(BatchEntryButton, L10n.T(enabled ? L10n.T("Playlists.DoneSelecting") : L10n.T("Playlists.SelectTracks")));
        if (!enabled) TracksList.SelectedItems?.Clear();
        UpdateSelectionToolbar();
        Responsive();
        }
        finally { _changingBatchMode = false; }
        if (changed) _vm?.CompleteOperation(enabled ? "selection.begin" : "selection.end");
    }
    private void BatchEntry_Click(object? sender, RoutedEventArgs e) => SetBatchMode(!TracksList.BatchMode);
    private void DoneBatch_Click(object? sender, RoutedEventArgs e) => SetBatchMode(false);
    private void UpdateSelectionToolbar()
    {
        if (_updatingSelection) return;
        _updatingSelection = true;
        try
        {
        if (!TracksList.BatchMode && SelectedTracks.Length > 0) TracksList.SelectedItems?.Clear();
        var count = SelectedTracks.Length;
        SelectionCountText.Text = L10n.Format("Common.Selected", count);
        BatchActionsButton.IsEnabled = count > 0;
        var all = count > 0 && count == (_vm?.VisibleTracks.Count ?? 0);
        SelectionToggle.IsChecked = count == 0 ? false : all ? true : null;
        SelectionToggle.Content = L10n.T(all ? L10n.T("Playlists.ClearSelection") : count == 0 ? L10n.T("Playlists.SelectAll") : L10n.T("Playlists.PartiallySelectedSelectAll"));
        SelectionToggle.IsEnabled = _vm?.VisibleTracks.Count > 0;
        }
        finally { _updatingSelection = false; }
    }
    private void SelectionToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (!TracksList.BatchMode) return;
        // 三态按钮自行切换状态；全选动作应以本次点击前的选择集合判断。
        if (SelectedTracks.Length > 0 && SelectedTracks.Length == _vm?.VisibleTracks.Count) TracksList.SelectedItems?.Clear();
        else TracksList.SelectAll();
        UpdateSelectionToolbar();
    }
    private async void TrackPlay_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is not null && (sender as Control)?.DataContext is TrackItem track) await _vm.PlayTrackAsync(track);
    }
    private async void TrackInfo_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as Control)?.DataContext is TrackItem track) await SongInfoDialog.Show(this, track);
    }
    private void TrackMore_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: TrackItem track } control) OpenTrackActions(control, [track]);
    }
    private void BatchActions_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && SelectedTracks.Length > 0) OpenTrackActions(control, SelectedTracks);
    }
    private void OpenTrackActions(Control anchor, TrackItem[] tracks)
    {
        if (_vm is null || tracks.Length == 0) return;
        var menu = new ContextMenu();
        menu.Items.Add(Menu(tracks.Length == 1 ? L10n.T("Playback.Play") : L10n.Format("Playback.PlaySelectedTracks", tracks.Length), async () =>
        {
            if (tracks.Length == 1) await _vm.PlayTrackAsync(tracks[0]);
            else await _vm.PlayTrackAsync(tracks[0], tracks);
        }, IconKind.Play));
        var liked = tracks.All(t => t.IsFavorite);
        menu.Items.Add(Menu(liked ? L10n.T("Playlists.RemoveFromLikedSongs") : L10n.T("Playlists.AddToLikedSongs"), () => { if (liked) _vm.UnfavoriteTracks(tracks); else _vm.FavoriteTracks(tracks); return Task.CompletedTask; }, IconKind.Heart));
        var destinations = new MenuItem { Header = L10n.T("Playlists.AddToPlaylist"), Icon = new VectorIcon { Kind = IconKind.Playlist, Width = 18, Height = 18 } };
        foreach (var playlist in _vm.Playlists)
        {
            var item = Menu(PlaylistName(playlist), () => { _vm.AddToPlaylist(playlist, tracks); return Task.CompletedTask; }, rawLabel: true);
            item.Icon = PlaylistArtwork(playlist, 22); destinations.Items.Add(item);
        }
        destinations.Items.Add(Menu(L10n.T("Playlists.CreatePlaylistAndAdd"), async () =>
        {
            var name = await PlayerDialog.Prompt(this, L10n.T("Playlists.NewPlaylist"), L10n.T("Playlists.CreateAPlaylistContainingTheSelectedTracks"), L10n.T("Playlists.NewPlaylist"));
            if (name is not null) _vm.AddToPlaylist(_vm.CreatePlaylist(name), tracks);
        }, IconKind.Plus));
        menu.Items.Add(destinations);
        if (tracks.Length == 1)
        {
            menu.Items.Add(Menu(L10n.T("Common.TrackInformation"), () => SongInfoDialog.Show(this, tracks[0]), IconKind.Info));
            if (_vm.CurrentPlaylist is not null)
            {
                menu.Items.Add(Menu(L10n.T("Playlists.MoveUpInPlaylist"), () => { _vm.MoveInPlaylist(tracks[0], -1); return Task.CompletedTask; }, IconKind.MoveUp));
                menu.Items.Add(Menu(L10n.T("Playlists.MoveDownInPlaylist"), () => { _vm.MoveInPlaylist(tracks[0], 1); return Task.CompletedTask; }, IconKind.MoveDown));
            }
        }
        if (_vm.Page == "history")
            menu.Items.Add(Menu("Library.RemoveFromRecent", () => { _vm.RemoveRecentTracks(tracks.Select(t => t.Id)); return Task.CompletedTask; }, IconKind.Trash));
        else if (_vm.CurrentPlaylist is not null || _vm.Page == "songs") {
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu(_vm.CurrentPlaylist is null ? L10n.T("Library.RemoveFromLibrary") : L10n.T("Playlists.RemoveFromThisPlaylist"), async () =>
        {
            var fromLibrary = _vm.CurrentPlaylist is null;
            if (await PlayerDialog.Confirm(this, L10n.Format("Common.RemoveTracks", tracks.Length), fromLibrary
                ? L10n.T("Playlists.RemoveLibraryAndPlaylistReferencesOnlyMusicFilesAre")
                : L10n.T("Playlists.RemoveOnlyFromThisPlaylistLibraryAndMusicFiles"), L10n.T("Common.Remove"))) _vm.RemoveTracks(tracks);
        }, IconKind.Trash));
        }
        OpenMenu(anchor, menu);
    }
    private void LocatePlayingTrack(object? sender, EventArgs e)
    {
        if (!CanLocatePlayingTrack) return;
        if (_windowInactive || WindowState == WindowState.Minimized || !IsVisible) return;
        _locateTimer.Stop(); var request = ++_locateRequest;
        Dispatcher.UIThread.Post(() =>
        {
            if (request != _locateRequest || !CanLocatePlayingTrack || _vm is null || !LibraryPage.IsVisible || !_vm.VisibleTracks.Any(t => t.IsPlayingHere)) return;
            EnsurePlayingTrackVisible();
            // 复用面板可能在下一帧恢复旧锚点；最多复查四帧，不持续跟随用户，也不改变选择。
            _resetToTop = false; _locateAttempts = 6; _locateStableFrames = 0; _locatePage = _vm.Page; _locateTrackId = _vm.CurrentTrack?.Id; _locateTimer.Start();
        }, DispatcherPriority.Loaded);
    }
    private bool EnsurePlayingTrackVisible()
    {
        if (_vm is null || !LibraryPage.IsVisible || !CanLocatePlayingTrack) return false;
        var track = _vm.VisibleTracks.FirstOrDefault(t => t.IsPlayingHere); if (track is null) return false;
        var index = _vm.VisibleTracks.IndexOf(track); UpdateLayout();
        var container = TracksList.ContainerFromIndex(index);
        var viewport = TracksList.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        if (container is not null && viewport is not null && container.TranslatePoint(default, viewport) is { } point && point.Y >= 0 && point.Y + container.Bounds.Height <= viewport.Bounds.Height + .5) return false;
        TracksList.ScrollIntoView(index); UpdateLayout();
        return true;
    }
    private void ResetSongPagePosition(object? sender, EventArgs e)
    {
        ++_viewportRequest;
        _locateTimer.Stop(); var request = ++_locateRequest;
        if (CanLocatePlayingTrack) { LocatePlayingTrack(sender, e); return; }
        var page = _vm?.Page;
        Dispatcher.UIThread.Post(() =>
        {
            if (request != _locateRequest || page != _vm?.Page || !LibraryPage.IsVisible) return;
            EnsureTrackListAtTop(); _resetToTop = true; _locatePage = page; _locateAttempts = 6; _locateStableFrames = 0; _locateTimer.Start();
        }, DispatcherPriority.Loaded);
    }
    private bool EnsureTrackListAtTop()
    {
        UpdateLayout();
        var scroll = TracksList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var changed = scroll is not null && scroll.Offset.Y > .5;
        if (_vm?.VisibleTracks.Count > 0) TracksList.ScrollIntoView(0);
        if (scroll is not null) scroll.Offset = default;
        UpdateLayout(); return changed;
    }
}
