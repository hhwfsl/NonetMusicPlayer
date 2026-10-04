using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private Dictionary<string, Control>? _playerControls;
    private bool _safeLayout, _checkingLayout, _layoutCheckScheduled;
    private string _lastUsableLayoutJson = UiLayoutService.DefaultJson;
    private UiLayoutDocument? _renderedDocument;
    private bool _renderedCompact;
    private bool _renderedUltraCompact;
    public UiLayoutService LayoutConfiguration { get; private set; } = null!;

    private void InitializeLayoutConfiguration()
    {
        if (_vm is null) return;
        _playerControls ??= new()
        {
            ["artwork"] = PlayerArtwork, ["title"] = PlayerTitle, ["artist"] = PlayerArtist,
            ["previous"] = PlayerPrevious, ["play"] = PlayerPlay, ["next"] = PlayerNext,
            ["time"] = PlayerTime, ["mode"] = PlayerMode, ["favorite"] = PlayerFavorite,
            ["more"] = PlayerMore, ["volume"] = PlayerVolume,
            ["progress"] = PlaybackSlider
        };
        LayoutConfiguration = new(_vm.Storage);
        RenderLayout(LayoutConfiguration.Current);
        if (LayoutConfiguration.LastError is { } error) _vm.ReportWarning(error);
        SizeChanged += (_, _) => RequestLayoutCheck();
        Opened += (_, _) => RequestLayoutCheck();
    }

    public void ApplyLayoutConfiguration(string json)
    {
        var candidate = LayoutConfiguration.Read(json);
        ValidateProgressContract(candidate.Player);
        var previous = LayoutConfiguration.Current;
        try
        {
            RenderLayout(candidate); UpdateLayout(); ValidateVisibleLayout();
            LayoutConfiguration.Apply(json, _lastUsableLayoutJson); _lastUsableLayoutJson = LayoutConfiguration.AppliedJson; _safeLayout = false;
            _vm!.StatusText = L10n.T("Settings.LayoutValidatedAndAppliedPreviousLayoutBackedUpMusic");
            _vm.Save();
            _vm.CompleteOperation("layout.apply");
        }
        catch
        {
            RenderLayout(previous); UpdateLayout(); CheckResponsiveLayout(); throw;
        }
    }

    public void ReloadLayoutConfiguration()
    {
        if (!File.Exists(LayoutConfiguration.Path)) throw new InvalidDataException(L10n.T("Settings.LayoutFileNotFoundApplyAConfigurationOrRestore"));
        var info = new FileInfo(LayoutConfiguration.Path);
        if (info.Length > UiLayoutService.MaximumBytes) throw new InvalidDataException(L10n.T("Settings.LayoutConfigurationCannotExceedKB"));
        ApplyLayoutConfiguration(File.ReadAllText(info.FullName));
    }

    public void RestoreDefaultLayout()
    {
        if (_vm is null) return;
        LayoutConfiguration.RestoreDefault(_lastUsableLayoutJson); _lastUsableLayoutJson = UiLayoutService.DefaultJson; _safeLayout = false;
        _vm.Settings.Layout.Clear(); _vm.Settings.NavigationRight = false; _vm.Settings.PlayerTop = false;
        _vm.Settings.SidebarWidth = 208; _vm.Settings.PlayerHeight = 96;
        RenderLayout(LayoutConfiguration.Current); _vm.ApplySettings();
        _vm.StatusText = L10n.T("Plugins.DefaultLayoutRestoredMusicLikedSongsPlaylistsAndPlugin");
        _vm.CompleteOperation("layout.reset");
    }

    private static void ValidateProgressContract(UiPlayerLayout layout)
    {
        foreach (var item in layout.Items) {
            if (item.Id == "progress" && item.Height is > 64) throw new InvalidDataException(L10n.T("Playback.PlaybackProgressBarHeightCannotExceedPixels"));
            if (item.Grid is not null) ValidateProgressContract(item.Grid);
        }
    }
    private void RenderLayout(UiLayoutDocument document, bool fallback = false)
    {
        if (_playerControls is null || _vm is null) return;
        var compact = Bounds.Width < 1000 && (fallback || LayoutConfiguration.AppliedJson == UiLayoutService.DefaultJson);
        var ultraCompact = compact && Bounds.Width < 640;
        if (ReferenceEquals(document, _renderedDocument) && compact == _renderedCompact && ultraCompact == _renderedUltraCompact) return;
        _renderedDocument = document; _renderedCompact = compact; _renderedUltraCompact = ultraCompact;
        PlayerColumns.Margin = new(ultraCompact ? 18 : 24, 10, ultraCompact ? 18 : 24, 10);
        foreach (var control in _playerControls.Where(p => p.Key != "progress").Select(p => p.Value))
            if (control.Parent is Panel parent) parent.Children.Remove(control);
        if (PlayerDesktopLyrics.Parent is Panel desktopParent) desktopParent.Children.Remove(PlayerDesktopLyrics);
        PlayerColumns.Children.Clear();
        FillGrid(PlayerColumns, document.Player);
        if (PlayerVolume.Parent is Grid options)
        {
            options.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(40)));
            Grid.SetColumn(PlayerDesktopLyrics, options.ColumnDefinitions.Count - 1); Grid.SetRow(PlayerDesktopLyrics, Grid.GetRow(PlayerVolume));
            PlayerDesktopLyrics.Width = PlayerDesktopLyrics.Height = 40; options.Children.Add(PlayerDesktopLyrics);
        }
        if (compact && PlayerColumns.ColumnDefinitions.Count == 3)
        {
            PlayerColumns.ColumnDefinitions = new("*,132,*");
            foreach (var grid in PlayerColumns.Children.OfType<Grid>())
            {
                if (grid.Children.Contains(PlayerPrevious)) grid.ColumnDefinitions = new("44,44,44");
                if (grid.Children.Contains(PlayerTime)) {
                    grid.ColumnDefinitions = new(ultraCompact ? "44,44,44" : "44,44,44,44,44"); grid.RowDefinitions = new(ultraCompact ? "44,44,20" : "44,20");
                    Grid.SetRow(PlayerTime, ultraCompact ? 2 : 1); Grid.SetColumn(PlayerTime, 0); Grid.SetColumnSpan(PlayerTime, ultraCompact ? 3 : 5);
                    var index = 0;
                    foreach (var control in new[] { PlayerMode, PlayerFavorite, PlayerMore, PlayerVolume, PlayerDesktopLyrics })
                    { Grid.SetRow(control, ultraCompact ? index / 3 : 0); Grid.SetColumn(control, ultraCompact ? index % 3 : index); index++; }
                }
                if (ultraCompact && grid.Children.Contains(PlayerArtwork)) grid.ColumnDefinitions = new("56,*");
            }
            if (ultraCompact) { PlayerArtwork.Width = PlayerArtwork.Height = 48; PlayerTitle.FontSize = 16; PlayerArtist.FontSize = 13; }
            foreach (var id in new[] { "previous", "play", "next" }) { _playerControls[id].Width = 44; _playerControls[id].Height = 44; }
            foreach (var control in new[] { PlayerMode, PlayerFavorite, PlayerMore, PlayerVolume, PlayerDesktopLyrics }) { control.Width = 44; control.Height = 44; }
        }
        if (document.Player.Items.Any(i => i.Id == "progress" && i.Row == 0) && document.Player.Items.Where(i => i.Id != "progress").All(i => i.Row > 0)) PlayerColumns.RowDefinitions[0].Height = new GridLength(0);
        if (fallback || LayoutConfiguration.AppliedJson == UiLayoutService.DefaultJson) PositionPlayerResizeGrip(true);
        SetWorkspaceProfile(document);
    }

    private void SetWorkspaceProfile(UiLayoutDocument document)
    {
        if (_vm is null) return;
        var s = _vm.Settings;
        Workspace.Profile = new AppSettings
        {
            SidebarWidth = s.SidebarWidth, PlayerHeight = s.PlayerHeight, TouchMode = s.TouchMode,
            NavigationRight = s.NavigationRight, PlayerTop = s.PlayerTop,
            Layout = document.Workspace.Count > 0 ? document.Workspace.ToDictionary(p => p.Key, p => p.Value) : new(s.Layout)
        };
        Workspace.InvalidateMeasure();
    }
    private void SaveRegionResize()
    {
        if (_vm is null) return;
        try
        {
            _vm.Settings.SidebarWidth = Workspace.Profile.SidebarWidth;
            _vm.Settings.PlayerHeight = Workspace.Profile.PlayerHeight;
            _vm.Settings.Layout.Clear();
            if (LayoutConfiguration.Current.Workspace.Count > 0)
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(LayoutConfiguration.AppliedJson)!;
                json["workspace"] = new System.Text.Json.Nodes.JsonObject();
                LayoutConfiguration.Apply(json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), _lastUsableLayoutJson);
                _lastUsableLayoutJson = LayoutConfiguration.AppliedJson;
            }
            _vm.ApplySettings();
        }
        catch (Exception error) { _vm.ReportError(L10n.T("Common.CouldNotSavePanelDimensions"), error); }
    }

    private void FillGrid(Grid grid, UiPlayerLayout layout)
    {
        grid.RowDefinitions = new RowDefinitions(string.Join(',', layout.Rows));
        grid.ColumnDefinitions = new ColumnDefinitions(string.Join(',', layout.Columns));
        foreach (var item in layout.Items)
        {
            // 进度条独立叠放于播放栏上层并延展到两端；旧布局仍保留其必需标识。
            if (item.Id == "progress") continue;
            Control control;
            if (item.Grid is not null) { var nested = new Grid(); FillGrid(nested, item.Grid); control = nested; }
            else control = _playerControls![item.Id!];
            Grid.SetRow(control, item.Row); Grid.SetColumn(control, item.Column);
            Grid.SetRowSpan(control, item.RowSpan); Grid.SetColumnSpan(control, item.ColumnSpan);
            control.Margin = new Thickness(item.Margin[0], item.Margin[1], item.Margin[2], item.Margin[3]);
            control.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(item.HorizontalAlignment);
            control.VerticalAlignment = Enum.Parse<VerticalAlignment>(item.VerticalAlignment);
            var defaultWidth = item.Id == "artwork" ? 60d : item.Id is "play" or "previous" or "next" ? 50d : control is Button ? 40d : double.NaN;
            var defaultHeight = item.Id == "progress" ? 24d : defaultWidth;
            control.Width = item.Width ?? defaultWidth; control.Height = item.Height ?? defaultHeight;
            if (control is TextBlock text) text.FontSize = item.FontSize ?? (item.Id == "title" ? 18 : 16);
            else if (control is TemplatedControl templated) templated.FontSize = item.FontSize ?? FontSize;
            if (item.Id == "time") foreach (var textChild in PlayerTime.Children.OfType<TextBlock>()) textChild.FontSize = item.FontSize ?? 13;
            grid.Children.Add(control);
        }
    }

    // 布局不仅要语法合法，还须适应实际视口。临时回退保留用户文件，下次调整尺寸后重试。
    private void RequestLayoutCheck()
    {
        if (_layoutCheckScheduled) return;
        _layoutCheckScheduled = true;
        Dispatcher.UIThread.Post(() => { _layoutCheckScheduled = false; CheckResponsiveLayout(); }, DispatcherPriority.Loaded);
    }

    private void CheckResponsiveLayout()
    {
        if (_checkingLayout || _playerControls is null || !IsVisible) return;
        _checkingLayout = true;
        try
        {
            RenderLayout(LayoutConfiguration.Current);
            UpdateLayout();
            ValidateVisibleLayout(); _safeLayout = false; _lastUsableLayoutJson = LayoutConfiguration.AppliedJson;
        }
        catch (InvalidDataException exception)
        {
            RenderLayout(LayoutConfiguration.Read(UiLayoutService.DefaultJson), true); UpdateLayout();
            if (!_safeLayout) _vm!.ReportWarning(L10n.T("Settings.TheCustomLayoutDoesNotFitThisWindowUsing") + exception.Message);
            _safeLayout = true;
        }
        finally { _checkingLayout = false; }
    }

    private void ValidateVisibleLayout()
    {
        if (_playerControls is null) return;
        foreach (var widget in Workspace.Children.OfType<Controls.LayoutWidget>())
        {
            var min = widget.Key switch { "Navigation" => new Size(150, Bounds.Height < 600 ? 200 : 240), "Content" => new Size(Math.Min(330, Math.Max(200, Bounds.Width - 220)), Bounds.Height < 600 ? 180 : 300), _ => new Size(Math.Min(Bounds.Width - 2, Bounds.Width < 1000 ? 600 : 880), 96) };
            if (widget.Bounds.Width < min.Width || widget.Bounds.Height < min.Height)
                throw new InvalidDataException(L10n.Format("Common.WorkspaceIsTooSmallAtLeastPixelsAreRequired", widget.Key, min.Width, min.Height));
        }
        foreach (var (id, control) in _playerControls)
        {
            if (id == "progress") continue;
            var min = id switch { "artwork" => new Size(48, 48), "title" or "artist" => new Size(Bounds.Width < 640 ? 80 : 90, 14), "time" => new Size(80, 14), "progress" => new Size(180, 12), _ => new Size(32, 32) };
            if (control.Bounds.Width < min.Width || control.Bounds.Height < min.Height)
                throw new InvalidDataException(L10n.Format("Common.PlayerIsTooSmallAtLeastPixelsAreRequired", id, min.Width, min.Height));
            var child = control;
            while (child != PlayerColumns && child.Parent is Control parent)
            {
                var padding = id == "progress" && child == control ? 12 : 0; // 仅扣除 Fluent 滑块透明轨道边距。
                if (child.Bounds.X < -.5 || child.Bounds.Y < -padding - .5 || child.Bounds.Right > parent.Bounds.Width + .5 || child.Bounds.Bottom > parent.Bounds.Height + padding + .5)
                    throw new InvalidDataException(L10n.Format("Common.PlayerExceedsItsCellEnlargeTheGridCellOr", id));
                if (parent is Grid parentGrid)
                {
                    var column = Grid.GetColumn(child); var row = Grid.GetRow(child);
                    var x = parentGrid.ColumnDefinitions.Take(column).Sum(c => c.ActualWidth);
                    var y = parentGrid.RowDefinitions.Take(row).Sum(r => r.ActualHeight);
                    var width = parentGrid.ColumnDefinitions.Skip(column).Take(Grid.GetColumnSpan(child)).Sum(c => c.ActualWidth);
                    var height = parentGrid.RowDefinitions.Skip(row).Take(Grid.GetRowSpan(child)).Sum(r => r.ActualHeight);
                    if (child.Bounds.X < x - .5 || child.Bounds.Right > x + width + .5 || child.Bounds.Height > height + padding + .5 || child.Bounds.Y < y - padding - .5 || child.Bounds.Bottom > y + height + padding + .5)
                        throw new InvalidDataException(L10n.Format("Common.PlayerExceedsItsAllocatedRowsOrColumnsAndMay", id));
                }
                child = parent;
            }
        }
    }
}
