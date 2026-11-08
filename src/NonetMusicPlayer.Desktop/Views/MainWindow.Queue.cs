using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly RetargetableMotion _queueMotion = new();
    private Border? _queueSurface;
    private ListBox? _queueList;
    private TextBlock? _queueHeading, _queueEmpty;
    private bool _queueOpen, _refreshingQueue;
    private int _queueRequest;
    private MainViewModel? _queueOwner;
    public bool IsPlaybackQueueOpen => _queueOpen;

    private void InitializePlaybackQueue()
    {
        _queueMotion.Changed += DrawQueue;
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!_queueOpen || _queueSurface is null) return;
            var p = e.GetPosition(_queueSurface); var button = e.GetPosition(PlayerQueue);
            if (!new Rect(_queueSurface.Bounds.Size).Contains(p) && !new Rect(PlayerQueue.Bounds.Size).Contains(button)) SetPlaybackQueueOpen(false);
        }, RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && (_queueDragPointer is not null || _queuePendingPointer is not null)) { e.Handled = true; CancelQueueDrag(); return; }
            if (_queueOpen && e.Key == Key.Escape) { e.Handled = true; SetPlaybackQueueOpen(false); PlayerQueue.Focus(); }
        }, RoutingStrategies.Tunnel, true);
        SizeChanged += (_, _) => { if (_queueSurface?.IsVisible == true) DrawQueue(_queueMotion.Value); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !IsVisible || e.Property == WindowStateProperty && WindowState == WindowState.Minimized) CloseQueueImmediately();
        };
        Deactivated += (_, _) => CancelQueueDrag();
        Closed += (_, _) => { AttachQueueOwner(null); _queueMotion.Dispose(); };
    }
    private void AttachQueueOwner(MainViewModel? owner)
    {
        if (_queueOwner is not null)
        {
            _queueOwner.PlaybackQueueChanged -= QueueChanged;
            _queueOwner.OperationCompleted -= QueueOperation;
            _queueOwner.PropertyChanged -= QueuePropertyChanged;
        }
        CloseQueueImmediately(); _queueOwner = owner;
        if (owner is null) return;
        owner.PlaybackQueueChanged += QueueChanged;
        owner.OperationCompleted += QueueOperation;
        owner.PropertyChanged += QueuePropertyChanged;
    }
    private void QueueChanged(object? sender, EventArgs e) { if (_queueOpen) RefreshQueue(); }
    private void QueueOperation(object? sender, Core.Commands.CommandResult e) { if (_queueOpen) RefreshQueue(); }
    private void QueuePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (_queueOpen && e.PropertyName == nameof(MainViewModel.CurrentTrack)) RefreshQueue(); }
    private void PlayerQueue_Click(object? sender, RoutedEventArgs e) => SetPlaybackQueueOpen(!_queueOpen);

    /// <summary>浮层位于播放栏上方（顶置时位于下方），不导航、不重挂接页面或阻挡外部操作。</summary>
    public void SetPlaybackQueueOpen(bool open)
    {
        if (_vm is null || open == _queueOpen) return;
        EnsureQueueSurface(); _queueOpen = open;
        if (!open) CancelQueueDrag();
        PlayerQueue.Classes.Set("selected", open); PlayerQueue.Classes.Set("popup-open", open);
        if (open) { RefreshQueue(); _queueSurface!.IsVisible = true; DrawQueue(_queueMotion.Value); PlayerQueue.Focus(); }
        _queueSurface!.IsHitTestVisible = open;
        _ = AnimateQueueAsync(open);
    }
    private async Task AnimateQueueAsync(bool open)
    {
        var request = ++_queueRequest;
        await _queueMotion.MoveAsync(open ? 1 : 0, open ? 340 : 240);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (request == _queueRequest && !_queueOpen && _queueSurface is not null) _queueSurface.IsVisible = false;
        });
    }
    private void CloseQueueImmediately()
    {
        CancelQueueDrag();
        ++_queueRequest; _queueOpen = false; _queueMotion.Cancel();
        _ = _queueMotion.MoveAsync(0, 0);
        PlayerQueue.Classes.Remove("selected"); PlayerQueue.Classes.Remove("popup-open");
        if (_queueSurface is not null) _queueSurface.IsVisible = false;
    }
    private void EnsureQueueSurface()
    {
        // 窗口最外层不参与 ShellLayout 重建；重复打开时仍核对父级，修复旧浮层脱离视觉树。
        var layer = (Grid)Content!;
        if (_queueSurface is not null)
        {
            if (!ReferenceEquals(_queueSurface.Parent, layer))
            { if (_queueSurface.Parent is Panel parent) parent.Children.Remove(_queueSurface); layer.Children.Add(_queueSurface); }
            return;
        }
        _queueHeading = new TextBlock { FontSize = 19, VerticalAlignment = VerticalAlignment.Center };
        var close = Ui.IconButton(Controls.IconKind.Close, "Common.Close", () => SetPlaybackQueueOpen(false));
        Grid.SetColumn(close, 1);
        var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(22, 14, 14, 12), Children = { _queueHeading, close } };
        _queueList = new ListBox { Name = "PlaybackQueueList", ItemsSource = _queueRows, Background = Brushes.Transparent, BorderThickness = new(0), Margin = new(10, 0, 10, 12),
            ItemTemplate = new FuncDataTemplate<TrackItem>((track, _) =>
            {
                // 只实现可见行；绑定保留歌曲元数据补全后的即时显示。
                var title = new TextBlock { FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
                title.Bind(TextBlock.TextProperty, new Binding(nameof(TrackItem.Title)));
                var artist = new TextBlock { FontSize = 12, Opacity = .65, TextTrimming = TextTrimming.CharacterEllipsis };
                artist.Bind(TextBlock.TextProperty, new Binding(nameof(TrackItem.Artist)));
                var cover = new Image { Name = "QueueTrackArtwork", Stretch = Stretch.UniformToFill };
                cover.Bind(Image.SourceProperty, new Binding(nameof(TrackItem.Artwork)));
                var artwork = new Border { Width = 44, Height = 44, CornerRadius = new(8), ClipToBounds = true,
                    Child = new Grid { Children = { new VectorIcon { Kind = IconKind.Music, Width = 24, Height = 24, Brush = Ui.Brush("TextSecondaryBrush") }, cover } } };
                artwork.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceRaisedBrush"));
                var text = new StackPanel { Spacing = 4, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { title, artist } }; Grid.SetColumn(text, 1);
                var duration = new TextBlock { FontSize = 12, Opacity = .6, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                duration.Bind(TextBlock.TextProperty, new Binding(nameof(TrackItem.DurationText))); Grid.SetColumn(duration, 2);
                return new Grid { ColumnDefinitions = new("44,*,Auto"), MinHeight = 58, Margin = new(6, 3, 10, 3), Children = { artwork, text, duration } };
            }) };
        InitializeQueueDrag();
        // 点击当前行也表示重新选歌，不能仅依赖 SelectionChanged（选中项可能没有变化）。
        _queueList.Tapped += (_, e) => { if (!_suppressQueueTap) PlayQueueSelection(Ancestors(e.Source).OfType<ListBoxItem>().FirstOrDefault()?.DataContext as TrackItem); };
        _queueList.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; PlayQueueSelection(_queueList.SelectedItem as TrackItem); } };
        _queueEmpty = new TextBlock { Text = L10n.T("Playback.QueueEmpty"), Opacity = .65, TextWrapping = TextWrapping.Wrap, Margin = new(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var body = new Grid { Children = { _queueList, _queueEmpty } }; Grid.SetRow(body, 1);
        _queueSurface = new Border { Name = "PlaybackQueuePopover", BorderThickness = new(1), CornerRadius = new(24), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ZIndex = 800,
            BoxShadow = BoxShadows.Parse("0 12 36 0 #48000000"), Child = new Grid { RowDefinitions = new("Auto,*"), Children = { header, body } } };
        _queueSurface.Classes.Add("nonet-floating-surface");
        _queueSurface.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceBrush"));
        _queueSurface.Bind(Border.BorderBrushProperty, this.GetResourceObservable("DividerBrush"));
        Grid.SetRowSpan(_queueSurface, 100); Grid.SetColumnSpan(_queueSurface, 100);
        layer.Children.Add(_queueSurface);
    }
    private void RefreshQueue()
    {
        if (_vm is null || _queueList is null) return;
        CancelQueueDrag();
        var tracks = _vm.PlaybackQueue;
        _refreshingQueue = true;
        try
        {
            if (!_queueRows.SequenceEqual(tracks)) { _queueRows.Clear(); foreach (var track in tracks) _queueRows.Add(track); }
            _queueList.SelectedItem = tracks.FirstOrDefault(t => t.Id == _vm.CurrentTrack?.Id);
            _queueHeading!.Text = L10n.Format("Playback.QueueCount", tracks.Count);
            _queueEmpty!.IsVisible = tracks.Count == 0;
        }
        finally { _refreshingQueue = false; }
    }
    private async void PlayQueueSelection(TrackItem? track)
    {
        if (_refreshingQueue || track is null || _vm is null) return;
        try { await _vm.PlayQueueTrackAsync(track); }
        catch (Exception error) { _vm.ReportError(L10n.T("Playback.UnableToChangePlaybackState"), error); }
    }
    private void DrawQueue(double value)
    {
        if (_queueSurface is null) return;
        var layer = (Grid)Content!;
        var width = Math.Max(1, Math.Min(420, layer.Bounds.Width - 32));
        var player = PlayerSurface.TranslatePoint(default, layer) ?? default;
        var top = InterfaceSettings.PlayerTop;
        var available = top ? layer.Bounds.Height - player.Y - PlayerSurface.Bounds.Height - 24 : player.Y - 64;
        var height = Math.Max(1, Math.Min(560, available));
        var x = Math.Max(16, layer.Bounds.Width - width - 20);
        var y = top ? player.Y + PlayerSurface.Bounds.Height + 12 : Math.Max(52, player.Y - height - 12);
        _queueSurface.Width = width; _queueSurface.Height = height;
        _queueSurface.Margin = new(x, y, 0, 0);
        // 缩放、弧线位移和透明度共享当前值，反复点击从当前帧反向，不闪回起点。
        var scale = .9 + .1 * value;
        _queueSurface.RenderTransformOrigin = top ? new RelativePoint(1, 0, RelativeUnit.Relative) : new RelativePoint(1, 1, RelativeUnit.Relative);
        _queueSurface.RenderTransform = new MatrixTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(-12 * (1 - value), (top ? -1 : 1) * (24 * (1 - value) - 12 * 4 * value * (1 - value))));
        _queueSurface.CornerRadius = new(24 / scale); _queueSurface.Opacity = value;
    }
}
