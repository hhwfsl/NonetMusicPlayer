using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public event EventHandler? PlaybackQueueChanged;
    private void NotifyPlaybackQueueChanged()
    {
        OnPropertyChanged(nameof(PlaybackQueue));
        PlaybackQueueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>手动选歌以该歌曲开头，对剩余歌曲做 Fisher–Yates 等概率洗牌；自动切歌保留顺序。</summary>
    private List<TrackItem> OrderPlaybackQueue(IEnumerable<TrackItem> source, TrackItem? selected)
    {
        var tracks = source.DistinctBy(t => t.Id).ToList();
        if (Settings.PlayMode != PlayMode.Shuffle) return tracks;
        var hasSelected = selected is not null && tracks.Any(t => t.Id == selected.Id);
        if (selected is not null) tracks.RemoveAll(t => t.Id == selected.Id);
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tracks));
        if (hasSelected) tracks.Insert(0, selected!);
        return tracks;
    }

    /// <summary>浮层选歌沿用实际播放来源；随机模式下重新排队，不将浏览页误当成来源。</summary>
    public async Task PlayQueueTrackAsync(TrackItem track)
    {
        if (RejectLyricsPlaybackChange() || !_playingList.Any(t => t.Id == track.Id)) return;
        await StartTrackAsync(track, _playingList.ToArray(), PlayingSourcePage, 0);
        if (IsPlaying) CompleteOperation("queue.play");
    }
}
