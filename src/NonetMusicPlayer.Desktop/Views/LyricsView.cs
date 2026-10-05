using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Diagnostics;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;
public sealed class LyricsView : UserControl, IDisposable
{
    private readonly MainWindow _owner;
    private readonly MainViewModel _vm;
    private readonly ListBox _list;
    private readonly Grid _layout, _right;
    private readonly Border _art;
    private readonly Image _cover;
    private readonly TextBlock _title, _artist, _empty;
    private readonly Button _more, _seek;
    private readonly NonetMusicPlayer.Desktop.Models.BulkObservableCollection<LyricLine> _displayLines = [];
    private double[] _rowHeights = [], _rowOffsets = [];
    private double _measuredWidth;
    private const double LyricRowHeight = 112;
    private readonly DispatcherTimer _smoothScroll = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _wordFrames = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double _wordSample;
    private long _wordSampleAt;
    private long _animationStarted;
    private double _animationFrom, _animationTo;
    private readonly DispatcherTimer _previewSettled, _resumeFollow;
    private ScrollViewer? _scroll;
    private ContextMenu? _menu;
    private bool _disposed, _previewing, _touchScrolling, _lyricsRefreshPending, _initialCenter = true;
    private int _previewIndex;
    private int _activeIndex = -1;
    private double _spacerHeight = 120;
    private LyricLine? _previewLine;
    private Point? _lastTouch;
    public LyricsView(MainWindow owner, MainViewModel vm)
    {
        _owner = owner; _vm = vm;
        _cover = new Image { Source = vm.CurrentArtwork, Stretch = Stretch.UniformToFill };
        _art = new Border
        {
            Width = 300, Height = 300, CornerRadius = new CornerRadius(22), ClipToBounds = true,
            Background = Ui.Brush("SurfaceRaisedBrush"), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Grid { Children = { new VectorIcon { Kind = IconKind.Music, Width = 76, Height = 76 }, _cover } }
        };
        _title = Ui.Text(vm.CurrentTitle, 26); _title.TextAlignment = TextAlignment.Center;
        _artist = Ui.Text(vm.CurrentArtist, 15, true); _artist.TextAlignment = TextAlignment.Center;
        _more = Ui.Button("", ShowMore); _more.Name = "LyricsMore"; _more.Classes.Add("quiet");
        _more.Content = new VectorIcon { Kind = IconKind.More, Width = 22, Height = 22, Brush = Ui.Brush("TextPrimaryBrush") };
        _more.Width = _more.Height = 44; _more.HorizontalAlignment = HorizontalAlignment.Center;
        var left = Ui.Stack(_art, _title, _artist, _more);
        left.VerticalAlignment = VerticalAlignment.Center; left.HorizontalAlignment = HorizontalAlignment.Right; left.Margin = new Thickness(12, 12, 12, 12); left.Spacing = 18;
        _list = new ListBox
        {
            Name = "LyricLines", ItemsSource = _displayLines, AutoScrollToSelectedItem = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            // 歌词按测量后的行高呈现，换行仍属于同一时间戳；首尾居中通过面板边距实现，不添加伪歌词。
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { Margin = new Thickness(0, _spacerHeight, 0, _spacerHeight) }),
            ItemTemplate = new FuncDataTemplate<LyricLine>((line, _) => LyricContent(line))
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetBringIntoViewOnFocusChange(_list, false);
        _list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch), new Setter(TemplatedControl.ForegroundProperty, Ui.Brush("TextPrimaryBrush")), new Setter(MarginProperty, new Thickness(0)), new Setter(TemplatedControl.PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 0d) }
        });
        foreach (var state in new[] { "", ":pointerover", ":selected", ":selected:pointerover", ":selected:focus" })
            _list.Styles.Add(new Style(selector => {
                var selected = selector.OfType<ListBoxItem>();
                foreach (var part in state.Split(':', StringSplitOptions.RemoveEmptyEntries)) selected = selected.Class(":" + part);
                return selected.Template().OfType<ContentPresenter>();
            }) { Setters = { new Setter(BackgroundProperty, Brushes.Transparent), new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.BorderThicknessProperty, new Thickness(0)) } });
        _list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<TextBlock>().Class("lyric-original"))
        { Setters = { new Setter(TextBlock.ForegroundProperty, Ui.Brush("AccentTextBrush")), new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold) } });
        _smoothScroll.Tick += (_, _) => AnimateScroll();
        _wordFrames.Tick += (_, _) => RefreshWordColors(); _wordFrames.Start();
        _list.AddHandler(PointerWheelChangedEvent, (_, e) => { if (e.Delta.Y != 0) MovePreview(e.Delta.Y > 0 ? -1 : 1); e.Handled = true; }, RoutingStrategies.Tunnel, true);
        _list.AddHandler(PointerPressedEvent, LyricPointerPressed, RoutingStrategies.Tunnel, true);
        _list.AddHandler(PointerMovedEvent, LyricPointerMoved, RoutingStrategies.Tunnel, true);
        _list.AddHandler(PointerReleasedEvent, LyricPointerReleased, RoutingStrategies.Tunnel, true);
        _list.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)) return;
            MovePreview(e.Key switch { Key.Up => -1, Key.Down => 1, Key.PageUp => -5, Key.PageDown => 5, Key.Home => -_vm.LyricLines.Count, _ => _vm.LyricLines.Count }); e.Handled = true;
        }, RoutingStrategies.Tunnel, true);
        _list.TemplateApplied += (_, _) => AttachScroll();
        _empty = Ui.Text("", 18, true); _empty.TextAlignment = TextAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center; _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _seek = Ui.Button("", SeekPreview); _seek.Name = "LyricPreviewSeek"; _seek.Width = _seek.Height = 46;
        _seek.Content = new VectorIcon { Kind = IconKind.Play, Width = 20, Height = 20, Brush = Ui.Brush("AccentForegroundBrush") };
        _seek.Classes.Add("primary"); _seek.IsVisible = false; _seek.HorizontalAlignment = HorizontalAlignment.Right;
        _seek.VerticalAlignment = VerticalAlignment.Center; _seek.Margin = new Thickness(0, 0, 12, 0);
        _right = new Grid { Children = { _list, _empty, _seek }, Margin = new Thickness(0, 10, 12, 10) };
        _layout = new Grid { ColumnDefinitions = new ColumnDefinitions("0.43*,0.57*") };
        _layout.Children.Add(left); Grid.SetColumn(_right, 1); _layout.Children.Add(_right); Content = _layout;
        _previewSettled = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewSettled.Tick += (_, _) => { _previewSettled.Stop(); FindPreviewLine(); };
        _resumeFollow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _resumeFollow.Tick += (_, _) => ResumeFollow();
        SizeChanged += (_, _) => ResizeLayout();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { if (!_disposed) { AttachScroll(); ResizeLayout(); _activeIndex = -1; FollowPosition(); } }, DispatcherPriority.Loaded);
        _list.PointerCaptureLost += (_, _) => { if (_lastTouch is null) return; _lastTouch = null; _touchScrolling = false; if (_previewing) FindPreviewLine(); };
        vm.PropertyChanged += Changed; vm.LyricLines.CollectionChanged += LyricsChanged;
        vm.SettingsChanged += SettingsChanged;
        L10n.LanguageChanged += LanguageChanged;
        UpdateText(); UpdateTrack(); RebuildLyrics();
    }
    public bool IsPreviewing => _previewing;
    private void SettingsChanged(object? sender, EventArgs e)
    {
        foreach (var text in _list.GetVisualDescendants().OfType<KaraokeLine>())
            if (text.Tag is ValueTuple<LyricLine, bool> tag)
            {
                text.TextColor = (Ui.Brush("AccentTextBrush") as ISolidColorBrush)?.Color ?? Colors.White;
                text.PendingColor = (Ui.Brush(tag.Item2 ? "TextMutedBrush" : "TextPrimaryBrush") as ISolidColorBrush)?.Color;
            }
        MeasureLines(true); ResizeLayout(); RefreshWordColors();
    }
    private Control LyricContent(LyricLine? line)
    {
        var original = new KaraokeLine { Tag = (line, false), TextSize = 23, TextColor = (Ui.Brush("AccentTextBrush") as ISolidColorBrush)?.Color ?? Colors.White, PendingColor = (Ui.Brush("TextPrimaryBrush") as ISolidColorBrush)?.Color };
        original.UpdateTimed(line?.Text ?? "", line?.Words ?? [], _vm.PlaybackPosition, false);
        original.Classes.Add("lyric-original");
        var translation = new KaraokeLine { Tag = (line, true), TextSize = 17, TextColor = original.TextColor, PendingColor = (Ui.Brush("TextMutedBrush") as ISolidColorBrush)?.Color };
        translation.UpdateTimed(line?.Translation ?? "", line?.TranslationWords ?? [], _vm.PlaybackPosition, false);
        translation.IsVisible = !string.IsNullOrWhiteSpace(line?.Translation);
        var lines = Ui.Stack(original, translation); lines.Spacing = 7; lines.VerticalAlignment = VerticalAlignment.Center;
        var index = line is null ? -1 : _vm.LyricLines.IndexOf(line);
        return new Border { Name = "LyricRow", Height = index >= 0 && index < _rowHeights.Length ? _rowHeights[index] : LyricRowHeight, Padding = new Thickness(24, 5), Child = lines };
    }
    private void LanguageChanged(object? sender, EventArgs e) => UpdateText();
    private void UpdateText()
    {
        _empty.Text = L10n.T("Lyrics.NoLyricsAvailable0245E1") + "\n" + L10n.T("Lyrics.ImportAnLRCOrTXTFileMatchingThisTrack");
        ToolTip.SetTip(_more, L10n.T("Common.More")); AutomationProperties.SetName(_more, L10n.T("Common.More"));
        ToolTip.SetTip(_seek, L10n.T("Lyrics.PlayFromThisLyric")); AutomationProperties.SetName(_seek, L10n.T("Lyrics.PlayFromThisLyric"));
    }
    private void ShowMore()
    {
        _menu?.Close();
        var import = new MenuItem { Header = L10n.T("Lyrics.ImportLyrics"), Icon = new VectorIcon { Kind = IconKind.FileAdd, Width = 18, Height = 18 } };
        import.Click += async (_, _) =>
        {
            var paths = await _owner.OpenFilesAsync(L10n.T("Lyrics.SelectLyrics"), ["*.lrc", "*.txt"], false);
            if (paths.Length == 0) return;
            try
            {
                if (_vm.CurrentTrack is not null && _vm.Lyrics.Read(_vm.CurrentTrack.Id).Length > 0 && !await PlayerDialog.Confirm(_owner,
                    L10n.T("Lyrics.ReplaceCurrentLyrics"), L10n.T("Lyrics.ReplaceExistingLyricsWithTheSelectedFileTheMusic"), L10n.T("Common.Replace"))) return;
                _vm.ImportLyrics(paths[0]); UpdateTrack();
            }
            catch (Exception error) { _vm.ReportError(L10n.T("Lyrics.LyricsImportFailed"), error); }
        };
        MenuItem Shift(string text, double seconds)
        {
            var item = new MenuItem { Header = L10n.T(text) };
            item.Click += (_, _) => { _vm.Settings.LyricOffset = Math.Clamp(_vm.Settings.LyricOffset + seconds, -30, 30); _vm.ApplySettings(); ResumeFollow(); };
            return item;
        }
        var reset = new MenuItem { Header = L10n.T("Lyrics.ResetLyricsOffset") };
        reset.Click += (_, _) => { _vm.Settings.LyricOffset = 0; _vm.ApplySettings(); ResumeFollow(); };
        var reveal = new MenuItem { Header = L10n.T("Lyrics.ShowLyricFileInFolder"), Icon = new VectorIcon { Kind = IconKind.Folder, Width = 18, Height = 18 }, IsEnabled = _vm.CurrentTrack is { } track && _vm.Lyrics.ExistingPath(track.Id) is not null };
        reveal.Click += (_, _) => { try { if (_vm.CurrentTrack is { } current && _vm.Lyrics.ExistingPath(current.Id) is { } path) SongInfoDialog.RevealFile(path); } catch (Exception error) { _vm.ReportError(L10n.T("Lyrics.UnableToLocateTheLyricFile"), error); } };
        var items = new List<object> { import, reveal, new Separator(), Shift(L10n.T("Lyrics.LyricsSecondsEarlier"), .5), Shift(L10n.T("Lyrics.LyricsSecondsLater"), -.5), reset };
        foreach (var plugin in _vm.Plugins.Installed.Where(p => p.Enabled && p.Type is "ui" or "lyrics"))
            foreach (var contribution in plugin.MenuContributions.Where(c => c.Location == "lyrics.more"))
            {
                var item = new MenuItem { Header = contribution.Label, Icon = new VectorIcon { Kind = IconKind.Edit, Width = 18, Height = 18 } };
                item.Click += async (_, _) => { if (contribution.Action == "match-lyrics") await _vm.MatchCurrentLyricsAsync(plugin); else _vm.Navigate("plugin:" + plugin.Id); }; items.Add(item);
            }
        _menu = new ContextMenu { ItemsSource = items };
        _owner.OpenMenu(_more, _menu);
    }
    private void LyricsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // 合并逐行歌词集合变更，避免大文件反复重建整张虚拟化列表。
        if (_lyricsRefreshPending || _disposed) return;
        _lyricsRefreshPending = true;
        Dispatcher.UIThread.Post(() => { if (!_lyricsRefreshPending) return; _lyricsRefreshPending = false; if (!_disposed) RebuildLyrics(); }, DispatcherPriority.Background);
    }
    private void RebuildLyrics()
    {
        MeasureLines(true); _displayLines.ReplaceAll(_vm.LyricLines);
        _empty.IsVisible = _vm.LyricLines.Count == 0;
        _initialCenter = true; _activeIndex = -1; ResumeFollow();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentTrack) or nameof(MainViewModel.CurrentArtwork)) UpdateTrack();
        if (e.PropertyName == nameof(MainViewModel.PlaybackPosition)) FollowPosition();
    }
    private void UpdateTrack()
    {
        _cover.Source = _vm.CurrentArtwork; _title.Text = _vm.CurrentTitle; _artist.Text = _vm.CurrentArtist;
        // 换封面不改变歌词集合与视口；歌词自身通过集合变更单独刷新。
    }
    private void ResizeLayout()
    {
        var narrow = Bounds.Width < 720;
        var artSize = Math.Clamp(Bounds.Width * (narrow ? .27 : .30), 130, 350);
        artSize = Math.Min(artSize, Math.Max(130, Bounds.Height - 180));
        _art.Width = _art.Height = artSize; _title.FontSize = narrow ? 20 : 26;
        _layout.ColumnDefinitions[0].Width = new GridLength(narrow ? .36 : .43, GridUnitType.Star);
        _layout.ColumnDefinitions[1].Width = new GridLength(narrow ? .64 : .57, GridUnitType.Star);
        MeasureLines(false);
        _spacerHeight = Math.Max(0, (_scroll?.Viewport.Height > 0 ? _scroll.Viewport.Height : _right.Bounds.Height) / 2 - RowHeight(0) / 2);
        UpdateSpacing();
        AttachScroll(); if (!_previewing) CenterActiveLine();
    }
    private void AttachScroll()
    {
        var scroll = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (ReferenceEquals(scroll, _scroll)) return;
        if (_scroll is not null) _scroll.ScrollChanged -= ScrollChanged;
        _scroll = scroll; if (_scroll is not null) { _scroll.ScrollChanged += ScrollChanged; _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; }
    }
    private void ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportDelta.Y != 0 || e.ExtentDelta.Y != 0)
        {
            var next = Math.Max(0, _scroll!.Viewport.Height / 2 - RowHeight(0) / 2);
            if (Math.Abs(next - _spacerHeight) > .5) { _spacerHeight = next; UpdateSpacing(); }
            if (!_previewing) CenterActiveLine();
        }
    }
    private void FollowPosition()
    {
        _wordSample = _vm.PlaybackPosition; _wordSampleAt = Stopwatch.GetTimestamp(); RefreshWordColors();
        var time = _vm.PlaybackPosition + _vm.Settings.LyricOffset;
        var index = -1;
        for (var i = 0; i < _vm.LyricLines.Count; i++) if (_vm.LyricLines[i].Timed && _vm.LyricLines[i].Seconds <= time) index = i;
        index = _vm.LyricLines.Count == 0 ? -1 : Math.Max(0, index);
        if (_activeIndex == index) { if (_initialCenter) CenterActiveLine(); return; }
        _activeIndex = index; _list.SelectedIndex = index; RefreshWordColors(); CenterActiveLine();
    }
    private void CenterActiveLine()
    {
        if (_activeIndex < 0 || _disposed || _previewing) return;
        var index = _activeIndex;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _previewing || _activeIndex != index) return;
            AttachScroll();
            if (_scroll is null) return;
            if (_scroll.Viewport.Height <= 0 || _scroll.Extent.Height <= 0) return;
            var y = CenterOffset(_activeIndex);
            if (_initialCenter) { _smoothScroll.Stop(); _scroll.Offset = new Vector(0, y); _initialCenter = false; }
            else StartScroll(y);
        }, DispatcherPriority.Render);
    }
    /// <summary>恢复焦点立即定位真实当前行，取消失焦前的歌词预览等待。</summary>
    public void RestorePlaybackFocus()
    {
        _previewSettled.Stop(); _resumeFollow.Stop(); _smoothScroll.Stop(); _previewing = false; _seek.IsVisible = false;
        if (_lyricsRefreshPending) { _lyricsRefreshPending = false; RebuildLyrics(); }
        _initialCenter = true; _activeIndex = -1; AttachScroll(); ResizeLayout(); FollowPosition();
    }
    private void RefreshWordColors()
    {
        if (_disposed || !IsVisible) return;
        var time = _wordSample + (_vm.IsPlaying ? Math.Min(.2, Stopwatch.GetElapsedTime(_wordSampleAt).TotalSeconds) : 0) + _vm.Settings.LyricOffset;
        foreach (var text in _list.GetVisualDescendants().OfType<KaraokeLine>())
            if (text.Tag is ValueTuple<LyricLine, bool> tag)
            {
                var active = _activeIndex >= 0 && _activeIndex < _vm.LyricLines.Count && ReferenceEquals(_vm.LyricLines[_activeIndex], tag.Item1);
                text.UpdateTimed(tag.Item2 ? tag.Item1.Translation : tag.Item1.Text, [], time, active, LyricsService.LineCharacterProgress(tag.Item1, time, tag.Item2));
            }
    }
    private void AnimateScroll()
    {
        if (_disposed || _scroll is null || _touchScrolling) { _smoothScroll.Stop(); return; }
        var amount = Math.Clamp(Stopwatch.GetElapsedTime(_animationStarted).TotalMilliseconds / 300, 0, 1);
        var eased = 1 - Math.Pow(1 - amount, 3);
        _scroll.Offset = new Vector(0, _animationFrom + (_animationTo - _animationFrom) * eased);
        if (amount >= 1) { _smoothScroll.Stop(); if (_previewing) ShowPreviewSeek(); }
    }
    private double RowHeight(int index) => index >= 0 && index < _rowHeights.Length ? _rowHeights[index] : LyricRowHeight;
    private double RowOffset(int index) => index >= 0 && index < _rowOffsets.Length ? _rowOffsets[index] : index * LyricRowHeight;
    private double CenterOffset(int index) => Math.Clamp(_spacerHeight + RowOffset(index) + RowHeight(index) / 2 - (_scroll?.Viewport.Height ?? 0) / 2, 0, Math.Max(0, (_scroll?.Extent.Height ?? 0) - (_scroll?.Viewport.Height ?? 0)));
    private void StartScroll(double target)
    {
        if (_scroll is null) return;
        _animationFrom = _scroll.Offset.Y; _animationTo = target; _animationStarted = Stopwatch.GetTimestamp(); _smoothScroll.Start();
    }
    private int NearestIndex()
    {
        var center = (_scroll?.Offset.Y ?? 0) + (_scroll?.Viewport.Height ?? 0) / 2 - _spacerHeight;
        var nearest = 0; var distance = double.MaxValue;
        for (var i = 0; i < _vm.LyricLines.Count; i++) { var delta = Math.Abs(RowOffset(i) + RowHeight(i) / 2 - center); if (delta < distance) { nearest = i; distance = delta; } }
        return nearest;
    }
    private void MeasureLines(bool force)
    {
        var width = Math.Max(80, (_list.Bounds.Width > 0 ? _list.Bounds.Width : _right?.Bounds.Width > 0 ? _right.Bounds.Width : 400) - 48);
        if (!force && Math.Abs(width - _measuredWidth) < 1 && _rowHeights.Length == _vm.LyricLines.Count) return;
        _measuredWidth = width; _rowHeights = new double[_vm.LyricLines.Count]; _rowOffsets = new double[_rowHeights.Length]; var offset = 0d;
        double Measure(string text, double size, double lineHeight)
        {
            var block = new TextBlock { Text = text, FontSize = size, FontFamily = _owner.FontFamily, FontWeight = size == 23 ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = lineHeight };
            block.Measure(new Size(width, double.PositiveInfinity)); return block.DesiredSize.Height;
        }
        for (var i = 0; i < _rowHeights.Length; i++)
        {
            var line = _vm.LyricLines[i]; var height = Measure(line.Text, 23, 31) + (string.IsNullOrWhiteSpace(line.Translation) ? 0 : 7 + Measure(line.Translation, 17, 24)) + 10;
            _rowOffsets[i] = offset; _rowHeights[i] = Math.Max(LyricRowHeight, Math.Ceiling(height)); offset += _rowHeights[i];
        }
        foreach (var item in _list.GetVisualDescendants().OfType<ListBoxItem>())
            if (item.DataContext is LyricLine line && item.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "LyricRow") is { } row) { var index = _vm.LyricLines.IndexOf(line); row.Height = RowHeight(index); }
    }
    private void MovePreview(int step)
    {
        AttachScroll(); BeginPreview();
        _previewIndex = Math.Clamp(_previewIndex + step, 0, Math.Max(0, _vm.LyricLines.Count - 1));
        StartScroll(CenterOffset(_previewIndex));
    }
    private void BeginPreview()
    {
        if (_vm.LyricLines.Count == 0) return;
        AttachScroll(); if (!_previewing) _previewIndex = NearestIndex();
        _previewing = true; _previewLine = null; _seek.IsVisible = false;
        _smoothScroll.Stop();
        _previewSettled.Stop(); _previewSettled.Start(); _resumeFollow.Stop(); _resumeFollow.Start();
    }
    private void FindPreviewLine()
    {
        if (!_previewing || _disposed || _touchScrolling) return;
        AttachScroll(); _previewIndex = NearestIndex(); StartScroll(CenterOffset(_previewIndex));
    }
    private void ShowPreviewSeek()
    {
        if (!_previewing || _vm.LyricLines.Count == 0) return;
        _previewLine = _vm.LyricLines[_previewIndex];
        _seek.RenderTransform = null; _seek.IsVisible = _previewLine.Timed;
    }
    private void SeekPreview()
    {
        if (_previewLine is not { Timed: true } line) return;
        _vm.Seek(line.Seconds - _vm.Settings.LyricOffset);
        if (!_vm.IsPlaying) _vm.TogglePlayPauseCommand.Execute(null);
        ResumeFollow();
    }
    private void UpdateSpacing()
    {
        foreach (var panel in _list.GetVisualDescendants().OfType<VirtualizingStackPanel>()) panel.Margin = new Thickness(0, _spacerHeight, 0, _spacerHeight);
    }
    private void ResumeFollow()
    {
        _previewSettled.Stop(); _resumeFollow.Stop(); _previewing = false; _previewLine = null; _seek.IsVisible = false;
        _activeIndex = -1; FollowPosition();
    }
    private void LyricPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control control && (control is ScrollBar || control.GetVisualAncestors().OfType<ScrollBar>().Any())) { BeginPreview(); return; }
        if (e.Pointer.Type == PointerType.Touch || e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) { _lastTouch = e.GetPosition(_list); _touchScrolling = false; e.Pointer.Capture(_list); e.Handled = true; }
    }
    private void LyricPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_lastTouch is not { } previous) return;
        var point = e.GetPosition(_list); var delta = previous.Y - point.Y;
        if (!_touchScrolling && Math.Abs(delta) < 4) return;
        _lastTouch = point; _touchScrolling = true; BeginPreview(); AttachScroll();
        if (_scroll is not null) _scroll.Offset = new Vector(_scroll.Offset.X, Math.Clamp(_scroll.Offset.Y + delta, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
        e.Handled = true;
    }
    private void LyricPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_lastTouch is null) return;
        _lastTouch = null; _touchScrolling = false; e.Pointer.Capture(null); e.Handled = true;
        if (_previewing) { _previewSettled.Stop(); FindPreviewLine(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _vm.PropertyChanged -= Changed; _vm.LyricLines.CollectionChanged -= LyricsChanged; L10n.LanguageChanged -= LanguageChanged;
        _vm.SettingsChanged -= SettingsChanged;
        _previewSettled.Stop(); _resumeFollow.Stop(); _smoothScroll.Stop(); _wordFrames.Stop(); _menu?.Close(); _more.ContextMenu = null;
        if (_scroll is not null) _scroll.ScrollChanged -= ScrollChanged;
        _cover.Source = null; _list.ItemsSource = null; _displayLines.Clear();
    }
}
