using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private bool _allowClose, _confirmingClose;
    private PointerPressedEventArgs? _dragPress;
    private Point _dragStart;
    private bool _dragging;
    private TrackItem? _dragTrack;
    private readonly HashSet<Key> _handledShortcutKeys = [];
    public MainWindow()
    {
        InitializeComponent();
        InputCommitService.Install(this);
        InputCommitService.Bind(LibrarySearch, text => { if (_vm is not null) _vm.SearchText = text; });
        InitializeTrayLifecycle();
        InitializeViewportContinuity();
        InitializeWindowPlacement();
        DataContextChanged += (_, _) => AttachViewModel();
        SizeChanged += (_, _) => Responsive();
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) { WindowSurface.CornerRadius = WindowState == WindowState.Maximized ? new(0) : new(12); WindowSurface.BorderThickness = WindowState == WindowState.Maximized ? new(0) : new(1); RefreshResizeBorders(); } };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Library_Drop);
        AddHandler(KeyDownEvent, (_, e) => RecoverExtensionInterface(e), RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, Shortcuts, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space) ActiveLyricsTimingTool()?.ReleaseAnnotationKey(); if (_handledShortcutKeys.Remove(e.Key)) e.Handled = true; }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => { _handledShortcutKeys.Clear(); ActiveLyricsTimingTool()?.ReleaseAnnotationKey(); };
        TracksList.AddHandler(PointerPressedEvent, TracksPressed, RoutingStrategies.Tunnel);
        TracksList.AddHandler(PointerMovedEvent, TracksMoved);
        TracksList.PointerReleased += (_, _) => _dragPress = null;
        InitializeSeekInteraction();
        InitializeNotifications();
        InitializeUpdates();
        InitializeTrackLocation();
        TracksList.SelectionChanged += (_, _) => { UpdateSelectionToolbar(); if (TracksList.BatchMode && !_changingBatchMode) _vm?.CompleteOperation("selection.select"); };
        SetBatchMode(false);
        InitializeTitleInteraction();
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_vm is null) return;
            var touch = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
            if (_vm.Settings.TouchMode != touch) { _vm.Settings.TouchMode = touch; ApplyAppearance(); }
        }, RoutingStrategies.Tunnel, true);
        Workspace.LayoutChanged += (_, _) => SaveRegionResize();
        L10n.LanguageChanged += LanguageChanged;
        PropertyChanged += (_, e) => { if (e.Property == ActualThemeVariantProperty && !_applyingAppearance) ApplyAppearance(); };
        Closing += ConfirmClosing;
        Closed += (_, _) => { foreach (var bitmap in _pluginNavigationAssets) bitmap.Dispose(); _pluginNavigationAssets.Clear(); foreach (var overlay in _extensionOverlays.Values.ToArray()) overlay.Close(); _extensionOverlays.Clear(); L10n.LanguageChanged -= LanguageChanged; _cachedSettings?.Dispose(); _desktopLyrics?.Dispose(); _taskbar?.Dispose(); _notificationTimer.Stop(); _locateTimer.Stop(); DisposePage(); if (_vm is not null) { DetachPlayerCommands(); _vm.Plugins.DetachHost(this); _vm.ViewChanged -= ViewChanged; _vm.NavigationRequested -= ResetSongPagePosition; _vm.SettingsChanged -= SettingsChanged; _vm.LocatePlayingTrack -= LocatePlayingTrack; _vm.UserNotification -= UserNotification; _vm.Save(); } };
    }
    private void AttachViewModel()
    {
        if (_vm is not null) { DetachPlayerCommands(); _vm.Plugins.DetachHost(this); _vm.ViewChanged -= ViewChanged; _vm.NavigationRequested -= ResetSongPagePosition; _vm.SettingsChanged -= SettingsChanged; _vm.LocatePlayingTrack -= LocatePlayingTrack; _vm.UserNotification -= UserNotification; }
        _desktopLyrics?.Dispose(); _taskbar?.Dispose();
        _vm = DataContext as MainViewModel; if (_vm is null) return;
        _vm.ViewChanged += ViewChanged; _vm.SettingsChanged += SettingsChanged; Workspace.Profile = _vm.Settings;
        _vm.NavigationRequested += ResetSongPagePosition;
        _vm.LocatePlayingTrack += LocatePlayingTrack; _vm.UserNotification += UserNotification;
        L10n.SetLanguage(_vm.Settings.Language);
        _desktopLyrics = new DesktopLyricsWindow(this, _vm); _desktopLyrics.StateChanged += (_, _) => { RefreshDesktopLyricsButton(); DesktopLyricsOperationChanged(); }; RefreshDesktopLyricsButton(); _taskbar = WindowsTaskbarService.Attach(this, _vm);
        _vm.Plugins.AttachHost(this, _vm);
        InitializePlayerCommands();
        InitializeLayoutConfiguration(); ApplyAppearance(); ShowPage(); ResetSongPagePosition(this, EventArgs.Empty);
    }
    private string? _displayedPage;
    private void ViewChanged(object? sender, EventArgs e)
    {
        // 普通状态变化保留页面；插件被关闭后其资源已释放，重新打开时必须新建页面。
        if (_vm is not null && _displayedPage == _vm.Page && (_vm.Page is "terminal" or "settings" or "lyrics" || _vm.Page.StartsWith("plugin:") && AlternatePage.Content is PluginPageView { IsDisposed: false }))
        { BuildPlaylists(); BuildPluginNavigation(); return; }
        ShowPage();
    }
    private void SettingsChanged(object? sender, EventArgs e) { Terminal?.Configure(GetTerminalOptions()); ApplyAppearance(); BuildPluginNavigation(); BuildExtensionSlots(); }
    private void ApplyAppearance()
    {
        if (_vm is null || _applyingAppearance) return; var s = _vm.Settings; _applyingAppearance = true;
        try {
        L10n.SetLanguage(s.Language); FontSize = s.FontSize;
        try { Application.Current!.Resources["AppFontFamily"] = AppFontService.Resolve(_vm.Storage, s); }
        catch (Exception error)
        {
            s.FontFilePath = null; s.FontFamily = ""; Application.Current!.Resources["AppFontFamily"] = AppFontService.Resolve(_vm.Storage, s);
            _vm.ReportError(L10n.T("Settings.CustomFontCouldNotBeReadUsingTheDefault"), error);
        }
        Classes.Set("touch", s.TouchMode);
        PlaybackSlider.Height = PlaybackSlider.MinHeight = s.TouchMode ? 40 : 24;
        ((Grid)PlayerColumns.Parent!).RowDefinitions[0].Height = new GridLength(0);
        PlaybackSlider.Margin = new Thickness(0, s.TouchMode ? -20 : -12, 0, 0);
        PlaybackSlider.ThumbRadius = s.TouchMode ? 9 : 6; PlaybackSlider.InvalidateVisual();
        // 跳转与调整高度的命中区域相邻且不得重叠；鼠标区域深入边界 12 DIP，触摸区域为 20 DIP。
        PlayerResizeGrip.Height = s.TouchMode ? 16 : 12; PlayerResizeGrip.Margin = new(0, s.TouchMode ? 20 : 12, 0, 0);
        Opacity = s.UiOpacity;
        foreach (var widget in Workspace.Children.OfType<LayoutWidget>()) widget.Label = L10n.T(widget.Key switch { "Navigation" => L10n.T("Common.Navigation"), "Content" => L10n.T("Common.Content"), _ => L10n.T("Playback.Player") });
        var palette = new Dictionary<string, string>();
        foreach (var plugin in _vm.Plugins.Installed.Where(p => p.Enabled && (p.Type == "theme" || p.Type == "extension" && p.Permissions.Contains("ui-extend"))))
            foreach (var token in plugin.Tokens) palette[token.Key] = token.Value;
        ThemeService.Apply(s, palette);
        AppBackgroundImage.Source = AppBackgroundService.GetImage(s.BackgroundImagePath);
        AppBackgroundImage.Opacity = Math.Clamp(s.BackgroundImageOpacity, 0, 1);
        AppBackgroundImage.Stretch = Enum.TryParse<Stretch>(s.BackgroundImageStretch, out var stretch) ? stretch : Stretch.UniformToFill;
        MyMusicNavigation.IsVisible = s.MyMusicExpanded; MyMusicChevron.Kind = s.MyMusicExpanded ? IconKind.ChevronUp : IconKind.ChevronDown;
        PluginNavigation.IsVisible = s.PluginsExpanded; PluginsChevron.Kind = s.PluginsExpanded ? IconKind.ChevronUp : IconKind.ChevronDown;
        PlaylistNavigation.IsVisible = s.PlaylistsExpanded;
        PlaylistsChevron.Kind = s.PlaylistsExpanded ? IconKind.ChevronUp : IconKind.ChevronDown;
        ToolTip.SetTip(PlaylistsCollapseButton, L10n.T(s.PlaylistsExpanded ? "Common.Collapse" : "Common.Expand"));
        Avalonia.Automation.AutomationProperties.SetName(PlaylistsCollapseButton, L10n.T(s.PlaylistsExpanded ? "Common.Collapse" : "Common.Expand"));
        ApplyTitleButtons();
        RefreshDesktopLyricsButton();
        foreach (var (control, id, label) in new[] { (PlayerPlay, "playPause", L10n.T("Playback.PlayPause")), (PlayerPrevious, "previous", L10n.T("Common.Previous")), (PlayerNext, "next", L10n.T("Common.Next")), (PlayerFavorite, "favorite", L10n.T("Common.LikeUnlike")) })
        {
            var action = ShortcutService.Actions.Single(a => a.Id == id); var gesture = ShortcutService.Get(s.KeyBindings, action);
            ToolTip.SetTip(control, L10n.T(label) + (string.IsNullOrEmpty(gesture) ? "" : " (" + gesture + ")"));
        }
        SetWorkspaceProfile(_safeLayout ? LayoutConfiguration.Read(UiLayoutService.DefaultJson) : LayoutConfiguration.Current);
        SidebarPanel.BorderThickness = s.NavigationRight ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        Responsive(); RequestLayoutCheck();
        PositionPlayerResizeGrip();
        } finally { _applyingAppearance = false; }
    }
    private void Responsive()
    {
        var contentWidth = Workspace.Children.OfType<LayoutWidget>().FirstOrDefault(w => w.Key == "Content")?.Bounds.Width ?? Bounds.Width;
        var narrow = contentWidth < 1000;
        SearchContainer.Width = Math.Min(300, Math.Max(125, contentWidth - (_vm?.CurrentPlaylist is null ? 150 : 260) - (TracksList.BatchMode ? 430 : 0)));
        var hideMetadata = contentWidth < 740;
        var showArtwork = contentWidth >= 360; var showDuration = contentWidth >= 400;
        Application.Current!.Resources["TrackArtworkColumnWidth"] = new GridLength(showArtwork ? 48 : 0);
        Application.Current.Resources["TrackDurationColumnWidth"] = new GridLength(showDuration ? 56 : 0);
        Application.Current.Resources["ShowTrackArtwork"] = showArtwork;
        Application.Current.Resources["ShowTrackDuration"] = showDuration;
        TrackHeader.ColumnDefinitions[2].Width = new GridLength(showArtwork ? 48 : 0);
        TrackHeader.ColumnDefinitions[6].Width = new GridLength(showDuration ? 56 : 0);
        foreach (var label in TrackHeader.Children.OfType<TextBlock>().Where(c => Grid.GetColumn(c) is 2 or 6)) label.IsVisible = Grid.GetColumn(label) == 2 ? showArtwork : showDuration;
        foreach (var button in LibraryActionsFlow.Children.OfType<Button>()) button.Margin = new(6, 0, 0, 0);
        LibraryAddFiles.IsVisible = LibraryAddFolder.IsVisible = _vm?.CurrentPlaylist is not null;
        PageContentGrid.Margin = Bounds.Height < 620 ? new(18, 12, 18, 6) : new(30, 24, 30, 12);
        LibraryActions.Margin = Bounds.Height < 620 ? new(0, 8, 0, 0) : new(0, 18, 0, 0);
        var profile = Bounds.Height < 620 ? 0 : Bounds.Width < 1000 || Bounds.Height < 650 ? 1 : 2;
        if (_vm?.CurrentPlaylist is not null && _playlistHeaderProfile != profile) BuildPlaylistHeader();
        var artist = hideMetadata ? 0 : narrow ? 88 : 130; var album = hideMetadata ? 0 : narrow ? 88 : 160;
        Application.Current!.Resources["ArtistColumnWidth"] = new GridLength(artist);
        Application.Current.Resources["AlbumColumnWidth"] = new GridLength(album);
        Application.Current.Resources["ShowTrackMetadata"] = !hideMetadata;
        Application.Current.Resources["TrackActionsColumnWidth"] = new GridLength(_vm?.Settings.TouchMode == true ? 176 : 144);
        Application.Current.Resources["TrackActionColumnWidth"] = new GridLength(_vm?.Settings.TouchMode == true ? 44 : 36);
        foreach (var label in TrackHeader.Children.OfType<TextBlock>().Where(c => Grid.GetColumn(c) is 4 or 5)) label.IsVisible = !hideMetadata;
        TrackHeader.ColumnDefinitions[7].Width = new GridLength(_vm?.Settings.TouchMode == true ? 176 : 144);
        foreach (var button in Workspace.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("nav")))
            button.Height = _vm?.Settings.TouchMode == true ? 48 : Bounds.Height < 760 ? 38 : button.Tag?.ToString()?.StartsWith("playlist:") == true ? 46 : 44;
        UpdateGroupColumns(contentWidth); PositionFullLyrics();
        PositionPlayerResizeGrip();
    }
    private void PositionPlayerResizeGrip(bool defaultLayout = false)
    {
        var top = _vm?.Settings.PlayerTop == true;
        PlayerResizeGrip.VerticalAlignment = top ? Avalonia.Layout.VerticalAlignment.Bottom : Avalonia.Layout.VerticalAlignment.Top;
        var inset = _vm?.Settings.TouchMode == true ? 20 : 12;
        PlayerResizeGrip.Margin = top ? new Thickness(0, 0, 0, inset) : new Thickness(0, inset, 0, 0);
        if (LayoutConfiguration is null || (!defaultLayout && !_safeLayout && LayoutConfiguration.AppliedJson != UiLayoutService.DefaultJson)) return;
        if (PlayerPlay.Parent is Grid transport)
            transport.Margin = top ? new(transport.Margin.Left, 0, transport.Margin.Right, _vm?.Settings.TouchMode == true ? 12 : 4) : new(transport.Margin.Left, _vm?.Settings.TouchMode == true ? 12 : 4, transport.Margin.Right, 0);
    }
    public void ShowPage()
    {
        if (_vm is null) return;
        _displayedPage = _vm.Page;
        _activeMenu?.Close();
        if (_selectionPage != _vm.Page) { _selectionPage = _vm.Page; SetBatchMode(false); }
        DisposePage(); BuildPlaylists(); BuildPluginNavigation(); BuildExtensionSlots();
        var slot = _vm.Page switch { "library" => "page.music", "songs" => "page.songs", "history" => "page.history",
            "albums" => "page.albums", "artists" => "page.artists", "statistics" => "page.statistics", _ => _vm.Page.StartsWith("playlist:") ? "page.playlist" : "" };
        var replacement = slot.Length > 0 ? ExtensionReplacement(slot) : null;
        var alternate = replacement is not null || _vm.Page is "library" or "terminal" or "settings" or "plugins" or "albums" or "artists" or "statistics" || _vm.Page.StartsWith("plugin:", StringComparison.Ordinal);
        var lyrics = _vm.Page == "lyrics"; FullLyricsHost.IsVisible = lyrics; ContentSurface.IsVisible = !lyrics; PageContentGrid.IsVisible = !lyrics; SidebarPanel.IsVisible = !lyrics;
        LibraryPage.IsVisible = !alternate; AlternatePage.IsVisible = alternate;
        LibraryActions.IsVisible = !alternate; SearchContainer.IsVisible = !alternate; TrackCountLabel.IsVisible = !alternate;
        UserManualButton.IsVisible = _vm.Page == "settings";
        PageBackToTop.IsVisible = _vm.Page is "library" or "plugins" or "statistics";
        BuildPlaylistHeader(); UpdateSelectionToolbar();
        if (_vm.Page.StartsWith("plugin:") && _vm.Plugins.Installed.FirstOrDefault(p => "plugin:" + p.Id == _vm.Page && p.Type == "extension") is { } extension)
            PageHeading.IsVisible = !_vm.Plugins.LoadExtensionPage(extension).OwnsHeader;
        else PageHeading.IsVisible = true;
        AlternatePage.Content = replacement ?? (_vm.Page switch
        {
            "library" => MusicHome(), "terminal" => new TerminalView(this), "settings" => SettingsPage(), "plugins" => new PluginsView(this, _vm),
            "albums" => Groups(false), "artists" => Groups(true), "statistics" => new StatisticsView(_vm), _ => _vm.Page.StartsWith("plugin:") ? PluginPage(_vm.Page) : null
        });
        if (lyrics) FullLyricsHost.Content = ExtensionReplacement("page.lyrics") ?? new LyricsView(this, _vm);
        foreach (var nav in Workspace.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("nav")))
        { nav.Classes.Set("selected", nav.Tag?.ToString() == _vm.Page); }
        Dispatcher.UIThread.Post(Responsive, Avalonia.Threading.DispatcherPriority.Loaded);
        LocatePlayingTrack(this, EventArgs.Empty);
    }
    private void BuildPlaylists()
    {
        if (_vm is null) return; PlaylistNavigation.Children.Clear();
        foreach (var playlist in _vm.Playlists)
        {
            var label = new TextBlock { Text = PlaylistName(playlist), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*") };
            content.Children.Add(PlaylistArtwork(playlist, 28)); Grid.SetColumn(label, 1); content.Children.Add(label);
            var button = new Button { Content = content, Tag = "playlist:" + playlist.Id, Padding = new Thickness(10, 0), Height = 46 }; button.Classes.Add("nav"); ToolTip.SetTip(button, playlist.Name);
            button.Click += (_, _) => _vm.Navigate("playlist:" + playlist.Id, playlist.Name);
            AttachPlaylistDrag(button, playlist);
            button.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(button).Properties.IsRightButtonPressed) return; OpenPlaylistMenu(button, playlist); e.Handled = true; };
            PlaylistNavigation.Children.Add(button);
        }
    }
    private void Navigate_Click(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string page }) _vm?.Navigate(page); }
    private void PlayerResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e) => Workspace.BeginPlayerResize(e);
    private void ShowLyrics_Click(object? sender, RoutedEventArgs e) => ToggleLyrics();
    private void ClearSearch_Click(object? sender, RoutedEventArgs e) { if (_vm is not null) _vm.SearchText = ""; }
    private async void AddFiles_Click(object? sender, RoutedEventArgs e) => await AddFilesAsync();
    private async Task AddFilesAsync()
    {
        if (_vm?.CurrentPlaylist is null) { _vm?.ReportWarning(L10n.T("Playlists.SelectAPlaylistBeforeAddingMusic")); return; }
        var paths = await OpenFilesAsync(L10n.T("Library.AddMusicFiles"), Services.MusicLibraryScanner.SupportedExtensions.Select(x => "*" + x).ToArray());
        if (paths.Length > 0 && _vm is not null) await _vm.ImportAsync(paths, _vm.CurrentPlaylist?.IsSystem == true, _vm.CurrentPlaylist);
    }
    private async void AddFolder_Click(object? sender, RoutedEventArgs e) => await AddFolderAsync();
    private async Task AddFolderAsync()
    {
        if (_vm?.CurrentPlaylist is null) { _vm?.ReportWarning(L10n.T("Playlists.SelectAPlaylistBeforeAddingMusic")); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L10n.T("Library.AddMusicFolder"), AllowMultiple = true });
        var paths = folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length > 0 && _vm is not null) await _vm.ImportAsync(paths, _vm.CurrentPlaylist?.IsSystem == true, _vm.CurrentPlaylist);
    }
    private void CancelImport_Click(object? sender, RoutedEventArgs e) => _vm?.CancelImport();
    private void PlayAll_Click(object? sender, RoutedEventArgs e) { if (_vm is not null) _ = _vm.PlayTrackAsync(_vm.VisibleTracks.FirstOrDefault(), _vm.VisibleTracks.ToArray()); }
    private async void TracksList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || Ancestors(e.Source).Any(c => c is Button or CheckBox)) return;
        var track = Ancestors(e.Source).OfType<Control>().Select(c => c.DataContext).OfType<TrackItem>().FirstOrDefault() ?? TracksList.SelectedItem as TrackItem;
        await _vm.PlayTrackAsync(track);
    }
    private static IEnumerable<Visual> Ancestors(object? source) => source is Visual visual ? visual.GetVisualAncestors().Prepend(visual) : [];
    private void TrackFavorite_Click(object? sender, RoutedEventArgs e) { _vm?.ToggleFavorite((sender as Control)?.DataContext as TrackItem); e.Handled = true; }
    private void CurrentFavorite_Click(object? sender, RoutedEventArgs e) => _vm?.ToggleFavorite(_vm.CurrentTrack);
    private void PlayMode_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Control control) return;
        var menu = new ContextMenu();
        menu.Placement = PlacementMode.Top;
        foreach (var (mode, label, icon) in new[] { (PlayMode.RepeatAll, L10n.T("Playback.RepeatAll"), IconKind.RepeatAll), (PlayMode.RepeatOne, L10n.T("Playback.RepeatOne8CDE79"), IconKind.RepeatOne), (PlayMode.Shuffle, L10n.T("Playback.ShuffleF34B7C"), IconKind.Shuffle) })
        {
            var item = Menu((_vm.Settings.PlayMode == mode ? "✓  " : "    ") + L10n.T(label), () => { _vm.SetPlayMode(mode); return Task.CompletedTask; });
            item.Tag = mode; menu.Items.Add(item);
            item.Icon = new VectorIcon { Kind = icon, Width = 20, Height = 20 };
        }
        OpenMenu(control, menu);
    }
    private void PlayerMore_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Control control) return;
        var menu = new ContextMenu { Placement = PlacementMode.Top }; var track = _vm.CurrentTrack;
        var playlists = new MenuItem { Header = L10n.T("Playlists.AddToPlaylist"), IsEnabled = track is not null, Icon = new VectorIcon { Kind = IconKind.Playlist } };
        foreach (var playlist in _vm.Playlists) playlists.Items.Add(Menu(PlaylistName(playlist), () => { if (track is not null) _vm.AddToPlaylist(playlist, [track]); return Task.CompletedTask; }, rawLabel: true));
        playlists.Items.Add(Menu(L10n.T("Playlists.CreatePlaylistAndAdd"), async () =>
        {
            var name = await PlayerDialog.Prompt(this, L10n.T("Playlists.NewPlaylist"), L10n.T("Playlists.CreateAPlaylistContainingTheCurrentTrack"), L10n.T("Playlists.NewPlaylist"));
            if (name is null || track is null) return;
            try { _vm.AddToPlaylist(_vm.CreatePlaylist(name), [track]); BuildPlaylists(); } catch (Exception exception) { _vm.ReportError(L10n.T("Common.CreationFailed"), exception); }
        })); menu.Items.Add(playlists);
        var info = Menu(L10n.T("Common.TrackInformation"), async () => { if (track is not null) await SongInfoDialog.Show(this, track); }, IconKind.Info);
        info.IsEnabled = track is not null; menu.Items.Add(info);
        OpenMenu(control, menu);
    }
    private async void CreatePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return; var name = await PlayerDialog.Prompt(this, L10n.T("Playlists.NewPlaylist"), L10n.T("Playlists.ChooseANameForTheNewPlaylist"), L10n.T("Playlists.NewPlaylist")); if (name is null) return;
        try { var playlist = _vm.CreatePlaylist(name); _vm.Navigate("playlist:" + playlist.Id); } catch (Exception exception) { _vm.ReportError(L10n.T("Common.CreationFailed"), exception); }
    }
    private async Task RenameAsync(Playlist playlist)
    {
        var name = await PlayerDialog.Prompt(this, L10n.T("Playlists.RenamePlaylist"), L10n.T("Playlists.PlaylistNamesMustContainCharacters"), playlist.Name); if (name is null || _vm is null) return;
        try { _vm.RenamePlaylist(playlist, name); } catch (Exception e) { _vm.ReportError(L10n.T("Common.RenameFailed"), e); }
    }
    private async Task DeleteAsync(Playlist playlist) { if (_vm is not null && await PlayerDialog.Confirm(this, L10n.T("Playlists.DeletePlaylist858586"), L10n.T("Playlists.MusicFilesAreKeptCtrlZCanUndoThis"), L10n.T("Playlists.DeletePlaylist"))) _vm.DeletePlaylist(playlist); }
    private void TracksPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || Workspace.Editing) return;
        var track = Ancestors(e.Source).OfType<Control>().Select(c => c.DataContext).OfType<TrackItem>().FirstOrDefault(); if (track is null) return;
        if (e.GetCurrentPoint(TracksList).Properties.IsRightButtonPressed && e.Source is Control anchor) { OpenTrackActions(anchor, [track]); e.Handled = true; return; }
        if (e.GetCurrentPoint(TracksList).Properties.IsLeftButtonPressed && !Ancestors(e.Source).Any(c => c is Button or CheckBox)) { _dragPress = e; _dragTrack = track; _dragStart = e.GetPosition(TracksList); e.Handled = true; }
    }
    private async void TracksMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(TracksList);
        if (_dragPress is null || _dragging || !e.GetCurrentPoint(TracksList).Properties.IsLeftButtonPressed || Math.Abs(point.X - _dragStart.X) + Math.Abs(point.Y - _dragStart.Y) < 8) return;
        var selectedIds = SelectedTracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        if (_dragTrack is not null && (!TracksList.BatchMode || !selectedIds.Contains(_dragTrack.Id))) { selectedIds.Clear(); selectedIds.Add(_dragTrack.Id); }
        var ids = _vm!.VisibleTracks.Where(t => selectedIds.Contains(t.Id)).Select(t => t.Id).ToArray(); if (ids.Length == 0) return;
        _dragging = true; var press = _dragPress; _dragPress = null;
        try { var transfer = new DataTransfer(); transfer.Add(DataTransferItem.CreateText("nonet-tracks:" + string.Join('\n', ids))); await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy | DragDropEffects.Move); }
        finally { _dragging = false; }
    }
    private void DragOver(object? sender, DragEventArgs e) { var text = e.DataTransfer.TryGetText(); e.DragEffects = text?.StartsWith("nonet-playlist:") == true ? DragDropEffects.Move : e.DataTransfer.Contains(DataFormat.File) || text?.StartsWith("nonet-tracks:") == true ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Library_Drop(object? sender, DragEventArgs e) { if (e.Handled) return; e.Handled = true; if (TryReorderTracks(e)) return; await DropAsync(e, _vm?.CurrentPlaylist?.IsSystem == true, _vm?.CurrentPlaylist); }
    private async void Favorite_Drop(object? sender, DragEventArgs e) { e.Handled = true; await DropAsync(e, true, null); }
    private async void Player_Drop(object? sender, DragEventArgs e)
    {
        if (e.Handled || _vm is null) return; e.Handled = true;
        var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToArray() ?? [];
        if (paths.Length > 0) await _vm.PlayTemporaryFilesAsync(paths);
    }
    private async Task DropAsync(DragEventArgs e, bool favorite, Playlist? playlist)
    {
        if (_vm is null) return;
        try
        {
            if (e.DataTransfer.TryGetText() is { } text && text.StartsWith("nonet-tracks:"))
            {
                AddDroppedTracks(text, favorite, playlist); return;
            }
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            if (paths.Length == 1 && Path.GetExtension(paths[0]).ToLowerInvariant() is ".lrc" or ".txt") { _vm.ImportLyrics(paths[0]); return; }
            if (favorite) playlist ??= _vm.LikedPlaylist;
            if (playlist is null)
            {
                _vm.ReportWarning(L10n.T("Playlists.SelectAPlaylistBeforeAddingMusic"));
            }
            else await _vm.ImportAsync(paths, favorite, playlist);
        }
        catch (Exception exception) { _vm.ReportError(L10n.T("Common.DropFailed"), exception); }
    }
    private void AddDroppedTracks(string payload, bool favorite, Playlist? playlist)
    {
        if (_vm is null || !payload.StartsWith("nonet-tracks:", StringComparison.Ordinal)) return;
        var byId = _vm.State.Tracks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var tracks = payload["nonet-tracks:".Length..].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Select(id => byId.GetValueOrDefault(id)).OfType<TrackItem>().ToArray();
        if (tracks.Length == 0) { _vm.ReportWarning(L10n.T("Library.DroppedTracksAreNoLongerInTheLibrarySelect")); return; }
        if (favorite) _vm.FavoriteTracks(tracks);
        else if (playlist is not null) _vm.AddToPlaylist(playlist, tracks);
    }
    private void LibraryMore_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Control control) return; var menu = new ContextMenu();
        if (_vm.CurrentPlaylist is not null && !LibraryAddFiles.IsVisible) {
            menu.Items.Add(Menu(L10n.T("Library.AddMusic"), AddFilesAsync, IconKind.FileAdd));
            menu.Items.Add(Menu(L10n.T("Common.AddFolder"), AddFolderAsync, IconKind.Folder));
            menu.Items.Add(new Separator());
        }
        if (_vm.CurrentPlaylist is not null) menu.Items.Add(Menu(L10n.T("Playlists.ImportM3UPlaylist"), async () => { var paths = await OpenFilesAsync(L10n.T("Playlists.ImportPlaylist"), ["*.m3u", "*.m3u8"], false); if (paths.Length > 0) { try { await _vm.ImportM3uAsync(paths[0]); } catch (Exception exception) { _vm.ReportError(L10n.T("Playlists.PlaylistImportFailed"), exception); } } }));
        menu.Items.Add(Menu(L10n.T("Common.ExportCurrentListAsM3U"), async () => { var name = L10n.T("Playlists.ExportPrefix") + "_" + (_vm.CurrentPlaylist is { } list ? PlaylistName(list) : _vm.PageTitle); foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_'); var path = await SaveFileAsync(L10n.T("Common.ExportLocalTrackList"), name + ".m3u8", "m3u8"); if (path is not null) { try { _vm.ExportM3u(path); _vm.StatusText = L10n.T("Plugins.LocalTracksExportedPluginTracksAreOmittedToKeep"); } catch (Exception exception) { _vm.ReportError(L10n.T("Common.ExportFailed"), exception); } } }));
        menu.Items.Add(Menu(L10n.T("Common.UndoLastAction"), () => { _vm.UndoCommand.Execute(null); return Task.CompletedTask; }));
        if (_vm.CurrentPlaylist is { } playlist) { menu.Items.Add(Menu(L10n.T("Playlists.EditPlaylistDetails"), () => EditPlaylistAsync(playlist), IconKind.Edit)); if (!playlist.IsSystem) menu.Items.Add(Menu(L10n.T("Playlists.DeletePlaylist"), () => DeleteAsync(playlist), IconKind.Trash)); }
        if (_vm.Page == "history") menu.Items.Add(Menu(L10n.T("Playback.ClearHistory"), async () => { if (await PlayerDialog.Confirm(this, L10n.T("Playback.ClearPlaybackHistory"), L10n.T("Playlists.LikedSongsPlaylistsAndMusicFilesAreUnchanged"), L10n.T("Common.Clear"))) _vm.ClearHistory(); }));
        OpenMenu(control, menu);
    }
    private MenuItem Menu(string label, Func<Task> action, IconKind? icon = null, bool rawLabel = false) { var item = new MenuItem { Header = rawLabel ? label : L10n.T(label) }; if (icon is { } kind) item.Icon = new VectorIcon { Kind = kind, Width = 18, Height = 18 }; item.Click += async (_, _) => { try { await action(); } catch (Exception error) { _vm?.ReportError(L10n.T("Common.ActionFailed"), error); } }; return item; }
    private void PlaybackSlider_Released(object? sender, PointerReleasedEventArgs e) => CommitSeek();
    private void Shortcuts(object? sender, KeyEventArgs e)
    {
        if (HasPluginConfigurationOverlay) return;
        if (_vm is null || e.Handled) return;
        if (_handledShortcutKeys.Contains(e.Key)) { e.Handled = true; return; }
        // 标注是当前页面的事务，而非某个子控件的行为；先于播放器的空格快捷键处理。
        if (_activeMenu?.IsOpen != true && ActiveLyricsTimingTool()?.HandleAnnotationKey(e) == true) { _handledShortcutKeys.Add(e.Key); return; }
        var focused = FocusManager?.GetFocusedElement();
        var ancestors = Ancestors(focused).ToArray();
        if (ancestors.Any(c => c is IPluginKeyboardScope || c is ShortcutCaptureButton { IsCapturing: true }) || _activeMenu?.IsOpen == true) return;
        var action = ShortcutService.Match(_vm.Settings.KeyBindings, e.Key, e.KeyModifiers); if (action is null) return;
        var textEditor = ancestors.OfType<TextBox>().FirstOrDefault();
        if (textEditor is not null && action is not ("search" or "layout" or "addMusic" or "help" or "clearSearch")) return;
        if (textEditor is { AcceptsReturn: true } && action == "clearSearch") return;
        if (ancestors.Any(c => c is Slider or ComboBox or CheckBox or ToggleSwitch) && action is not ("search" or "layout" or "addMusic" or "help")) return;
        switch (action)
        {
            case "search": _vm.Navigate("library"); LibrarySearch.Focus(); LibrarySearch.SelectAll(); break;
            case "layout": if (_vm.Page != "settings") _vm.Navigate("settings"); if (AlternatePage.Content is SettingsView settings) settings.FocusLayoutConfiguration(); break;
            case "clearSearch": if (TracksList.BatchMode) SetBatchMode(false); else _vm.SearchText = ""; break;
            case "addMusic": _ = AddFilesAsync(); break;
            case "favorite": _vm.ToggleFavorite(_vm.CurrentTrack); break;
            case "undo": _vm.UndoCommand.Execute(null); break;
            case "batch": if (!LibraryPage.IsVisible) return; SetBatchMode(!TracksList.BatchMode); break;
            case "selectAll": if (!LibraryPage.IsVisible || !TracksList.BatchMode) return; TracksList.SelectAll(); break;
            case "clearSelection": if (!LibraryPage.IsVisible || !TracksList.BatchMode) return; TracksList.SelectedItems?.Clear(); break;
            case "moveUp": case "moveDown": if (!TracksList.BatchMode || SelectedTracks.Length != 1 || _vm.CurrentPlaylist is null) return; _vm.MoveInPlaylist(SelectedTracks[0], action == "moveUp" ? -1 : 1); break;
            case "previous": _vm.PlayPreviousCommand.Execute(null); break;
            case "next": _vm.PlayNextCommand.Execute(null); break;
            case "seekBack": _vm.Seek(_vm.PlaybackPosition - 5); break;
            case "seekForward": _vm.Seek(_vm.PlaybackPosition + 5); break;
            case "playPause": _vm.TogglePlayPauseCommand.Execute(null); break;
            case "help": OpenUserManual(); break;
            default: return;
        }
        _handledShortcutKeys.Add(e.Key); e.Handled = true;
    }
    private LyricsTimingControl? ActiveLyricsTimingTool() => AlternatePage.Content is PluginPageView page
        ? page.GetVisualDescendants().OfType<LyricsTimingControl>().FirstOrDefault(tool => tool.IsActive) : null;
    public async Task<string[]> OpenFilesAsync(string title, string[] patterns, bool multiple = true)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L10n.T(title), AllowMultiple = multiple, FileTypeFilter = [new FilePickerFileType(L10n.T("Common.SupportedFiles")) { Patterns = patterns }] });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }
    public async Task<string?> SaveFileAsync(string title, string name, string extension)
        => (await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = L10n.T(title), SuggestedFileName = name, DefaultExtension = extension, ShowOverwritePrompt = true }))?.TryGetLocalPath();
    public static void OpenPath(string path) { if (!Directory.Exists(path) && !File.Exists(path)) throw new DirectoryNotFoundException(L10n.T("Common.ThisLocationNoLongerExistsChooseAFolderIn")); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
    private void CloseWindow_Click(object? sender, RoutedEventArgs e) => Close();
    private void MinimizeWindow_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private async void ConfirmClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_vm?.IsMigratingData == true) { e.Cancel = true; _vm.ReportWarning(L10n.T("Storage.AppDataIsBeingCopiedWaitForCompletionBefore")); return; }
        if (!_allowClose && _vm?.Settings.CloseToTray == true && TrayAvailable) { e.Cancel = true; FocusManager?.Focus(null); _vm.Save(); Hide(); return; }
        if (_allowClose || _vm?.Settings.ConfirmClose != true) return;
        e.Cancel = true; if (_confirmingClose) return; _confirmingClose = true;
        try { if (await PlayerDialog.Confirm(this, L10n.T("Playback.ClosePlayer"), L10n.T("Playlists.ExitingStopsPlaybackYourLibraryFavoritesAndSettingsHave"), L10n.T("Common.Close"))) { _allowClose = true; Close(); } }
        finally { _confirmingClose = false; }
    }
    private void ResizeBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState != WindowState.Normal || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || sender is not Border border) return;
        var edge = border.Tag switch { "Top" => WindowEdge.North, "Bottom" => WindowEdge.South, "Left" => WindowEdge.West, "Right" => WindowEdge.East, "TopLeft" => WindowEdge.NorthWest, "TopRight" => WindowEdge.NorthEast, "BottomLeft" => WindowEdge.SouthWest, _ => WindowEdge.SouthEast };
        BeginResizeDrag(edge, e); e.Handled = true;
    }
}
