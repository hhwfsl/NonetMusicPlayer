using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>管理页面生命周期、侧栏入口、歌词窗口和页面间导航。</summary>
public sealed partial class MainWindow
{
    private bool _applyingAppearance;
    private DesktopLyricsWindow? _desktopLyrics;
    private WindowsTaskbarService? _taskbar;
    private string _beforeLyricsPage = "library";
    private UserManualWindow? _manualWindow;
    private SettingsView? _cachedSettings;
    private AppSettings? _cachedSettingsProfile;
    private string? _cachedSettingsKey;
    private SettingsView SettingsPage()
    {
        var key = _vm!.Settings.Language + "|" + _vm.Storage.Root + "|" + _vm.Storage.BackupFolder + "|" + _vm.Lyrics.Folder + "|" + string.Join(";", _vm.Plugins.Installed.Where(p => p.Enabled).Select(p => p.Id + ":" + p.Version));
        if (_cachedSettings is null || !ReferenceEquals(_cachedSettingsProfile, _vm.Settings) || _cachedSettingsKey != key)
        {
            _cachedSettings?.Dispose(); _cachedSettings = new SettingsView(this, _vm); _cachedSettingsProfile = _vm.Settings; _cachedSettingsKey = key;
        }
        return _cachedSettings;
    }
    private static string PlaylistName(Playlist playlist) => playlist.IsSystem ? L10n.T("Playlists.LikedSongs") : playlist.Name;
    private void LanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => { _vm?.RefreshLocalizedText(); ApplyAppearance(); ShowPage(); _manualWindow?.Close(); });
    }
    private void DisposePage()
    {
        _restoreExpandedStyleControl?.Invoke();
        DisposeWorkspacePage();
        DisposeLyricsPage();
    }
    private void DisposeWorkspacePage()
    {
        if (AlternatePage.Content is IDisposable page && !ReferenceEquals(page, _cachedSettings)) page.Dispose();
        // 分类网格现在由包含返回栏的页面承载，退出时仍需显式释放已实现卡片的封面。
        _groupList?.Dispose();
        AlternatePage.Content = null; _groupList = null;
    }
    private void DisposeLyricsPage()
    {
        if (FullLyricsHost.Content is IDisposable lyrics) lyrics.Dispose();
        FullLyricsHost.Content = null;
    }
    private void SetWorkspacePageVisible(bool visible)
    {
        ContentSurface.IsVisible = PageContentGrid.IsVisible = SidebarPanel.IsVisible = visible;
    }
    private void ShowLyricsPage(bool animate)
    {
        _restoreExpandedStyleControl?.Invoke();
        DisposeLyricsPage();
        FullLyricsHost.Content = ExtensionReplacement("page.lyrics") ?? new LyricsView(this, _vm!);
        FullLyricsHost.IsVisible = true;
        RefreshElementExtensions();
        PositionFullLyrics();
        UpdateLayout();
        // 第一帧之前定位到真实播放行；保留原页控件直到转场完成，避免重采样和重新排版。
        var ready = FullLyricsHost.Content is not LyricsView view || view.PrepareEntrance();
        UpdateLayout();
        if (animate && ready)
        {
            var region = LyricsTransitionRegion();
            if (CaptureLyricsSnapshot(region) is { } snapshot)
            {
                PlayLyricsTransition(snapshot, region, opening: true, HideWorkspaceForLyrics);
                return;
            }
        }
        HideWorkspaceForLyrics();
    }
    private void HideWorkspaceForLyrics()
    {
        SetWorkspacePageVisible(false);
        DisposeWorkspacePage();
        _selectionPage = "lyrics";
        SetBatchMode(false);
    }
    private void ToggleLyrics()
    {
        if (_vm is null) return;
        if (_vm.Page == "lyrics") _vm.Navigate(_beforeLyricsPage);
        else { _beforeLyricsPage = _vm.Page; _vm.Navigate("lyrics"); }
    }
    private void DesktopLyrics_Click(object? sender, RoutedEventArgs e) => _desktopLyrics?.Toggle();
    private void RefreshDesktopLyricsButton()
    {
        var locked = _desktopLyrics?.IsLocked == true;
        var visible = _vm?.Settings.DesktopLyricsVisible == true;
        if (PlayerDesktopLyrics.Content is Controls.VectorIcon icon) icon.Kind = locked ? Controls.IconKind.Unlock : visible ? Controls.IconKind.DesktopLyricsHide : Controls.IconKind.DesktopLyricsShow;
        ToolTip.SetTip(PlayerDesktopLyrics, L10n.T(locked ? L10n.T("Lyrics.UnlockDesktopLyrics") : visible ? L10n.T("Lyrics.HideDesktopLyrics") : L10n.T("Lyrics.ShowDesktopLyrics")));
        Avalonia.Automation.AutomationProperties.SetName(PlayerDesktopLyrics, L10n.T(locked ? L10n.T("Lyrics.UnlockDesktopLyrics") : L10n.T("Lyrics.DesktopLyrics")));
    }
    internal void RefreshPluginNavigation() => BuildPluginNavigation();
    private void ExecuteSearch_Click(object? sender, RoutedEventArgs e) { if (_vm is not null) { _vm.SearchText = LibrarySearch.Text ?? ""; _vm.ApplyFilter(); } FocusManager?.Focus(null); }
    private void ToggleMyMusic_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return; _vm.Settings.MyMusicExpanded = !_vm.Settings.MyMusicExpanded; _vm.ApplySettings();
    }
    private void TogglePlugins_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return; _vm.Settings.PluginsExpanded = !_vm.Settings.PluginsExpanded; _vm.ApplySettings();
    }
    private void TogglePlaylists_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Settings.PlaylistsExpanded = !_vm.Settings.PlaylistsExpanded; _vm.ApplySettings();
    }
    private void BackToTop_Click(object? sender, RoutedEventArgs e)
    {
        // 只改变当前歌曲列表的视口，不修改选中项、播放来源或页面导航。
        _locateTimer.Stop(); ++_locateRequest;
        var scroll = TracksList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is not null) scroll.Offset = default;
    }
    private void PageBackToTop_Click(object? sender, RoutedEventArgs e)
    {
        var scroll = (AlternatePage.Content as Control)?.GetVisualDescendants().Prepend(AlternatePage.Content as Control).OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is not null) scroll.Offset = default;
    }
    private Rect LyricsTransitionRegion()
    {
        var layer = (Grid)WindowSurface.Child!;
        if (FullLyricsHost.TranslatePoint(default, layer) is not { } origin || PlayerSurface.TranslatePoint(default, layer) is not { } player) return default;
        var playerTop = InterfaceSettings.PlayerTop;
        var top = origin.Y;
        // 贴边歌词可覆盖侧栏顶部；默认布局仍保留整条标题栏的高度。
        if (InterfaceLyricsProfile?.EdgeToEdge != true && TitleBarSurface.TranslatePoint(default, layer) is { } title)
            top = Math.Max(top, title.Y + TitleBarSurface.Bounds.Height);
        if (playerTop) top = Math.Max(top, player.Y + PlayerSurface.Bounds.Height);
        var bottom = origin.Y + FullLyricsHost.Bounds.Height;
        if (!playerTop) bottom = Math.Min(bottom, player.Y);
        return new Rect(origin.X, top, FullLyricsHost.Bounds.Width, Math.Max(0, bottom - top));
    }
    private Bitmap? CaptureLyricsSnapshot(Rect region)
    {
        if (FullLyricsHost.TranslatePoint(default, (Grid)WindowSurface.Child!) is not { } origin) return null;
        return LyricsPageMotion.Capture(FullLyricsHost, new Rect(region.X - origin.X, region.Y - origin.Y, region.Width, region.Height));
    }
    private void PlayLyricsTransition(Bitmap snapshot, Rect region, bool opening, Action? settled = null)
    {
        var layer = (Grid)WindowSurface.Child!;
        var protectedAreas = new List<Rect>();
        // 只为实际标题栏留出空间。清蓝的品牌在侧栏内，应随侧栏一同被遮盖。
        if (TitleBarSurface.TranslatePoint(default, layer) is { } caption)
            protectedAreas.Add(new Rect(caption, TitleBarSurface.Bounds.Size));
        if (PlaybackSlider.IsEffectivelyVisible && PlaybackSlider.TranslatePoint(default, layer) is { } origin)
        {
            // 以滑块实际绘制位置为准；边缘余量覆盖高 DPI 的抗锯齿像素。
            var center = origin.Y + PlaybackSlider.ThumbCenter.Y;
            var radius = PlaybackSlider.ThumbRadius + 1;
            protectedAreas.Add(new Rect(origin.X, center - radius, PlaybackSlider.Bounds.Width, 2 * radius));
        }
        _lyricsMotion.Begin(layer, FullLyricsHost, snapshot, region, protectedAreas, [ContentSurface, SidebarPanel],
            [TitleBarSurface, TitleBrand, PlayerSurface, PlaybackSlider], opening,
            InterfaceSettings.PlayerTop, InterfaceMotionProfile?.LyricsDurationMs ?? 300, settled);
    }
    private void PositionFullLyrics()
    {
        var player = Workspace.Children.OfType<Controls.LayoutWidget>().FirstOrDefault(w => w.Key == "Player");
        var height = player?.Bounds.Height ?? (PlayerSurface.Bounds.Height > 0 ? PlayerSurface.Bounds.Height : _vm is null ? 96 : InterfaceSettings.PlayerHeight);
        FullLyricsHost.Margin = _vm is not null && InterfaceSettings.PlayerTop ? new(0, height + 52, 0, 0) : new(0, 0, 0, height);
    }
    private void UserManual_Click(object? sender, RoutedEventArgs e) => OpenUserManual();
    public void OpenUserManual()
    {
        if (_manualWindow is { IsVisible: true }) { _manualWindow.Activate(); _vm?.CompleteOperation("help.open"); return; }
        try
        {
            _manualWindow = new UserManualWindow(); _manualWindow.Closed += (_, _) => _manualWindow = null; _manualWindow.Show(this);
            _vm?.CompleteOperation("help.open");
        }
        catch (Exception error) { _vm?.ReportError(L10n.T("Common.UnableToOpenUserManual"), error); }
    }
    private void AttachPlaylistDrag(Button button, Playlist playlist)
    {
        PointerPressedEventArgs? press = null; Point start = default; var dragging = false;
        button.AddHandler(PointerPressedEvent, (_, e) => { if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return; press = e; start = e.GetPosition(button); }, RoutingStrategies.Tunnel, true);
        button.AddHandler(PointerReleasedEvent, (_, _) => press = null, RoutingStrategies.Tunnel, true);
        button.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            var delta = e.GetPosition(button) - start;
            if (press is null || dragging || !e.GetCurrentPoint(button).Properties.IsLeftButtonPressed || Math.Abs(delta.X) + Math.Abs(delta.Y) < 8) return;
            dragging = true; var saved = press; press = null; e.Handled = true;
            try { var data = new DataTransfer(); data.Add(DataTransferItem.CreateText("nonet-playlist:" + playlist.Id)); await DragDrop.DoDragDropAsync(saved, data, DragDropEffects.Move); }
            finally { dragging = false; HideDropPreview(); }
        }, RoutingStrategies.Tunnel, true);
        DragDrop.SetAllowDrop(button, true);
        button.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;
            if (_vm is null) return;
            if (e.DataTransfer.TryGetText() is { } payload && payload.StartsWith("nonet-playlist:", StringComparison.Ordinal))
                _vm.ReorderPlaylists(payload["nonet-playlist:".Length..], playlist.Id, e.GetPosition(button).Y < button.Bounds.Height / 2);
            else await DropAsync(e, playlist.IsSystem, playlist);
        });
    }
    private bool TryReorderTracks(DragEventArgs e)
    {
        if (CommitTrackReorderPreview(e)) return true;
        if (_vm?.CurrentPlaylist is not { IsSourceLinked: false } playlist || e.DataTransfer.TryGetText() is not { } payload || !payload.StartsWith("nonet-tracks:", StringComparison.Ordinal)) return false;
        var row = Ancestors(e.Source).OfType<ListBoxItem>().FirstOrDefault();
        if (row?.DataContext is not TrackItem target) return false;
        var ids = payload["nonet-tracks:".Length..].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _vm.ReorderPlaylistTracks(playlist, ids, target.Id, e.GetPosition(row).Y < row.Bounds.Height / 2); return true;
    }
}
