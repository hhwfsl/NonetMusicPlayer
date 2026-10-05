using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>通用歌词搜索部件：网络仅经插件协议，文件选择与危险写入由宿主负责。</summary>
public sealed class LyricsSearchControl : UserControl, IDisposable
{
    private readonly PluginManager _manager;
    private readonly PluginManifest _plugin;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ComboBox _songs, _format;
    private readonly TextBox _query, _preview;
    private readonly TextBlock _status;
    private readonly ListBox _results;
    private readonly Button _search, _load, _associate, _download;
    private LyricsFetchResult? _lyrics;
    private TrackItem? _track;
    private bool _disposed, _busy;
    public bool IsDisposed => _disposed;
    private MainViewModel Vm => TopLevel.GetTopLevel(this)?.DataContext as MainViewModel ?? throw new InvalidOperationException(L10n.T("Playback.ThePlayerIsNotConnected"));
    public LyricsSearchControl(PluginManager manager, PluginManifest plugin)
    {
        _manager = manager; _plugin = plugin; Name = "LyricsSearchTool";
        _songs = new ComboBox { Name = "LyricsSearchSong", PlaceholderText = L10n.T("LyricsSearch.ChooseSong"), MinHeight = 40, MaxDropDownHeight = 300, HorizontalAlignment = HorizontalAlignment.Stretch, IsTextSearchEnabled = true };
        _songs.ItemTemplate = new FuncDataTemplate<TrackItem>((track, _) => Ui.RawText(track is null ? "" : track.Title + " — " + track.Artist, 14));
        _query = new TextBox { Name = "LyricsSearchQuery", PlaceholderText = L10n.T("LyricsSearch.SongName"), MaxLength = 500, MinHeight = 40 };
        _format = new ComboBox { ItemsSource = new[] { L10n.T("LyricsSearch.Line"), L10n.T("LyricsSearch.Word") }, SelectedIndex = manager.ConfigurationValues(plugin)["lyricsFormat"]?.GetValue<string>() == "word" ? 1 : 0, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
        _status = Ui.RawText(L10n.T("LyricsSearch.Instructions"), 13, true); _status.TextWrapping = TextWrapping.Wrap;
        _preview = new TextBox { Name = "LyricsSearchPreview", IsVisible = false, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 240, FontSize = 14, MaxLength = 2_000_000 };
        // 歌词编辑区不复制全文到悬浮提示；局部值覆盖应用的自动 tooltip 样式。
        FullTextToolTips.SetEnabled(_preview, false); ToolTip.SetTip(_preview, null);
        _preview.TextChanged += (_, _) => Refresh();
        _songs.SelectionChanged += (_, _) => { _track = _songs.SelectedItem as TrackItem; if (_track is not null) _query.Text = _track.Title; Refresh(); };
        _format.SelectionChanged += (_, _) => { _lyrics = null; _preview.Text = ""; Refresh(); };
        _results = new ListBox { Name = "LyricsSearchResults", IsVisible = false, Height = 220 };
        _results.ItemTemplate = new FuncDataTemplate<LyricsCandidate>((candidate, _) =>
        {
            var label = candidate?.ToString() ?? ""; var text = Ui.RawText(label, 14); text.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(text, label); return text;
        });
        _results.SelectionChanged += (_, _) => { _lyrics = null; _preview.Text = ""; Refresh(); };
        _search = Action("Common.Search", SearchAsync); _load = Action("LyricsSearch.Preview", LoadAsync);
        _associate = Action(manager.EmbedsLyrics(plugin) ? "LyricsSearch.Embed" : "LyricsSearch.Associate", AssociateAsync);
        _download = Action("LyricsSearch.Download", DownloadAsync);
        var searchRow = new Grid { ColumnDefinitions = new("*,160,Auto"), ColumnSpacing = 8 }; searchRow.Children.Add(_query); Grid.SetColumn(_format, 1); searchRow.Children.Add(_format); Grid.SetColumn(_search, 2); searchRow.Children.Add(_search);
        Content = Ui.Stack(Ui.Text("LyricsSearch.ChooseSong", 13, true), _songs, searchRow, _status, _results, Ui.Actions(_load, _associate, _download), _preview);
        AttachedToVisualTree += (_, _) =>
        {
            var tracks = Vm.State.Tracks.Where(t => t.ProviderId is null).ToList(); if (Vm.CurrentTrack is { ProviderId: null } current && tracks.All(t => t.Id != current.Id)) tracks.Insert(0, current);
            _songs.ItemsSource = tracks; _songs.SelectedItem = Vm.CurrentTrack is { ProviderId: null } selected ? tracks.FirstOrDefault(t => t.Id == selected.Id) : tracks.FirstOrDefault(); Refresh();
        };
    }
    private Button Action(string key, Func<Task> operation)
    {
        var button = Ui.Button(L10n.T(key), () => { }); ToolTip.SetTip(button, L10n.T(key));
        button.Click += async (_, _) =>
        {
            if (_busy || _disposed) return; _busy = true; Refresh();
            try { await operation(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_disposed) Vm.ReportError(L10n.T("LyricsSearch.Failed"), error); }
            finally { _busy = false; if (!_disposed) Refresh(); }
        }; return button;
    }
    private async Task SearchAsync()
    {
        var title = _query.Text?.Trim() ?? ""; if (title.Length == 0) return;
        _lyrics = null; _preview.Text = ""; _status.Text = L10n.T("LyricsSearch.Searching");
        var query = _track is not null && title == _track.Title ? MainViewModel.LyricsQueryFor(_track) : new LyricsQuery(title);
        var result = await _manager.SearchLyricsAsync(_plugin, query, _lifetime.Token); if (_disposed) return;
        _results.ItemsSource = result.Candidates; _results.IsVisible = result.Candidates.Count > 0; _preview.IsVisible = false; _status.Text = result.Candidates.Count == 0 ? L10n.T("LyricsSearch.NoMatch") : L10n.Format("LyricsSearch.Results", result.Candidates.Count);
        if (result.Warnings.Count > 0) _status.Text += " · " + L10n.T("LyricsSearch.PartialSources") + " " + string.Join(", ", result.Warnings.Select(w => w.Split(':')[0]));
    }
    private async Task LoadAsync()
    {
        if (_results.SelectedItem is not LyricsCandidate candidate) return;
        _lyrics = await _manager.FetchLyricsAsync(_plugin, candidate, _format.SelectedIndex == 1 ? "word" : "line", _lifetime.Token);
        if (_disposed) return; _preview.Text = _lyrics.Text; _preview.IsVisible = true; _status.Text = L10n.T(_lyrics.Warning == "line-fallback" ? "LyricsSearch.LineFallback" : "LyricsSearch.PreviewReady");
    }
    private async Task AssociateAsync()
    {
        if (_track is not { } track || EditedLyrics() is not { } lyrics || TopLevel.GetTopLevel(this) is not MainWindow owner) return;
        if (!string.IsNullOrWhiteSpace(Vm.Lyrics.ReadForTrack(track.Id, track.FilePath)) && !await PlayerDialog.Confirm(owner, L10n.T("Lyrics.ReplaceCurrentLyrics"), L10n.T("LyricsSearch.ReplaceWarning"), L10n.T("Common.Replace"))) return;
        await _manager.ApplyLyricsAsync(_plugin, track, lyrics, Vm.Lyrics, _manager.EmbedsLyrics(_plugin), _lifetime.Token);
        if (_disposed) return; if (Vm.CurrentTrack?.Id == track.Id) Vm.ReloadLyrics(); Vm.Save(); Vm.StatusText = _status.Text = L10n.T("LyricsSearch.Associated");
    }
    private async Task DownloadAsync()
    {
        if (EditedLyrics() is not { } lyrics || TopLevel.GetTopLevel(this) is not MainWindow owner) return;
        var name = _results.SelectedItem is LyricsCandidate candidate ? candidate.Artist + " - " + candidate.Title : _query.Text ?? "lyrics";
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        var path = await owner.SaveFileAsync(L10n.T("LyricsSearch.Download"), name + ".lrc", "lrc"); if (path is null || _disposed) return;
        await SavePreviewAsync(path, lyrics); Vm.StatusText = _status.Text = L10n.T("LyricsSearch.Downloaded");
    }
    /// <summary>点击操作时冻结编辑区内容；预览响应只提供来源信息，不能覆盖用户修改。</summary>
    private LyricsFetchResult? EditedLyrics()
    {
        if (_lyrics is null || string.IsNullOrWhiteSpace(_preview.Text)) return null;
        var text = _preview.Text;
        if (Encoding.UTF8.GetByteCount(text) > 2_000_000) throw new InvalidDataException(L10n.T("LyricsSearch.EditTooLarge"));
        return _lyrics with { Text = text };
    }
    private Task SavePreviewAsync(string path, LyricsFetchResult lyrics)
        => File.WriteAllTextAsync(path, lyrics.Text, new UTF8Encoding(false), _lifetime.Token);
    private void Refresh()
    {
        if (_search is null) return;
        _search.IsEnabled = !_busy; _load.IsEnabled = !_busy && _results.SelectedItem is LyricsCandidate;
        var hasText = _lyrics is not null && !string.IsNullOrWhiteSpace(_preview.Text);
        _associate.IsEnabled = !_busy && hasText && _track is not null; _download.IsEnabled = !_busy && hasText;
        _songs.IsEnabled = _format.IsEnabled = _query.IsEnabled = !_busy;
        // 文件选择和确认期间禁止改动文本，保证写入的是点击按钮时的同一份编辑内容。
        _preview.IsReadOnly = _busy;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
}
