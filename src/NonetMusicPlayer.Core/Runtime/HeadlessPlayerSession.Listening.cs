using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Persistence;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private HeadlessListeningSession? _listeningSession;
    private void StartListeningRecord()
    {
        EndListeningRecord("track-change"); if (Current is null) return;
        _listeningSession = new() { TrackId = Current.Id, Title = Current.Metadata.Title, Artist = Current.Metadata.Artist, Source = State.LastPlaylistId ?? "temporary" };
        State.ListeningSessions.Add(_listeningSession); RecordPlaybackEvent("player.play");
    }
    private void EndListeningRecord(string reason)
    {
        if (_listeningSession is not { } session) return;
        session.EndedAt = DateTimeOffset.UtcNow; session.EndReason = reason; _listeningSession = null;
    }
    private void RecordListeningInterval(double seconds)
    {
        if (_listeningSession is not { } session || Current is null || seconds <= 0) return;
        var now = DateTimeOffset.UtcNow; var position = _audio.Position.TotalSeconds;
        session.Seconds += seconds; session.LastObservedAt = now;
        var start = now.AddSeconds(-seconds); var from = Math.Max(0, position - seconds);
        // 连续播放合并为区间，避免每个时钟采样都扩张状态文件；暂停和跳转仍分段。
        if (State.ListeningIntervals.LastOrDefault() is { } last && last.SessionId == session.Id && Math.Abs(last.To - from) < .05 && (start - last.EndedAt).TotalSeconds is >= -.1 and < 1)
            State.ListeningIntervals[^1] = last with { EndedAt = now, Seconds = last.Seconds + seconds, To = position };
        else State.ListeningIntervals.Add(new(session.Id, Current.Id, start, now, seconds, from, position));
    }
    private void RecordPlaybackEvent(string operation)
    {
        if (_listeningSession is not { } session || operation is not ("player.play" or "player.pause" or "player.resume" or "player.stop" or "player.next" or "player.previous" or "player.seek" or "player.volume" or "player.mute" or "player.mode" or "player.device" or "queue.select" or "queue.move")) return;
        var now = DateTimeOffset.UtcNow; session.LastObservedAt = now;
        State.ListeningEvents.Add(new(session.Id, now, operation, _loaded ? _audio.Position.TotalSeconds : State.LastPosition, State.Settings.Volume, State.Settings.PlayMode.ToString()));
    }
    private IEnumerable<JsonNode> ListeningRecords(string kind, string? sessionId) => kind switch
    {
        "session" => State.ListeningSessions.Where(s => sessionId is null || s.Id == sessionId).OrderByDescending(s => s.StartedAt).Select(s => JsonSerializer.SerializeToNode(s, CoreJson.Options)!),
        "interval" => State.ListeningIntervals.Where(s => sessionId is null || s.SessionId == sessionId).OrderByDescending(s => s.StartedAt).Select(s => JsonSerializer.SerializeToNode(s, CoreJson.Options)!),
        "event" => State.ListeningEvents.Where(s => sessionId is null || s.SessionId == sessionId).OrderByDescending(s => s.At).Select(s => JsonSerializer.SerializeToNode(s, CoreJson.Options)!),
        _ => throw new FormatException("session | interval | event | hour")
    };
}
