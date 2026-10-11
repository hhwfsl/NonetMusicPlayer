using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<TrackItem> _cloudSearchTracks = [];
    private string _musicSearchSource = "", _musicSearchQuery = "", _musicSearchCursor = "";
    private long _musicSearchRequest;
    public bool HasMoreMusicResults => _musicSearchCursor.Length > 0;
    public bool MusicSearchBusy { get; private set; }
    public event EventHandler? MusicSearchChanged;
    public string LastMusicSearchSource => _musicSearchSource;
    public string LastMusicSearchQuery => _musicSearchQuery;
    /// <summary>本地筛选已有引用；云端结果为临时列表，收藏和加入歌单复用现有数据事务。</summary>
    public async Task SearchMusicAsync(string sourceId, string query, bool nextPage = false)
    {
        if (query.Trim().Length == 0) return;
        if (query.Length > 300) { ReportWarning(L10n.T("MusicSearch.QueryTooLong")); return; }
        var request = ++_musicSearchRequest;
        if (sourceId.Length == 0) { MusicSearchBusy = false; Navigate("songs"); SearchText = query.Trim(); MusicSearchChanged?.Invoke(this, EventArgs.Empty); return; }
        var cursor = nextPage && sourceId == _musicSearchSource && query == _musicSearchQuery ? _musicSearchCursor : "";
        MusicSearchBusy = true; MusicSearchChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await Plugins.SearchMusicAsync(sourceId, query, cursor);
            if (request != _musicSearchRequest || _disposed) return;
            _musicSearchSource = sourceId; _musicSearchQuery = query; _musicSearchCursor = result.Page.NextCursor ?? "";
            _cloudSearchTracks = result.Tracks.Select(t => State.Tracks.FirstOrDefault(s => s.Id == t.Id) ?? t).ToArray();
            Navigate("cloud-search", L10n.Format("MusicSearch.Results", Plugins.MusicSearchSources.First(p => p.Id == sourceId).MusicSearch!.Label));
            if (result.Page.Message.Length > 0) ReportWarning(result.Page.Message);
            CompleteOperation("music.search");
        }
        catch (Exception error) { if (request == _musicSearchRequest) ReportError(L10n.T("MusicSearch.Failed"), error); }
        finally { if (request == _musicSearchRequest) { MusicSearchBusy = false; MusicSearchChanged?.Invoke(this, EventArgs.Empty); } }
    }
}
