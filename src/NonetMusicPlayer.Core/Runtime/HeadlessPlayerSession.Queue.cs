using NonetMusicPlayer.Core.Playback;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private void RestoreQueueSnapshot()
    {
        var known = State.Tracks.Concat(State.RecentTemporaryTracks).Concat(State.QueueTracks).DistinctBy(t => t.Id).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var restored = State.LastQueueIds.Distinct(StringComparer.Ordinal).Where(known.ContainsKey).Select(id => known[id]).ToList();
        if (restored.Count > 0) _queue = restored;
        if (State.SourceQueueIds.Count == 0) State.SourceQueueIds = _queue.Select(t => t.Id).ToList();
    }
    /// <summary>只在手动选歌时洗牌；自动前进、后退、错误恢复始终沿可见队列走。</summary>
    private void SelectQueue(IEnumerable<StoredMusicTrack> tracks, StoredMusicTrack selected, bool preserveSource = false)
    {
        _queue = tracks.DistinctBy(t => t.Id).ToList();
        if (!preserveSource) { State.SourceQueueIds = _queue.Select(t => t.Id).ToList(); State.ManualQueueOrder = false; }
        if (State.Settings.PlayMode != PlaybackMode.Shuffle) return;
        _queue.RemoveAll(t => t.Id == selected.Id);
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_queue)); _queue.Insert(0, selected);
    }
    private void ChangeMode(PlaybackMode mode)
    {
        if (mode == State.Settings.PlayMode) return;
        State.Settings.PlayMode = mode; State.ManualQueueOrder = false;
        if (mode == PlaybackMode.Shuffle && Current is { } current) SelectQueue(_queue, current, true);
        else
        {
            var positions = State.SourceQueueIds.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i, StringComparer.Ordinal);
            _queue = _queue.OrderBy(t => positions.GetValueOrDefault(t.Id, int.MaxValue)).ToList();
        }
    }
}
