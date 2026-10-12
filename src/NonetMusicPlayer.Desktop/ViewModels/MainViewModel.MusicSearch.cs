using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly List<TrackItem> _cloudSearchTracks = [];
    private readonly HashSet<string> _musicSearchIds = new(StringComparer.Ordinal), _musicSearchCursors = new(StringComparer.Ordinal);
    private string _musicSearchSource = "", _musicSearchQuery = "", _musicSearchCursor = "";
    private long _musicSearchRequest;
    private bool _musicArtworkBusy;
    public bool HasMoreMusicResults => _musicSearchCursor.Length > 0;
    public bool MusicSearchBusy { get; private set; }
    public bool ShowMusicSearchSpinner => Page == "cloud-search" && MusicSearchBusy;
    public Avalonia.Layout.VerticalAlignment MusicSearchSpinnerAlignment => VisibleTracks.Count == 0 ? Avalonia.Layout.VerticalAlignment.Center : Avalonia.Layout.VerticalAlignment.Bottom;
    public event EventHandler? MusicSearchChanged;
    public string LastMusicSearchSource => _musicSearchSource;
    public string LastMusicSearchQuery => _musicSearchQuery;
    private void PublishMusicSearchState()
    {
        OnPropertyChanged(nameof(ShowMusicSearchSpinner)); OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(MusicSearchSpinnerAlignment));
        OnPropertyChanged(nameof(EmptyStateTitle)); OnPropertyChanged(nameof(EmptyStateDescription));
        MusicSearchChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>首次搜索先导航再等待，追加分页不重建页面；离页后的迟到响应不得夺回导航。</summary>
    public async Task SearchMusicAsync(string sourceId, string query, bool nextPage = false)
    {
        query = query.Trim(); if (query.Length == 0) return;
        if (query.Length > 300) { ReportWarning(L10n.T("MusicSearch.QueryTooLong")); return; }
        if (nextPage && (MusicSearchBusy || Page != "cloud-search" || !HasMoreMusicResults || sourceId != _musicSearchSource || query != _musicSearchQuery)) return;
        var request = nextPage ? _musicSearchRequest : ++_musicSearchRequest;
        if (sourceId.Length == 0) { MusicSearchBusy = false; Navigate("songs"); SearchText = query; PublishMusicSearchState(); return; }
        var plugin = Plugins.MusicSearchSources.FirstOrDefault(p => p.Id == sourceId); if (plugin is null) return;
        var cursor = nextPage ? _musicSearchCursor : "";
        if (!nextPage)
        {
            _musicSearchSource = sourceId; _musicSearchQuery = query; _musicSearchCursor = "";
            _cloudSearchTracks.Clear(); _musicSearchIds.Clear(); _musicSearchCursors.Clear(); MusicSearchBusy = true;
            var title = L10n.Format("MusicSearch.Results", plugin.MusicSearch!.Label);
            if (Plugins.HasHook("navigation.before")) await NavigateWithHooksAsync("cloud-search", title, ++_extensionNavigationRequest);
            else Navigate("cloud-search", title);
            if (request != _musicSearchRequest || Page != "cloud-search") { if (request == _musicSearchRequest) { MusicSearchBusy = false; PublishMusicSearchState(); } return; }
        }
        _musicSearchCursors.Add(cursor); MusicSearchBusy = true; PublishMusicSearchState();
        try
        {
            var result = await Plugins.SearchMusicAsync(sourceId, query, cursor);
            if (request != _musicSearchRequest || _disposed || Page != "cloud-search") return;
            var stored = State.Tracks.ToDictionary(t => t.Id, StringComparer.Ordinal); var added = new List<TrackItem>();
            foreach (var candidate in result.Tracks)
            {
                if (!_musicSearchIds.Add(candidate.Id)) continue;
                var track = stored.GetValueOrDefault(candidate.Id) ?? candidate;
                if (track.CoverPath is null && candidate.CoverPath is not null) { track.CoverPath = candidate.CoverPath; track.HasSearchCover = true; if (stored.ContainsKey(track.Id)) Plugins.PersistSearchCover(track); }
                track.DisplayIndex = _cloudSearchTracks.Count + added.Count + 1;
                track.IsPlayingHere = PlayingSourcePage == "cloud-search" && CurrentTrack?.Id == track.Id; added.Add(track);
            }
            _cloudSearchTracks.AddRange(added);
            var next = result.Page.NextCursor ?? "";
            _musicSearchCursor = added.Count == 0 || _musicSearchCursors.Contains(next) ? "" : next;
            if (SearchText.Length == 0) VisibleTracks.AddRange(added); else ApplyFilter();
            if (PlayingSourcePage == "cloud-search" && CurrentTrack is not null && _cloudSearchTracks.Any(t => ReferenceEquals(t, CurrentTrack)))
                foreach (var track in added) if (!_playingList.Any(t => t.Id == track.Id)) _playingList.Add(track);
            if (result.Page.Message.Length > 0) ReportWarning(result.Page.Message);
            CompleteOperation("music.search");
        }
        catch (Exception error)
        {
            if (request == _musicSearchRequest) { _musicSearchCursors.Remove(cursor); ReportError(L10n.T("MusicSearch.Failed"), error); }
        }
        finally { if (request == _musicSearchRequest) { MusicSearchBusy = false; PublishMusicSearchState(); } }
    }
    public Task LoadMoreMusicResultsAsync() => SearchMusicAsync(_musicSearchSource, _musicSearchQuery, true);
    /// <summary>只下载当前可见行附近的封面，结果元数据不等待图片下载；旧音源无需实现新 RPC。</summary>
    public async Task LoadMusicSearchArtworkAsync(IEnumerable<TrackItem> visible)
    {
        if (_musicArtworkBusy || _disposed || Page != "cloud-search") return;
        var plugin = Plugins.MusicSearchSources.FirstOrDefault(p => p.Id == _musicSearchSource);
        if (plugin?.MusicSearch?.DeferredArtwork != true) return;
        var tracks = visible.Where(t => t.ProviderId == plugin.Id && (t.CoverPath is null || !File.Exists(t.CoverPath))).Take(30).ToArray();
        if (tracks.Length == 0) return;
        var request = _musicSearchRequest; _musicArtworkBusy = true;
        try
        {
            var result = await Plugins.ReadMusicArtworkAsync(plugin, tracks);
            if (request != _musicSearchRequest || _disposed) return;
            foreach (var image in result)
            {
                var track = tracks.FirstOrDefault(t => t.Id == image.Id); if (track is null || image.CoverPath is null) continue;
                track.CoverPath = image.CoverPath; track.HasSearchCover = true;
                if (State.Tracks.Any(t => t.Id == track.Id)) Plugins.PersistSearchCover(track);
                track.RefreshArtwork();
            }
        }
        catch (Exception error) { if (!_disposed && request == _musicSearchRequest) AppLog.Warning("MusicSource", "Deferred search artwork unavailable", error); }
        finally
        {
            _musicArtworkBusy = false;
            // 旧查询图片完成后唤醒新查询的视口，不让新结果等待下一次鼠标滚动。
            if (!_disposed && request != _musicSearchRequest && Page == "cloud-search") MusicSearchChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void LeaveMusicSearch(string nextPage)
    {
        if (nextPage is "library" or "cloud-search") return;
        ++_musicSearchRequest; MusicSearchBusy = false; PublishMusicSearchState();
    }
}
