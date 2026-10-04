using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<string> _failedPlaybackIds = new(StringComparer.Ordinal);
    private double _recoveryStartPosition;

    /// <summary>单次播放恢复请求对每首不同歌曲最多尝试一次。</summary>
    private async Task StartTrackWithRecoveryAsync(TrackItem? track, IEnumerable<TrackItem>? list, string sourcePage,
        double resumePosition, bool preserveQueueScope, bool preserveFailures)
    {
        if (track is null || _disposed) return;
        if (_lyricsPlaybackLease is { } restriction && (restriction.Track.Id != track.Id || !restriction.Starting && !preserveQueueScope)) { RejectLyricsPlaybackChange(); return; }
        var request = Interlocked.Increment(ref _playRequest);
        await _playGate.WaitAsync();
        try
        {
            if (request != Interlocked.Read(ref _playRequest) || _disposed) return;
            if (_lyricsPlaybackLease is { } active && active.Track.Id != track.Id) { RejectLyricsPlaybackChange(); return; }
            UpdateListeningStatistics();
            if (!preserveFailures) _failedPlaybackIds.Clear();
            var explicitTracks = list?.ToArray();
            if (!preserveQueueScope) _explicitQueueIds = explicitTracks?.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var queue = explicitTracks ?? SourceTracks(sourcePage).ToArray();
            if (_explicitQueueIds is not null) queue = queue.Where(t => _explicitQueueIds.Contains(t.Id)).ToArray();
            _playingList = queue.DistinctBy(t => t.Id).ToList();
            if (_playingList.All(t => t.Id != track.Id)) _playingList.Add(track);
            _explicitQueueIds?.Add(track.Id);
            PlayingSourcePage = NormalizeSourcePage(sourcePage);
            _loadingAudio = true; _audioLoaded = false; IsPlaying = false;
            TrackItem? candidate = track;
            while (candidate is not null)
            {
                if (request != Interlocked.Read(ref _playRequest) || _disposed) return;
                try
                {
                    StatusText = L10n.Format("Playback.Loading", candidate.Title);
                    var source = candidate.ProviderId is null ? candidate.FilePath : await Plugins.ResolveAsync(candidate);
                    if (request != Interlocked.Read(ref _playRequest) || _disposed) return;
                    await _audio.LoadAsync(source);
                    if (request != Interlocked.Read(ref _playRequest) || _disposed) { _audio.Stop(); return; }
                    _audio.Volume = (float)(Volume / 100);
                    CurrentTrack = candidate;
                    _updatingPosition = true;
                    try
                    {
                        PlaybackDuration = Math.Max(1, _audio.Duration.TotalSeconds);
                        PlaybackPosition = resumePosition >= PlaybackDuration - .05 ? 0 : Math.Clamp(resumePosition, 0, PlaybackDuration);
                        _audio.Position = TimeSpan.FromSeconds(PlaybackPosition);
                    }
                    finally { _updatingPosition = false; }
                    _audioLoaded = true; _resumePending = false; _recoveryStartPosition = PlaybackPosition;
                    _audio.Play(); IsPlaying = true; StartListeningSession();
                    State.History.Remove(candidate.Id); State.History.Insert(0, candidate.Id);
                    if (!TrackIndex().ContainsKey(candidate.Id))
                    {
                        State.RecentTemporaryTracks.RemoveAll(t => t.Id == candidate.Id);
                        State.RecentTemporaryTracks.Add(candidate);
                    }
                    TrimHistory();
                    StatusText = L10n.Format("Playback.Playing0CA729", candidate.FormatText, ModeDescription); Save();
                    RefreshPlayingRows(); RequestPlayingTrackLocation();
                    return;
                }
                catch (AudioOutputUnavailableException error)
                {
                    CurrentTrack = candidate; _audioLoaded = false; IsPlaying = false; _resumePending = true;
                    PlaybackPosition = resumePosition; ResetListeningAnchor(); Save();
                    ReportError(L10n.T("Playback.AudioOutputIsUnavailablePlaybackPausedConnectADevice"), error); return;
                }
                catch (Exception error)
                {
                    if (request != Interlocked.Read(ref _playRequest) || _disposed) return;
                    _audioLoaded = false; IsPlaying = false;
                    _failedPlaybackIds.Add(candidate.Id);
                    try { _audio.Stop(); } catch (Exception stopError) { AppLog.Warning("Audio", "跳过故障音频时停止资源失败", stopError); }
                    ReportError(L10n.Format("Playback.UnableToPlaySkipped", candidate.Title), error);
                    candidate = NextRecoveryTrack(candidate);
                    resumePosition = 0;
                }
            }
            StopUnavailableQueue();
        }
        finally { _loadingAudio = false; _playGate.Release(); }
    }

    private TrackItem? NextRecoveryTrack(TrackItem? failed)
    {
        var candidates = _playingList.Where(t => !_failedPlaybackIds.Contains(t.Id)).ToArray();
        if (candidates.Length == 0) return null;
        if (Settings.PlayMode == PlayMode.Shuffle)
        {
            var selected = _shuffle.Choose(candidates.Select(t => t.Id), failed?.Id);
            return candidates.First(t => t.Id == selected);
        }
        var start = _playingList.FindIndex(t => t.Id == failed?.Id);
        for (var offset = 1; offset <= _playingList.Count; offset++)
        {
            var target = _playingList[(Math.Max(-1, start) + offset) % _playingList.Count];
            if (!_failedPlaybackIds.Contains(target.Id)) return target;
        }
        return null;
    }

    private void RecoverRuntimeFailure()
    {
        if (CurrentTrack is { } failed) _failedPlaybackIds.Add(failed.Id);
        var next = NextRecoveryTrack(CurrentTrack);
        if (next is null) { StopUnavailableQueue(); return; }
        _ = StartTrackWithRecoveryAsync(next, _playingList.ToArray(), PlayingSourcePage, 0, true, true);
    }

    private void StopUnavailableQueue()
    {
        try { _audio.Stop(); } catch (Exception error) { AppLog.Warning("Audio", "不可播放列表停止资源失败", error); }
        _audioLoaded = false; IsPlaying = false; _resumePending = false;
        ResetListeningAnchor(); Save();
        ReportWarning(L10n.T("Playback.NoPlayableSongsRemainInThisListPlaybackStopped"));
    }
}
