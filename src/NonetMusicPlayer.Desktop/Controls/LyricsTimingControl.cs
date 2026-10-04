using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Lyrics;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>契约提供的原生歌词标注部件。插件无需接触音频对象、键盘钩子或任意文件写入接口。</summary>
public sealed class LyricsTimingControl : UserControl, IPluginKeyboardScope, IDisposable
{
    private readonly string _pluginId;
    private readonly TextBlock _file, _state;
    private readonly ComboBox _songSelector;
    private readonly ListBox _lines;
    private readonly System.Collections.ObjectModel.ObservableCollection<TimingRow> _rows = [];
    private LyricsTimingSession? _renderedSession;
    private readonly Button _choose, _start, _mark, _undo, _save, _cancel;
    private LyricsTimingSession? _session;
    private MainViewModel.LyricsPlaybackLease? _lease;
    private TrackItem? _track;
    private bool _spaceHeld, _busy, _disposed, _updatingSongs;
    public bool IsActive => _lease is not null;
    public LyricsTimingSession? Session => _session;
    public TrackItem? SelectedTrack => _track;
    private MainViewModel Vm => TopLevel.GetTopLevel(this)?.DataContext as MainViewModel ?? throw new InvalidOperationException(L10n.T("Playback.ThePlayerIsNotConnected"));

    public LyricsTimingControl(string pluginId)
    {
        _pluginId = pluginId; Name = "LyricsTimingTool"; Focusable = true;
        _file = Ui.Text("LyricsTiming.NoFile", 12, true); _state = Ui.Text("LyricsTiming.Instructions", 13, true);
        _songSelector = new ComboBox { Name = "TimingSong", MinHeight = 42, MaxDropDownHeight = 360, HorizontalAlignment = HorizontalAlignment.Stretch, IsTextSearchEnabled = true, PlaceholderText = L10n.T("LyricsTiming.ChooseSong") };
        _songSelector.ItemTemplate = new FuncDataTemplate<TrackItem>((track, _) =>
        {
            var label = track is null ? "" : track.Title + " — " + track.Artist;
            var text = Ui.RawText(label, 14); text.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(text, label); return text;
        });
        TextSearch.SetTextBinding(_songSelector, new Avalonia.Data.Binding(nameof(TrackItem.Title)));
        ToolTip.SetTip(_songSelector, L10n.T("LyricsTiming.ChooseSong"));
        _songSelector.SelectionChanged += (_, _) => { if (!_updatingSongs && !IsActive && _songSelector.SelectedItem is TrackItem track) { _track = track; Refresh(); } };
        _songSelector.DropDownOpened += (_, _) => UpdateSongs();
        _choose = Button("LyricsTiming.ChooseFile", ChooseAsync, "TimingChoose");
        _start = Button("LyricsTiming.Start", StartAsync, "TimingStart");
        _mark = Button("LyricsTiming.Mark", () => { Mark(); return Task.CompletedTask; }, "TimingMark");
        _undo = Button("LyricsTiming.Undo", () => { _session?.Undo(); Refresh(); return Task.CompletedTask; }, "TimingUndo");
        _save = Button("LyricsTiming.Save", SaveAsync, "TimingSave");
        _cancel = Button("Common.Cancel", () => { Cancel(); return Task.CompletedTask; }, "TimingCancel");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _choose, _start, _mark, _undo, _save, _cancel }) { button.Margin = new(0, 0, 8, 8); actions.Children.Add(button); }
        _lines = new ListBox { Name = "TimingLines", Height = 340, SelectionMode = SelectionMode.Single, Background = Brushes.Transparent, ItemsSource = _rows };
        _lines.ItemTemplate = new FuncDataTemplate<TimingRow>((row, _) =>
        {
            if (row is null) return new Border();
            var grid = new Grid { ColumnDefinitions = new("35,120,*"), Margin = new(4, 6) };
            grid.Children.Add(Ui.RawText((row.Index + 1).ToString(), 12, true));
            var timestamp = Ui.RawText(row.Timestamp, 13, true); timestamp.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(TimingRow.Timestamp))); Grid.SetColumn(timestamp, 1); grid.Children.Add(timestamp);
            var text = Ui.RawText(row.Text, 16); text.TextWrapping = TextWrapping.Wrap; Grid.SetColumn(text, 2); grid.Children.Add(text);
            return grid;
        });
        Content = Ui.Stack(Ui.Text("LyricsTiming.ChooseSong", 13, true), _songSelector, _file, _state, actions, _lines);
        AddHandler(KeyDownEvent, Pressed, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space) _spaceHeld = false; }, RoutingStrategies.Tunnel);
        LostFocus += (_, _) => _spaceHeld = false;
        AttachedToVisualTree += (_, _) => { UpdateSongs(); Refresh(); }; Refresh();
    }

    private Button Button(string label, Func<Task> action, string name)
    {
        var button = new Button { Content = L10n.T(label), Name = name };
        button.Click += async (_, _) =>
        {
            if (_busy || _disposed) return; _busy = true; Refresh();
            try { await action(); }
            catch (Exception error) { if (!_disposed) Vm.ReportError(L10n.T("LyricsTiming.Failed"), error); }
            finally { _busy = false; if (!_disposed) Refresh(); }
        };
        return button;
    }
    private async Task ChooseAsync()
    {
        if (IsActive) return;
        UpdateSongs();
        if (TopLevel.GetTopLevel(this) is not MainWindow owner || _track is not { } track)
            throw new InvalidOperationException(L10n.T("Commands.SelectTrack"));
        var paths = await owner.OpenFilesAsync(L10n.T("Lyrics.SelectLyrics"), ["*.txt", "*.lrc"], false);
        if (_disposed || paths.Length == 0) return;
        LoadFile(paths[0], track);
    }
    /// <summary>统一的载入入口供界面和自动化验证使用；只读取选定文件，不覆盖它。</summary>
    public void LoadFile(string path, TrackItem track)
    {
        if (_disposed || IsActive) throw new InvalidOperationException(L10n.T("LyricsTiming.Busy"));
        if (Path.GetExtension(path).ToLowerInvariant() is not (".lrc" or ".txt") || new FileInfo(path).Length > 2_000_000)
            throw new InvalidDataException(L10n.T("LyricsTiming.TooLarge"));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 2_000_000) throw new InvalidDataException(L10n.T("LyricsTiming.TooLarge"));
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = Encoding.GetEncoding("GB18030").GetString(bytes); }
        _session = new(text); _track = track; _file.Text = Path.GetFileName(path); Refresh();
    }
    /// <summary>默认当前曲目，用户可在标注开始前改选；改变选择不立即切歌，也不清除已载入的文本。</summary>
    public void SelectTrack(TrackItem track)
    {
        if (_disposed || IsActive) throw new InvalidOperationException(L10n.T("LyricsTiming.Busy"));
        _track = track; UpdateSongs(); Refresh();
    }
    private void UpdateSongs()
    {
        if (_disposed || IsActive || TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
        _track ??= vm.CurrentTrack;
        var songs = vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).Concat(vm.CurrentTrack is { } current ? [current] : Array.Empty<TrackItem>()).DistinctBy(t => t.Id).ToArray();
        _updatingSongs = true;
        try { _songSelector.ItemsSource = songs; _songSelector.SelectedItem = songs.FirstOrDefault(t => t.Id == _track?.Id); }
        finally { _updatingSongs = false; }
    }
    public async Task StartAsync()
    {
        if (_disposed || _session is null || _track is null || IsActive) return;
        _lease = Vm.ReserveLyricsPlayback(_pluginId, _track);
        _session = new(string.Join('\n', _session.Lines));
        try { await _lease.StartAsync(); if (!_disposed) { Refresh(); Focus(); } }
        catch { _lease?.Dispose(); _lease = null; throw; }
    }
    private void Pressed(object? sender, KeyEventArgs e) => HandleAnnotationKey(e);
    /// <summary>由主窗口优先路由标注键，使播放栏或歌词列表持有焦点时也不会误触播放暂停。</summary>
    public bool HandleAnnotationKey(KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || !IsActive || _disposed) return false;
        e.Handled = true;
        if (_spaceHeld) return true; _spaceHeld = true;
        try { Mark(); } catch (Exception error) { Vm.ReportError(L10n.T("LyricsTiming.Failed"), error); }
        return true;
    }
    public void ReleaseAnnotationKey() => _spaceHeld = false;
    public void Mark()
    {
        if (_disposed || _lease is null || _session is null || !Vm.IsPlaying) return;
        _session.Mark(Math.Min(Vm.AudioClockSeconds, Vm.PlaybackDuration)); Refresh();
    }
    private async Task SaveAsync()
    {
        if (_lease is null || _session?.Complete != true) return;
        if (TopLevel.GetTopLevel(this) is not MainWindow owner) return;
        if (Vm.Lyrics.Read(_lease.Track.Id).Length > 0 && !await PlayerDialog.Confirm(owner, L10n.T("Lyrics.ReplaceCurrentLyrics"), L10n.T("LyricsTiming.Replace"), L10n.T("Common.Replace"))) return;
        if (_disposed || _lease is null) return;
        SaveCompleted();
    }
    public void SaveCompleted()
    {
        if (_disposed || _lease is null || _session is null) throw new InvalidOperationException(L10n.T("LyricsTiming.SessionEnded"));
        _lease.Save(_session.ToLrc()); _lease.Dispose(); _lease = null;
        _state.Text = L10n.T("LyricsTiming.Saved"); Refresh(false);
    }
    public void Cancel()
    {
        _lease?.Dispose(); _lease = null; _session = null; _track = null; _spaceHeld = false;
        if (!_disposed) { _state.Text = L10n.T("LyricsTiming.Cancelled"); Refresh(false); }
    }
    private void Refresh(bool status = true)
    {
        if (_disposed) return;
        _songSelector.IsEnabled = !_busy && !IsActive;
        if (_songSelector.SelectedItem is not TrackItem selectedSong || selectedSong.Id != _track?.Id)
        { _updatingSongs = true; try { _songSelector.SelectedItem = (_songSelector.ItemsSource as IEnumerable<TrackItem>)?.FirstOrDefault(t => t.Id == _track?.Id); } finally { _updatingSongs = false; } }
        _choose.IsEnabled = !_busy && !IsActive; _start.IsEnabled = !_busy && !IsActive && _session is not null;
        _mark.IsEnabled = !_busy && IsActive && _session?.Complete == false; _undo.IsEnabled = !_busy && IsActive && _session?.NextLine > 1;
        _save.IsEnabled = !_busy && IsActive && _session?.Complete == true; _cancel.IsEnabled = !_busy && _session is not null;
        if (status) _state.Text = _session is null ? L10n.T("LyricsTiming.Instructions") : IsActive
            ? L10n.Format("LyricsTiming.Progress", _session.NextLine, _session.Lines.Count) : L10n.T("LyricsTiming.Ready");
        var times = _session?.Times;
        if (!ReferenceEquals(_renderedSession, _session))
        {
            _renderedSession = _session; _rows.Clear();
            if (_session is not null) for (var i = 0; i < _session.Lines.Count; i++) _rows.Add(new(i, _session.Lines[i]));
        }
        // 仅改变实际更新的时间文本，保留虚拟化容器和用户的列表位置。
        if (times is not null) for (var i = 0; i < _rows.Count; i++) _rows[i].Update(times[i]);
        if (_session is { } session) { _lines.SelectedIndex = Math.Min(session.NextLine, session.Lines.Count - 1); if (_lines.SelectedItem is { } selected) _lines.ScrollIntoView(selected); }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose() { if (_disposed) return; Cancel(); _disposed = true; }
    private sealed class TimingRow(int index, string text) : ViewModelBase
    {
        private string _timestamp = "—"; private double? _time;
        public int Index { get; } = index;
        public string Text { get; } = text;
        public string Timestamp { get => _timestamp; private set => SetProperty(ref _timestamp, value); }
        public void Update(double? time) { if (time == _time) return; _time = time; Timestamp = time is { } value ? LyricsTimingSession.FormatTimestamp(value) : "—"; }
    }
}
