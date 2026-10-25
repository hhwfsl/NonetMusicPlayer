using System.Globalization;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly TimeProvider _listeningClock;
    private readonly ListeningSessionRecorder _listeningJournal;
    private long _listeningTimestamp, _statisticsNotificationTimestamp;
    private double _listeningPosition, _sessionListeningSeconds;
    private bool _hasListeningAnchor, _previouslyListening, _sessionCounted;
    private bool _statisticsErrorReported;
    public event EventHandler? StatisticsChanged;

    private void StartListeningSession()
    {
        if (CurrentTrack is { } track)
            _listeningJournal.Start(track, PlayingSourcePage, State.Playlists.FirstOrDefault(p => p.Id == PlayingPlaylistId), _listeningClock.GetUtcNow(), (int)_listeningClock.GetLocalNow().Offset.TotalMinutes, PlaybackPosition, Volume, Settings.PlayMode, Settings.DeviceName);
        var format = (_audio as NonetMusicPlayer.Core.Audio.NativeAudioPlayer)?.DecodedFormat;
        _listeningJournal.SetAudioFormat(PlaybackDuration, format.HasValue ? (int)format.Value.SampleRate : null, format.HasValue ? (int)format.Value.Channels : null);
        _sessionListeningSeconds = 0; _sessionCounted = false; ResetListeningAnchor();
    }
    private void ResetListeningAnchor()
    {
        _listeningTimestamp = _listeningClock.GetTimestamp(); _hasListeningAnchor = true;
        try { _listeningPosition = _audioLoaded ? _audio.Position.TotalSeconds : PlaybackPosition; _previouslyListening = _audioLoaded && _audio.IsPlaying; }
        catch { _listeningPosition = PlaybackPosition; _previouslyListening = false; }
        _listeningJournal.Observe(_listeningClock.GetUtcNow(), _listeningPosition, _previouslyListening, Volume, Settings.PlayMode, Settings.DeviceName, (int)_listeningClock.GetLocalNow().Offset.TotalMinutes);
    }
    private void UpdateListeningStatistics()
    {
        // 同时要求解码位置前进和单调时钟前进；界面延迟不会丢掉已播放的音频，暂停与跳转不计入。
        try
        {
            var now = _listeningClock.GetTimestamp();
            if (!_hasListeningAnchor) { ResetListeningAnchor(); return; }
            var elapsed = _listeningClock.GetElapsedTime(_listeningTimestamp, now).TotalSeconds;
            var position = _audioLoaded ? _audio.Position.TotalSeconds : PlaybackPosition;
            var playing = _audioLoaded && _audio.IsPlaying;
            var advance = position - _listeningPosition;
            var finishedTail = _previouslyListening && !playing && _audioLoaded && position >= _audio.Duration.TotalSeconds - .1;
            var from = _listeningPosition;
            var credit = !_disposed && !_loadingAudio && !IsSeeking && CurrentTrack is not null && _previouslyListening && (playing || finishedTail)
                && elapsed > 0 && double.IsFinite(elapsed) && advance > 0 && double.IsFinite(advance) && advance <= elapsed + .75;
            _listeningTimestamp = now; _listeningPosition = position; _previouslyListening = playing;
            var utcNow = _listeningClock.GetUtcNow();
            _listeningJournal.Observe(utcNow, position, playing || finishedTail, Volume, Settings.PlayMode, Settings.DeviceName, (int)_listeningClock.GetLocalNow().Offset.TotalMinutes);
            if (!credit) return;
            var seconds = Math.Min(elapsed, advance);
            if (seconds <= 0 || !double.IsFinite(seconds)) return;
            var track = CurrentTrack!;
            var counted = TrackIndex().ContainsKey(track.Id) && (PlayingSourcePage != "temporary" || _countedTemporaryIds.Contains(track.Id));
            var start = utcNow.AddSeconds(-seconds);
            _listeningJournal.Credit(start, utcNow, (int)_listeningClock.GetLocalNow().Offset.TotalMinutes, from, position, seconds, counted);
            _statisticsErrorReported = false;
            if (!counted) return;
            _sessionListeningSeconds += seconds;
            var hour = utcNow.ToUnixTimeSeconds() / 3600;
            State.HourlyListeningEntries.RemoveAll(e => e.UtcHour < hour - 71 || e.UtcHour > hour);
            HourlyListeningEntry Bucket(long key)
            {
                var bucket = State.HourlyListeningEntries.FirstOrDefault(e => e.UtcHour == key);
                if (bucket is null) { bucket = new() { UtcHour = key }; State.HourlyListeningEntries.Add(bucket); }
                return bucket;
            }
            // 跨整点的采样按实际重叠时间分配，避免整首歌的时长只落在开始播放的小时。
            while (start < utcNow)
            {
                var key = start.ToUnixTimeSeconds() / 3600;
                var boundary = DateTimeOffset.FromUnixTimeSeconds((key + 1) * 3600);
                var end = boundary < utcNow ? boundary : utcNow;
                var local = TimeZoneInfo.ConvertTime(start, _listeningClock.LocalTimeZone);
                // 非整小时的时区也按本地午夜分段，日汇总和小时汇总来源相同。
                var midnight = new DateTimeOffset(local.Date.AddDays(1), local.Offset).ToUniversalTime();
                if (midnight < end) end = midnight;
                var date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var entry = Daily(date);
                entry.Seconds += (end - start).TotalSeconds;
                entry.TimestampedSeconds = (entry.TimestampedSeconds ?? 0) + (end - start).TotalSeconds;
                var bucket = Bucket(key);
                bucket.Seconds = Math.Min(3600, bucket.Seconds + (end - start).TotalSeconds);
                Storage.ListeningJournal.Stage(bucket);
                start = end;
            }
            // 刚开始即暂停不算一次聆听；恢复同一会话仍只计一次。
            if (!_sessionCounted && _sessionListeningSeconds >= 1)
            {
                Daily(_listeningClock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).PlayCount++; _sessionCounted = true;
                var bucket = Bucket(hour); bucket.PlayCount++; Storage.ListeningJournal.Stage(bucket); _listeningJournal.CountPlay();
            }
            if (_listeningClock.GetElapsedTime(_statisticsNotificationTimestamp, now) >= TimeSpan.FromSeconds(1))
            {
                _statisticsNotificationTimestamp = now; StatisticsChanged?.Invoke(this, EventArgs.Empty);
            }
            ListeningEntry Daily(string date)
            {
                var entry = State.ListeningEntries.FirstOrDefault(item => item.Date == date && item.TrackId == track.Id);
                if (entry is null) { entry = new ListeningEntry { Date = date, TrackId = track.Id }; State.ListeningEntries.Add(entry); }
                entry.Title = track.Title; entry.Artist = track.Artist; return entry;
            }
        }
        catch (Exception error)
        {
            _hasListeningAnchor = false;
            if (!_statisticsErrorReported) { _statisticsErrorReported = true; ReportError(L10n.T("Statistics.UnableToRecordHistory"), error); }
            else AppLog.Warning("Statistics", "更新听歌统计失败，本次采样已忽略", error);
        }
    }

    public RecentListeningCharts GetRecentListeningCharts() => RecentListeningCharts.Build(State.ListeningEntries, State.HourlyListeningEntries, _listeningClock.GetLocalNow());

    public ListeningStatisticsSnapshot GetStatistics(DateOnly? selectedDate = null, string period = "all")
    {
        IEnumerable<ListeningEntry> source = State.ListeningEntries;
        if (selectedDate is { } date)
        {
            var prefix = date.ToString(period == "year" ? "yyyy" : period == "month" ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
            source = source.Where(entry => entry.Date.StartsWith(prefix, StringComparison.Ordinal));
        }
        var entries = source.ToArray();
        var songs = entries.GroupBy(entry => entry.TrackId, StringComparer.Ordinal)
            .Select(group => { var latest = group.OrderByDescending(entry => entry.Date, StringComparer.Ordinal).First(); return new ListeningRankItem(group.Key, latest.Title, latest.Artist, group.Sum(entry => entry.Seconds), group.Sum(entry => entry.PlayCount)); })
            .OrderByDescending(item => item.Seconds).ThenByDescending(item => item.PlayCount).ThenBy(item => item.Name, StringComparer.CurrentCulture).ToArray();
        var artists = entries.SelectMany(entry => SplitArtists(entry.Artist).Select(artist => (Artist: artist, Entry: entry)))
            .GroupBy(item => item.Artist, StringComparer.Ordinal)
            .Select(group => new ListeningRankItem(group.Key, group.Key, "", group.Sum(item => item.Entry.Seconds), group.Sum(item => item.Entry.PlayCount)))
            .OrderByDescending(item => item.Seconds).ThenByDescending(item => item.PlayCount).ThenBy(item => item.Name, StringComparer.CurrentCulture).ToArray();
        var days = entries.GroupBy(entry => entry.Date, StringComparer.Ordinal)
            .Select(group => new DailyListeningSummary(group.Key, group.Sum(entry => entry.Seconds), group.Sum(entry => entry.PlayCount)))
            .OrderByDescending(item => item.Date, StringComparer.Ordinal).ToArray();
        var today = _listeningClock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new(entries.Sum(entry => entry.Seconds), entries.Sum(entry => entry.PlayCount), days.Count(day => day.Seconds > 0), days.FirstOrDefault(day => day.Date == today)?.Seconds ?? 0, songs, artists, days);
    }
    private static IEnumerable<string> SplitArtists(string artists) => artists.Split([" / ", ";", "；", "、"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).DefaultIfEmpty(L10n.T("Library.UnknownArtist")).Distinct(StringComparer.Ordinal);
}
