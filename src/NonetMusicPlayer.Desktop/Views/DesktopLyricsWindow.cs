using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>独立置顶歌词窗口，主窗口仅管理生命周期，不拥有其原生窗口。</summary>
public sealed class DesktopLyricsWindow : Window, IDisposable
{
    private readonly MainWindow _owner;
    private readonly MainViewModel _vm;
    private readonly TextBlock _song;
    private readonly KaraokeLine _line, _next;
    private readonly DispatcherTimer _frame = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private long _positionSample;
    private double _samplePosition;
    private bool _positioned;
    private readonly VectorIcon _playIcon;
    private readonly Button _previous, _play, _forward, _close, _lock;
    private bool _disposing, _disposed, _handledSpace, _lyricsRefreshPending;
    private bool _restoring, _restorePending;
    private long _lastPresentationCheck;
    private readonly TouchWindowMover _touchMover;
    private readonly Border _surface;
    private readonly List<Border> _resizeHandles = [];
    private readonly Grid _header;
    private readonly DesktopLyricsInputService _nativeInput;
    private bool _resizing, _sizingLyrics;
    private PixelPoint _resizeStart;
    private Size _resizeSize;
    private PixelPoint _resizeOrigin;
    private enum ResizeEdge { Left, Right, Top, Bottom }
    public bool IsLocked => _vm.Settings.DesktopLyricsLocked;
    public event EventHandler? StateChanged;
    public bool FrameVisible { get; private set; }

    public DesktopLyricsWindow(MainWindow owner, MainViewModel vm)
    {
        _owner = owner; _vm = vm; DataContext = vm;
        _touchMover = new TouchWindowMover(this, allowMouse: true, constrain: ConstrainDrag, completed: () => { SaveBounds(); vm.Save(); });
        WindowDecorations = WindowDecorations.None; CanResize = true;
        Width = Math.Clamp(vm.Settings.DesktopLyricsWidth, 360, 2000); Height = Math.Clamp(vm.Settings.DesktopLyricsHeight, 120, 2000); MinWidth = 360; MinHeight = 120; MaxWidth = 2000; MaxHeight = 2000;
        ShowInTaskbar = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; Background = Brushes.Transparent;
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            var style = new Style(s => s.OfType<Button>().Class(state).Template().OfType<ContentPresenter>());
            style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromArgb(state == ":pressed" ? (byte)70 : (byte)40, 255, 255, 255))));
            Styles.Add(style);
        }
        _song = new TextBlock { FontSize = 12, Foreground = Brushes.White, Opacity = .65, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _line = new KaraokeLine { Name = "DesktopCurrentLyric", Wrap = false };
        _next = new KaraokeLine { Name = "DesktopNextLyric", Wrap = false };
        Button Icon(IconKind kind, Action action)
        {
            var button = Ui.Button("", action); button.Width = 44; button.Height = 44; button.Padding = new Thickness(10);
            button.Background = Brushes.Transparent; button.Foreground = Brushes.White;
            button.Content = new VectorIcon { Kind = kind, Brush = Brushes.White, Width = 18, Height = 18 }; return button;
        }
        _previous = Icon(IconKind.Previous, () => vm.PlayPreviousCommand.Execute(null));
        _play = Icon(IconKind.Play, () => vm.TogglePlayPauseCommand.Execute(null)); _playIcon = (VectorIcon)_play.Content!;
        _forward = Icon(IconKind.Next, () => vm.PlayNextCommand.Execute(null)); _close = Icon(IconKind.Close, HideLyrics);
        _lock = Icon(IconKind.Lock, LockLyrics); _lock.Name = "LockDesktopLyrics";
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _previous, _play, _forward, _lock, _close } };
        var header = _header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(_song); Grid.SetColumn(controls, 1); header.Children.Add(controls);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4, Children = { _line, _next } };
        var body = new ScrollViewer { Content = lines, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new(0, 4) };
        var content = new Grid { RowDefinitions = new RowDefinitions("44,*") };
        content.Children.Add(header); Grid.SetRow(body, 1); content.Children.Add(body);
        var surface = _surface = new Border { Name = "DesktopLyricsSurface", Background = Brushes.Transparent, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 0), Child = content };
        var root = new Grid { Children = { surface } }; Content = root;
        AddResizeHandles(root);
        _nativeInput = new DesktopLyricsInputService(this, TextRegions, point =>
        {
            if (IsLocked) return false;
            if (FrameVisible && (new Rect(Bounds.Size).Contains(point) || _touchMover.IsDragging || _resizing)) return true;
            return TextRegions().Any(rect => rect.Contains(point));
        }, visible => SetFrameVisible(visible));
        SetFrameVisible(false);
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (IsLocked) return;
            if (FrameVisible && new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
            if (_line.TextBounds.Contains(e.GetPosition(_line)) || _next.TextBounds.Contains(e.GetPosition(_next))) SetFrameVisible(true);
        }, RoutingStrategies.Tunnel, true);
        PointerExited += (_, _) => { if (!_touchMover.IsDragging && !_resizing) SetFrameVisible(false); };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (IsLocked) { e.Handled = true; return; }
            if (!FrameVisible && (_line.TextBounds.Contains(e.GetPosition(_line)) || _next.TextBounds.Contains(e.GetPosition(_next)))) SetFrameVisible(true);
            if (!FrameVisible || e.Source is Visual v && v.GetVisualAncestors().Prepend(v).Any(a => a is Button || a is Border border && _resizeHandles.Contains(border))) return;
            _touchMover.TryBegin(e);
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => { PlaceOnDesktop(); UpdateLyrics(); _nativeInput.Refresh(IsLocked, FrameVisible); _nativeInput.RestorePresentation(raise: true); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty) { if (IsVisible) { SamplePosition(); _frame.Start(); } else _frame.Stop(); StateChanged?.Invoke(this, EventArgs.Empty); }
            if (e.Property == IsVisibleProperty && !IsVisible || e.Property == WindowStateProperty && WindowState == WindowState.Minimized || e.Property == TopmostProperty && !Topmost)
                ScheduleRestore();
        };
        _frame.Tick += (_, _) =>
        {
            UpdateLyrics(); _nativeInput.Refresh(IsLocked, FrameVisible);
            if (Stopwatch.GetElapsedTime(_lastPresentationCheck).TotalSeconds >= 1)
            {
                _lastPresentationCheck = Stopwatch.GetTimestamp(); _nativeInput.RestorePresentation();
            }
        };
        SizeChanged += (_, _) => { UpdateLyrics(); _nativeInput.Refresh(IsLocked, FrameVisible); };
        Closing += (_, e) => { if (!_disposing) { e.Cancel = true; HideLyrics(); } };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (IsLocked) { e.Handled = true; return; }
            if (e.Key == Key.Escape) { HideLyrics(); e.Handled = true; }
            if (e.Key == Key.Space) { if (!_handledSpace) vm.TogglePlayPauseCommand.Execute(null); _handledSpace = true; e.Handled = true; }
        }, RoutingStrategies.Tunnel, true);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space && _handledSpace) { _handledSpace = false; e.Handled = true; } }, RoutingStrategies.Tunnel, true);
        Deactivated += (_, _) => _handledSpace = false;
        owner.Opened += OwnerOpened; owner.Closed += OwnerClosed;
        owner.Activated += OwnerActivated; owner.PropertyChanged += OwnerPropertyChanged;
        vm.PropertyChanged += Changed; vm.SettingsChanged += SettingsChanged; vm.LyricLines.CollectionChanged += LyricsChanged; L10n.LanguageChanged += LanguageChanged;
        UpdateText(); UpdateLyrics();
        if (owner.IsVisible) ScheduleRestore();
    }

    private void SetFrameVisible(bool visible)
    {
        visible &= !IsLocked;
        var changed = FrameVisible != visible;
        FrameVisible = visible; _header.Opacity = visible ? 1 : 0; _header.IsHitTestVisible = visible;
        foreach (var handle in _resizeHandles) handle.IsVisible = visible;
        _surface.Background = visible ? new SolidColorBrush(Color.FromArgb(160, 18, 21, 29)) : Brushes.Transparent;
        if (changed) _nativeInput.Refresh(IsLocked, visible);
    }
    private void AddResizeHandles(Grid root)
    {
        foreach (var edge in Enum.GetValues<ResizeEdge>())
        {
            var left = edge == ResizeEdge.Left; var right = edge == ResizeEdge.Right;
            var top = edge == ResizeEdge.Top; var bottom = edge == ResizeEdge.Bottom;
            var handle = new Border
            {
                Name = "DesktopLyricsResize" + edge,
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                Width = left || right ? 8 : double.NaN, Height = top || bottom ? 8 : double.NaN,
                HorizontalAlignment = left ? HorizontalAlignment.Left : right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = top ? VerticalAlignment.Top : bottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Cursor = new Cursor(left || right ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth)
            };
            ToolTip.SetTip(handle, L10n.T("Lyrics.DragToResizeTheLyricsWindow"));
            handle.PointerPressed += (_, e) =>
            {
                if (IsLocked || !FrameVisible || e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                _resizing = true; _resizeStart = this.PointToScreen(e.GetPosition(this)); _resizeOrigin = Position; _resizeSize = Bounds.Size; e.Pointer.Capture(handle); e.Handled = true;
            };
            handle.PointerMoved += (_, e) =>
            {
                if (!_resizing || e.Pointer.Captured != handle) return;
                var point = this.PointToScreen(e.GetPosition(this)); var dx = (point.X - _resizeStart.X) / RenderScaling; var dy = (point.Y - _resizeStart.Y) / RenderScaling;
                var width = left || right ? Math.Clamp(_resizeSize.Width + (left ? -dx : dx), MinWidth, MaxWidth) : _resizeSize.Width;
                var height = top || bottom ? Math.Clamp(_resizeSize.Height + (top ? -dy : dy), MinHeight, MaxHeight) : _resizeSize.Height;
                Width = width; Height = height;
                Position = new(_resizeOrigin.X + (left ? (int)Math.Round((_resizeSize.Width - width) * RenderScaling) : 0), _resizeOrigin.Y + (top ? (int)Math.Round((_resizeSize.Height - height) * RenderScaling) : 0)); e.Handled = true;
            };
            void Finish() { if (!_resizing) return; _resizing = false; PlaceOnDesktop(); SaveBounds(); _vm.Save(); }
            handle.PointerReleased += (_, e) => { if (!_resizing || e.Pointer.Captured != handle) return; Finish(); e.Pointer.Capture(null); e.Handled = true; };
            handle.PointerCaptureLost += (_, _) => Finish();
            root.Children.Add(handle); _resizeHandles.Add(handle);
        }
    }
    private void SettingsChanged(object? sender, EventArgs e) { UpdateLyrics(); UpdateLayout(); _nativeInput.Refresh(IsLocked, FrameVisible); }
    private void ApplyLyricColor()
    {
        var color = (Ui.Brush("AccentBrush") as ISolidColorBrush)?.Color ?? Color.Parse(_vm.Settings.Accent);
        _line.TextColor = _next.TextColor = color;
    }
    public IReadOnlyList<Rect> TextRegions() => new[] { _line, _next }.Where(line => line.TextBounds.Width > 0).Select(line => new Rect(line.TranslatePoint(line.TextBounds.Position, this) ?? default, line.TextBounds.Size)).ToArray();
    public void LockLyrics()
    {
        if (_disposed || !IsVisible) return;
        _touchMover.CancelDrag(); _resizing = false; PlaceOnDesktop(); SaveBounds(); _vm.Settings.DesktopLyricsLocked = true; SetFrameVisible(false); _nativeInput.Refresh(true, false); _vm.Save(); StateChanged?.Invoke(this, EventArgs.Empty);
        FocusManager?.Focus(null);
    }
    public void UnlockLyrics()
    {
        if (_disposed) return;
        _vm.Settings.DesktopLyricsLocked = false; SetFrameVisible(false); _nativeInput.Refresh(false, false);
        if (_vm.Settings.DesktopLyricsVisible) RestoreVisibility();
        _vm.Save(); StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OwnerOpened(object? sender, EventArgs e) => RestoreVisibility();
    private void OwnerActivated(object? sender, EventArgs e) => RestoreVisibility();
    private void OwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) ScheduleRestore();
    }
    private void OwnerClosed(object? sender, EventArgs e) => Dispose();
    private void ScheduleRestore()
    {
        if (_disposed || _disposing || _restoring || _restorePending || !_vm.Settings.DesktopLyricsVisible) return;
        _restorePending = true;
        Dispatcher.UIThread.Post(() => { _restorePending = false; RestoreVisibility(); }, DispatcherPriority.Loaded);
    }
    public void RestoreVisibility()
    {
        if (_disposed || _disposing || _restoring || !_vm.Settings.DesktopLyricsVisible) return;
        _restoring = true;
        try
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            if (!IsVisible) Show();
            PlaceOnDesktop(); SamplePosition(); UpdateLyrics(); UpdateLayout();
            _nativeInput.Refresh(IsLocked, FrameVisible); _nativeInput.RestorePresentation(raise: true); _frame.Start();
        }
        finally { _restoring = false; }
    }
    public void Toggle()
    {
        if (_disposed) return;
        if (IsLocked) { UnlockLyrics(); return; }
        if (_vm.Settings.DesktopLyricsVisible && _nativeInput.IsPresented) HideLyrics();
        else { _vm.Settings.DesktopLyricsVisible = true; RestoreVisibility(); _vm.Save(); StateChanged?.Invoke(this, EventArgs.Empty); }
    }
    public void HideLyrics()
    {
        if (_disposed || IsLocked) return; SaveBounds(); _vm.Settings.DesktopLyricsVisible = false; SetFrameVisible(false); Hide(); _vm.Save(); StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void PlaceOnDesktop()
    {
        if (_touchMover.IsDragging || _resizing) return;
        var screen = _positioned ? Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(Position)) : null;
        screen ??= Screens.ScreenFromWindow(_owner) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea; var scale = screen.Scaling;
        Width = Math.Clamp(Width, MinWidth, Math.Max(MinWidth, Math.Min(MaxWidth, area.Width / scale)));
        Height = Math.Clamp(Height, MinHeight, Math.Max(MinHeight, Math.Min(MaxHeight, area.Height / scale)));
        var x = _positioned ? Position.X : _vm.Settings.DesktopLyricsX ?? area.X + (area.Width - (int)(Width * scale)) / 2;
        var y = _positioned ? Position.Y : _vm.Settings.DesktopLyricsY ?? area.Bottom - (int)(Height * scale) - 24;
        Position = new PixelPoint(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - (int)(Width * scale))), Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - (int)(Height * scale))));
        _positioned = true;
    }
    private void SaveBounds()
    {
        if (!_positioned) return;
        _vm.Settings.DesktopLyricsX = Position.X; _vm.Settings.DesktopLyricsY = Position.Y; _vm.Settings.DesktopLyricsWidth = Width;
        _vm.Settings.DesktopLyricsHeight = Height;
    }
    private PixelPoint ConstrainDrag(PixelPoint candidate, PixelPoint pointer)
    {
        var screen = Screens.ScreenFromPoint(pointer) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return candidate;
        var area = screen.WorkingArea;
        return new PixelPoint(Math.Clamp(candidate.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(Bounds.Width * RenderScaling))),
            Math.Clamp(candidate.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Bounds.Height * RenderScaling))));
    }
    private void SamplePosition() { _samplePosition = _vm.PlaybackPosition; _positionSample = Stopwatch.GetTimestamp(); }
    private void LanguageChanged(object? sender, EventArgs e) { UpdateText(); UpdateLyrics(); }
    private void UpdateText()
    {
        Title = L10n.T("Lyrics.DesktopLyrics");
        void Label(Button button, string text) { ToolTip.SetTip(button, L10n.T(text)); AutomationProperties.SetName(button, L10n.T(text)); }
        Label(_previous, L10n.T("Common.Previous")); Label(_play, _vm.IsPlaying ? L10n.T("Common.Pause") : L10n.T("Playback.Play")); Label(_forward, L10n.T("Common.Next")); Label(_close, L10n.T("Lyrics.CloseDesktopLyrics"));
        Label(_lock, L10n.T("Lyrics.LockDesktopLyrics"));
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PlaybackPosition) or nameof(MainViewModel.IsPlaying)) SamplePosition();
        if (e.PropertyName is nameof(MainViewModel.PlaybackPosition) or nameof(MainViewModel.CurrentTrack) or nameof(MainViewModel.CurrentTitle) or nameof(MainViewModel.IsPlaying)) UpdateLyrics();
    }
    private void LyricsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_disposed || _lyricsRefreshPending) return; _lyricsRefreshPending = true;
        Dispatcher.UIThread.Post(() => { _lyricsRefreshPending = false; if (!_disposed) UpdateLyrics(); }, DispatcherPriority.Background);
    }
    private void UpdateLyrics()
    {
        ApplyLyricColor();
        _song.Text = _vm.HasCurrentTrack ? _vm.CurrentTitle + " — " + _vm.CurrentArtist : L10n.T("Lyrics.DesktopLyrics");
        _playIcon.Kind = _vm.IsPlaying ? IconKind.Pause : IconKind.Play; UpdateText();
        var index = -1;
        var time = _samplePosition + (_vm.IsPlaying ? Math.Min(.5, Stopwatch.GetElapsedTime(_positionSample).TotalSeconds) : 0) + _vm.Settings.LyricOffset;
        for (var i = 0; i < _vm.LyricLines.Count; i++) if (_vm.LyricLines[i].Timed && _vm.LyricLines[i].Seconds <= time) index = i;
        if (index < 0) index = 0;
        if (_vm.LyricLines.Count == 0) { _line.Update(L10n.T("Lyrics.NoLyricsAvailable0245E1"), 0); _next.Update("", 0); _next.IsVisible = false; ResizeLyricText(); return; }
        var line = _vm.LyricLines[index];
        _line.UpdateTimed(line.Text, line.Words, time);
        // 第二行仅呈现当前句的翻译；无翻译时折叠，不以后一条原文填充。
        _next.IsVisible = !string.IsNullOrWhiteSpace(line.Translation);
        _next.UpdateTimed(line.Translation.Replace('\n', ' '), [], time,
            characterProgress: _next.IsVisible ? LyricsService.LineCharacterProgress(line, time, true) : null);
        ResizeLyricText();
    }
    private void ResizeLyricText()
    {
        if (_sizingLyrics) return;
        _sizingLyrics = true;
        try { FitLyricContent(); }
        finally { _sizingLyrics = false; }
    }
    private void FitLyricContent()
    {
        _line.TextSize = _vm.Settings.DesktopLyricsFontSize;
        _next.TextSize = _line.TextSize * 22 / 32;
        // 内容永不改变窗口尺寸，长句由呈现器在用户确定的宽度内横向滚动。
    }
    public void Dispose()
    {
        if (_disposed) return; _disposing = true;
        _owner.Opened -= OwnerOpened; _owner.Closed -= OwnerClosed;
        _owner.Activated -= OwnerActivated; _owner.PropertyChanged -= OwnerPropertyChanged;
        _vm.PropertyChanged -= Changed; _vm.SettingsChanged -= SettingsChanged; _vm.LyricLines.CollectionChanged -= LyricsChanged; L10n.LanguageChanged -= LanguageChanged;
        SaveBounds(); _frame.Stop(); _nativeInput.Dispose(); _touchMover.Dispose(); Close(); _disposed = true;
    }
}
