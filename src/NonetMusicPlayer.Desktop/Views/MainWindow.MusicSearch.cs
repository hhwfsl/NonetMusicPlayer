using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private string _selectedMusicSource = "", _homeMusicQuery = "";
    private Grid? _homeMusicSearchField;
    private ScrollViewer? _musicSearchScroll;
    private bool _musicSearchScrollRequested;
    private readonly DispatcherTimer _musicSearchViewportTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private void MusicSearchStateChanged(object? sender, EventArgs e) { BuildMusicSearchHeader(); ScheduleMusicSearchViewport(); }
    private void InitializeMusicSearchScroll()
    {
        TracksList.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(AttachMusicSearchScroll, DispatcherPriority.Loaded);
        // 隧道路由同时覆盖已到底、没有 Offset 变化及子控件处理过滚轮的情况。
        TracksList.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => { if (e.Delta.Y < 0) { _musicSearchScrollRequested = true; ScheduleMusicSearchViewport(); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        _musicSearchViewportTimer.Tick += async (_, _) =>
        {
            _musicSearchViewportTimer.Stop(); AttachMusicSearchScroll();
            if (_vm is null) return;
            if (_vm.Page != "cloud-search")
            {
                _musicSearchScrollRequested = false;
                await _vm.LoadLanArtworkAsync(TracksList.GetVisualDescendants().OfType<ListBoxItem>().Select(i => i.DataContext).OfType<TrackItem>().DistinctBy(t => t.Id).ToArray());
                return;
            }
            var scroll = _musicSearchScroll;
            if (_musicSearchScrollRequested && scroll is not null && !_vm.MusicSearchBusy && _vm.HasMoreMusicResults
                && scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - Math.Max(.5, scroll.Viewport.Height * .15))
            { _musicSearchScrollRequested = false; await _vm.LoadMoreMusicResultsAsync(); }
            else _musicSearchScrollRequested = false;
            if (_vm.Page != "cloud-search" || _vm.MusicSearchBusy) return;
            var visible = TracksList.GetVisualDescendants().OfType<ListBoxItem>().Select(i => i.DataContext).OfType<TrackItem>().DistinctBy(t => t.Id).ToArray();
            await _vm.LoadMusicSearchArtworkAsync(visible);
        };
        Closed += (_, _) => { _musicSearchViewportTimer.Stop(); if (_musicSearchScroll is not null) _musicSearchScroll.ScrollChanged -= MusicSearchScrollChanged; };
    }
    private void AttachMusicSearchScroll()
    {
        var scroll = TracksList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (ReferenceEquals(scroll, _musicSearchScroll)) return;
        if (_musicSearchScroll is not null) _musicSearchScroll.ScrollChanged -= MusicSearchScrollChanged;
        _musicSearchScroll = scroll; if (scroll is not null) scroll.ScrollChanged += MusicSearchScrollChanged;
    }
    private void MusicSearchScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y > 0) _musicSearchScrollRequested = true;
        ScheduleMusicSearchViewport();
    }
    private void ScheduleMusicSearchViewport()
    {
        if (_vm is null || (_vm.Page != "cloud-search" && !_vm.VisibleTracks.Any(t => t.ProviderId?.StartsWith("lan:", StringComparison.Ordinal) == true))) { _musicSearchViewportTimer.Stop(); return; }
        _musicSearchViewportTimer.Stop(); _musicSearchViewportTimer.Start();
    }
    /// <summary>来源来自音源声明，不包含供应商专用分支；未启用或卸载的音源立即退出列表。</summary>
    private void BuildMusicSearchHeader()
    {
        if (_vm is null) return;
        MusicSearchHeader.IsVisible = _vm.Page is "library" or "cloud-search";
        if (!MusicSearchHeader.IsVisible) { _homeMusicQuery = ""; _homeMusicSearchField = null; MusicSearchHeader.Content = null; return; }
        var sources = new[] { (Id: "", Label: L10n.T("Common.Local")) }.Concat(_vm.Plugins.MusicSearchSources.Select(p => (p.Id, p.MusicSearch!.Label))).ToArray();
        if (sources.All(s => s.Id != _selectedMusicSource)) _selectedMusicSource = "";
        var select = new ComboBox { Name = "MusicSearchSource", ItemsSource = sources.Select(s => s.Label).ToArray(), SelectedIndex = Array.FindIndex(sources, s => s.Id == _selectedMusicSource), Width = 112, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(select, L10n.T("MusicSearch.Source"));
        var editor = new TextBox { Name = "HomeMusicSearch", Text = _homeMusicQuery, PlaceholderText = L10n.T("MusicSearch.Placeholder"), MaxLength = 300, MinHeight = 40, Padding = new(34, 0, 32, 0) };
        editor.Classes.Add("web-search");
        var field = new Grid { Name = "HomeMusicSearchField", Width = Math.Clamp(ViewportWidth * .22, 140, 280), Height = 40, ClipToBounds = true }; field.Children.Add(editor);
        _homeMusicSearchField = field;
        async Task Search()
        {
            _homeMusicQuery = editor.Text ?? ""; FocusManager?.Focus(null);
            await _vm.SearchMusicAsync(_selectedMusicSource, _homeMusicQuery);
        }
        var search = Ui.AsyncButton("MusicSearch.Search", Search); search.Name = "HomeMusicSearchButton";
        search.Classes.Add("search-icon"); search.Content = new VectorIcon { Kind = IconKind.Search, Width = 18, Height = 18 };
        search.Width = 32; search.Height = 36; search.HorizontalAlignment = HorizontalAlignment.Left; search.Cursor = new(StandardCursorType.Hand);
        var clear = Ui.Button("Common.Clear", () => { editor.Text = ""; _homeMusicQuery = ""; FocusManager?.Focus(null); }); clear.Classes.Add("search-icon");
        clear.Content = new VectorIcon { Kind = IconKind.Close, Width = 16, Height = 16 }; clear.Width = 30; clear.Height = 36; clear.HorizontalAlignment = HorizontalAlignment.Right; clear.Cursor = new(StandardCursorType.Hand);
        field.Children.Add(search); field.Children.Add(clear);
        editor.TextChanged += (_, _) => _homeMusicQuery = editor.Text ?? "";
        editor.KeyDown += async (_, e) => { if (e.Key != Key.Enter) return; e.Handled = true; await Search(); };
        select.SelectionChanged += (_, _) => { if (select.SelectedIndex >= 0) _selectedMusicSource = sources[select.SelectedIndex].Id; };
        var row = Ui.Actions(field, select); row.Spacing = 8;
        MusicSearchHeader.Content = row;
    }
    /// <summary>隐藏首帧使用预设窗口宽度，显示及缩放后更新现有容器，不重建输入框或改变焦点。</summary>
    private void UpdateMusicSearchHeaderWidth()
    {
        if (_homeMusicSearchField is { } field) field.SetCurrentValue(WidthProperty, Math.Clamp(ViewportWidth * .22, 140, 280));
    }
}
