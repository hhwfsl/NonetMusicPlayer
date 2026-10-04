using System.Globalization;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly TimeProvider _listeningClock;
    private long _listeningTimestamp, _statisticsNotificationTimestamp;
    private double _listeningPosition, _sessionListeningSeconds;
    private bool _hasListeningAnchor, _previouslyListening, _sessionCounted;
    public event EventHandler? StatisticsChanged;

    private void StartListeningSession()
    {
        _sessionListeningSeconds = 0; _sessionCounted = false; ResetListeningAnchor();
    }
    private void ResetListeningAnchor()
    {
        _listeningTimestamp = _listeningClock.GetTimestamp(); _hasListeningAnchor = true;
        try { _listeningPosition = _audioLoaded ? _audio.Position.TotalSeconds : PlaybackPosition; _previouslyListening = _audioLoaded && _audio.IsPlaying; }
        catch { _listeningPosition = PlaybackPosition; _previouslyListening = false; }
    }
    private void UpdateListeningStatistics()
    {
        // 解码位置确认音频确实前进，单调时钟计算实际聆听时长；休眠长间隔及手动跳转不计入时长。
        try
        {
            var now = _listeningClock.GetTimestamp();
            if (!_hasListeningAnchor) { ResetListeningAnchor(); return; }
            var elapsed = _listeningClock.GetElapsedTime(_listeningTimestamp, now).TotalSeconds;
            var position = _audioLoaded ? _audio.Position.TotalSeconds : PlaybackPosition;
            var playing = _audioLoaded && _audio.IsPlaying;
            var advance = position - _listeningPosition;
            var finishedTail = _previouslyListening && !playing && _audioLoaded && position >= _audio.Duration.TotalSeconds - .1;
            var credit = !_disposed && !_loadingAudio && !IsSeeking && CurrentTrack is not null && TrackIndex().ContainsKey(CurrentTrack.Id) && (PlayingSourcePage != "temporary" || _countedTemporaryIds.Contains(CurrentTrack.Id)) && _previouslyListening && (playing || finishedTail)
                && elapsed is > 0 and <= 2 && advance > 0 && double.IsFinite(advance) && advance <= elapsed + .75;
            _listeningTimestamp = now; _listeningPosition = position; _previouslyListening = playing;
            if (!credit) return;
            var seconds = Math.Min(elapsed, advance);
            if (seconds <= 0 || !double.IsFinite(seconds)) return;
            var date = _listeningClock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var track = CurrentTrack!;
            var entry = State.ListeningEntries.FirstOrDefault(item => item.Date == date && item.TrackId == track.Id);
            if (entry is null) { entry = new ListeningEntry { Date = date, TrackId = track.Id, Title = track.Title, Artist = track.Artist }; State.ListeningEntries.Add(entry); }
            entry.Title = track.Title; entry.Artist = track.Artist; entry.Seconds += seconds; _sessionListeningSeconds += seconds;
            // 刚开始即暂停不算一次聆听；恢复同一会话仍只计一次。
            if (!_sessionCounted && _sessionListeningSeconds >= 1) { entry.PlayCount++; _sessionCounted = true; }
            if (_listeningClock.GetElapsedTime(_statisticsNotificationTimestamp, now) >= TimeSpan.FromSeconds(1))
            {
                _statisticsNotificationTimestamp = now; StatisticsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception error) { AppLog.Warning("Statistics", "更新听歌统计失败，本次采样已忽略", error); _hasListeningAnchor = false; }
    }

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
