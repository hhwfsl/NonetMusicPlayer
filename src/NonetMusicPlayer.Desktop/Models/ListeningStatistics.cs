namespace NonetMusicPlayer.Desktop.Models;

/// <summary>按日期和歌曲持久化听歌记录，歌曲移除后仍保留历史元数据。</summary>
public sealed class ListeningEntry
{
    public string Date { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Seconds { get; set; }
    /// <summary>有明确时间戳的累计秒数；旧记录缺省为空，不能据日总量猜测小时分布。</summary>
    public double? TimestampedSeconds { get; set; }
    public int PlayCount { get; set; }
}

public sealed record ListeningRankItem(string Id, string Name, string Artist, double Seconds, int PlayCount);
public sealed record DailyListeningSummary(string Date, double Seconds, int PlayCount);
public sealed record ListeningStatisticsSnapshot(double TotalSeconds, int TotalPlays, int ActiveDays, double TodaySeconds,
    IReadOnlyList<ListeningRankItem> Songs, IReadOnlyList<ListeningRankItem> Artists, IReadOnlyList<DailyListeningSummary> Days);

/// <summary>以 UTC 整点为键，避免夏令时切换使两个本地小时相互覆盖。</summary>
public sealed class HourlyListeningEntry
{
    public long UtcHour { get; set; }
    public int PlayCount { get; set; }
    /// <summary>该 UTC 小时内实际播放的秒数；旧记录缺省为零，不根据次数推算。</summary>
    public double Seconds { get; set; }
}
public sealed record ListeningBucketPoint(string Label, double Value, string Detail);
public sealed record RecentListeningCharts(IReadOnlyList<ListeningBucketPoint> Days, IReadOnlyList<ListeningBucketPoint> Hours)
{
    /// <summary>与次数使用相同时间窗，保留原有 Hours 接口的次数语义。</summary>
    public IReadOnlyList<ListeningBucketPoint> HourDurations { get; init; } = [];
    /// <summary>日汇总存在未分配到小时的历史时，图表不能声称覆盖了完整时间窗。</summary>
    public bool HasIncompleteHourHistory { get; init; }
    public double TodaySeconds { get; init; }
    /// <summary>时间窗补零但不补造历史；每天、每小时只生成一个轻量数据点。</summary>
    public static RecentListeningCharts Build(IEnumerable<ListeningEntry> entries, IEnumerable<HourlyListeningEntry> hourly, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var daily = entries.GroupBy(e => e.Date).ToArray();
        var days = daily.ToDictionary(g => g.Key, g => g.Sum(e => Math.Max(0, e.Seconds)));
        var buckets = hourly.GroupBy(e => e.UtcHour).ToArray();
        var hours = buckets.ToDictionary(g => g.Key, g => g.Sum(e => (double)Math.Max(0, e.PlayCount)));
        var durations = buckets.ToDictionary(g => g.Key, g => g.Sum(e => double.IsFinite(e.Seconds) ? Math.Max(0, e.Seconds) : 0));
        var currentHour = now.ToUnixTimeSeconds() / 3600;
        var recordedDays = buckets.GroupBy(g => DateTimeOffset.FromUnixTimeSeconds(g.Key * 3600).ToOffset(now.Offset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.Sum(h => durations[h.Key]));
        // 新记录直接保存覆盖量，避免半小时时区、跨午夜或夏令时使完整记录被误判为缺失。
        var coveredDays = daily.ToDictionary(g => g.Key, g => g.Any(e => e.TimestampedSeconds.HasValue) ? g.Sum(e => e.TimestampedSeconds ?? 0) : recordedDays.GetValueOrDefault(g.Key));
        var firstDate = DateTimeOffset.FromUnixTimeSeconds((currentHour - 23) * 3600).ToOffset(now.Offset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var todayKey = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new(Enumerable.Range(0, 7).Select(i =>
        {
            var date = today.AddDays(i - 6); var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return new ListeningBucketPoint(date.ToString("MM/dd"), days.GetValueOrDefault(key), key);
        }).ToArray(), Enumerable.Range(0, 24).Select(i =>
        {
            var hour = currentHour + i - 23;
            var time = DateTimeOffset.FromUnixTimeSeconds(hour * 3600).ToOffset(now.Offset);
            return new ListeningBucketPoint(time.ToString("HH"), hours.GetValueOrDefault(hour), time.ToString("yyyy-MM-dd HH:mm zzz"));
        }).ToArray())
        {
            HasIncompleteHourHistory = days.Any(d => string.CompareOrdinal(d.Key, firstDate) >= 0 && string.CompareOrdinal(d.Key, todayKey) <= 0 && d.Value > coveredDays.GetValueOrDefault(d.Key) + .5),
            TodaySeconds = days.GetValueOrDefault(todayKey),
            HourDurations = Enumerable.Range(0, 24).Select(i =>
            {
                var hour = currentHour + i - 23;
                var time = DateTimeOffset.FromUnixTimeSeconds(hour * 3600).ToOffset(now.Offset);
                return new ListeningBucketPoint(time.ToString("HH"), durations.GetValueOrDefault(hour), time.ToString("yyyy-MM-dd HH:mm zzz"));
            }).ToArray()
        };
    }
}
