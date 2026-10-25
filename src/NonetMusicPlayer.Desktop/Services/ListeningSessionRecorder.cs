using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>独立于界面和音频后端的会话状态机；连续片段原位更新，不为每次采样创建数据库行。</summary>
public sealed class ListeningSessionRecorder(ListeningJournalStore store)
{
    private ListeningSessionRecord? _session;
    private ListeningIntervalRecord? _interval;
    private bool _playing;
    private int _offset;
    public void Start(TrackItem track, string page, Playlist? playlist, DateTimeOffset now, int offset, double position, double volume, PlayMode mode, string device)
    {
        End("track-changed", now);
        _session = new() { TrackId = track.Id, Title = track.Title, Artist = track.Artist, Album = track.Album, Format = track.Extension, FileSize = track.FileSize,
            LocalFilePath = track.ProviderId is null && Path.IsPathFullyQualified(track.FilePath) ? track.FilePath : null, DurationSeconds = track.DurationSeconds,
            SourceType = track.ProviderId is null ? "local" : track.ProviderId.StartsWith("lan:", StringComparison.Ordinal) ? "device" : "cloud", ProviderId = track.ProviderId, ProviderTrackId = track.ProviderTrackId,
            SourceDeviceKind = track.SourceDeviceKind, SourcePage = page, PlaylistId = playlist?.Id, PlaylistName = playlist?.Name,
            StartedUtc = now, UpdatedUtc = now, LocalOffsetMinutes = offset, StartPosition = position, LastPosition = position, InitialVolume = volume, InitialPlayMode = mode, InitialOutputDevice = device, Volume = volume, PlayMode = mode, OutputDevice = device };
        _playing = true; _interval = null; _offset = offset; Event("start", now, position); store.Stage(_session);
    }
    public void SetAudioFormat(double duration, int? sampleRate = null, int? channels = null)
    {
        if (_session is null) return; _session.DurationSeconds = duration; _session.SampleRate = sampleRate; _session.Channels = channels; store.Stage(_session);
    }
    public void Observe(DateTimeOffset now, double position, bool playing, double volume, PlayMode mode, string device, int? offset = null)
    {
        if (_session is not { EndedUtc: null } session) return;
        if (offset.HasValue && _offset != offset.Value) { _offset = offset.Value; Event("utc-offset", now, position, _offset.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (_playing == playing && Math.Abs(session.LastPosition - position) < .0001 && session.Volume == volume && session.PlayMode == mode && session.OutputDevice == device) return;
        if (_playing != playing) { _interval = null; Event(playing ? "resume" : "pause", now, position); }
        if (session.Volume != volume) Event("volume", now, position, volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (session.PlayMode != mode) Event("play-mode", now, position, mode.ToString());
        if (session.OutputDevice != device) Event("output-device", now, position, device);
        _playing = playing; session.State = playing ? "playing" : "paused"; session.Volume = volume; session.PlayMode = mode; session.OutputDevice = device;
        session.UpdatedUtc = now; session.LastPosition = position; store.Stage(session);
    }
    public void Seek(DateTimeOffset now, double position) { if (_session is not { EndedUtc: null }) return; _interval = null; Event("seek", now, position); _session.LastPosition = position; _session.UpdatedUtc = now; store.Stage(_session); }
    public void Credit(DateTimeOffset start, DateTimeOffset end, int offset, double from, double to, double seconds, bool counted)
    {
        if (_session is not { EndedUtc: null } session) return;
        if (_interval is null || _interval.LocalOffsetMinutes != offset || Math.Abs((start - _interval.EndedUtc).TotalSeconds) > .05 || Math.Abs(from - _interval.EndPosition) > .1)
            _interval = new() { Id = session.Id + "-i-" + (++session.IntervalCount).ToString("D8", System.Globalization.CultureInfo.InvariantCulture), SessionId = session.Id, StartedUtc = start, StartPosition = from, LocalOffsetMinutes = offset };
        _interval.EndedUtc = end; _interval.EndPosition = to; _interval.Seconds += seconds; if (counted) _interval.CountedSeconds += seconds;
        session.PlayedSeconds += seconds; if (counted) session.CountedSeconds += seconds;
        session.LastPosition = to; session.UpdatedUtc = end; store.Stage(_interval); store.Stage(session);
    }
    public void CountPlay() { if (_session is { EndedUtc: null } session) { session.PlayCount++; store.Stage(session); } }
    public void End(string reason, DateTimeOffset now)
    {
        if (_session is not { EndedUtc: null } session) return;
        session.EndedUtc = now; session.UpdatedUtc = now; session.State = "ended"; session.EndReason = reason; Event(reason, now, session.LastPosition); store.Stage(session); _interval = null; _playing = false;
    }
    private void Event(string type, DateTimeOffset now, double position, string value = "")
    {
        if (_session is null) return;
        var sequence = ++_session.EventCount;
        store.Stage(new ListeningEventRecord { Id = _session.Id + "-e-" + sequence.ToString("D8", System.Globalization.CultureInfo.InvariantCulture), SessionId = _session.Id, Sequence = sequence, OccurredUtc = now, LocalOffsetMinutes = _offset, Type = type, Position = position, Value = value });
        store.Stage(_session);
    }
}
