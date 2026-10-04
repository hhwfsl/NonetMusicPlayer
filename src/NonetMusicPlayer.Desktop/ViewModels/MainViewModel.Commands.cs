using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public event EventHandler<CommandResult>? OperationCompleted;
    public IReadOnlyList<TrackItem> PlaybackQueue => _playingList.ToArray();
    internal long ErrorRevision { get; private set; }

    /// <summary>发布已经完成的业务动作，不把定时器的进度刷新误记为用户命令。</summary>
    internal void CompleteOperation(string operation, System.Text.Json.Nodes.JsonNode? data = null) => OperationCompleted?.Invoke(this, CommandResults.Completed(operation, data));

    /// <summary>界面和命令共用历史清理，临时播放条目也必须同步清除，但不删除音频文件。</summary>
    public void ClearHistory()
    {
        State.History.Clear(); State.RecentTemporaryTracks.Clear(); Save(); ApplyFilter(); CompleteOperation("history.clear");
    }

    /// <summary>命令播放歌单时保留当前页面，同时将播放来源明确绑定到该歌单。</summary>
    public async Task PlayPlaylistAsync(Playlist playlist, string? startTrackId = null)
    {
        if (RejectLyricsPlaybackChange()) return;
        var source = "playlist:" + playlist.Id;
        var tracks = SourceTracks(source).ToArray();
        if (tracks.Length == 0) throw new InvalidOperationException(L10n.T("Playlists.SelectAPlaylistBeforeAddingMusic"));
        var selected = startTrackId is null ? tracks[0] : tracks.FirstOrDefault(t => t.Id == startTrackId)
            ?? throw new InvalidDataException(L10n.T("Commands.NotInPlaylist"));
        await StartTrackAsync(selected, tracks, source, 0);
        if (IsPlaying) CompleteOperation("queue.play");
    }

    public async Task SetPlayingAsync(bool playing)
    {
        if (_loadingAudio || _switchingOutput) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PreparingAudio"));
        if (playing && !_audioLoaded)
        {
            var track = CurrentTrack ?? VisibleTracks.FirstOrDefault() ?? State.Tracks.FirstOrDefault();
            if (track is null) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.NoTrack"));
            await StartTrackAsync(track, null, CurrentTrack is null ? NormalizeSourcePage(Page) : PlayingSourcePage, _resumePending ? PlaybackPosition : 0, CurrentTrack is not null);
            if (!IsPlaying) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaybackStartFailed"));
        }
        else if (IsPlaying != playing)
        {
            UpdateListeningStatistics();
            if (playing) _audio.Play(); else _audio.Pause();
            IsPlaying = playing; ResetListeningAnchor(); Save(); RequestPlayingTrackLocation();
        }
        CompleteOperation(playing ? "player.resume" : "player.pause");
    }

    public void RemovePlaylistTracks(Playlist playlist, IEnumerable<TrackItem> tracks)
    {
        var ids = tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var prior = playlist.TrackIds.ToList();
        playlist.TrackIds.RemoveAll(ids.Contains); if (playlist.IsSystem) SyncFavoriteFlags();
        _undo.Push(() => { playlist.TrackIds = prior; if (playlist.IsSystem) SyncFavoriteFlags(); });
        RefreshSourceQueue(); Save(); ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("playlist.remove");
    }
}
