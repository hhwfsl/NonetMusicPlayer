namespace NonetMusicPlayer.Desktop.Models;

/// <summary>按日期和歌曲持久化听歌记录，歌曲移除后仍保留历史元数据。</summary>
public sealed class ListeningEntry
{
    public string Date { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Seconds { get; set; }
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
}
public sealed record ListeningBucketPoint(string Label, double Value, string Detail);
public sealed record RecentListeningCharts(IReadOnlyList<ListeningBucketPoint> Days, IReadOnlyList<ListeningBucketPoint> Hours)
{
    /// <summary>时间窗补零但不补造历史；每天、每小时只生成一个轻量数据点。</summary>
    public static RecentListeningCharts Build(IEnumerable<ListeningEntry> entries, IEnumerable<HourlyListeningEntry> hourly, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var days = entries.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.Sum(e => Math.Max(0, e.Seconds)));
        var hours = hourly.GroupBy(e => e.UtcHour).ToDictionary(g => g.Key, g => g.Sum(e => (double)Math.Max(0, e.PlayCount)));
        var currentHour = now.ToUnixTimeSeconds() / 3600;
        return new(Enumerable.Range(0, 7).Select(i =>
        {
            var date = today.AddDays(i - 6); var key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return new ListeningBucketPoint(date.ToString("MM/dd"), days.GetValueOrDefault(key), key);
        }).ToArray(), Enumerable.Range(0, 24).Select(i =>
        {
            var hour = currentHour + i - 23;
            var time = DateTimeOffset.FromUnixTimeSeconds(hour * 3600).ToOffset(now.Offset);
            return new ListeningBucketPoint(time.ToString("HH"), hours.GetValueOrDefault(hour), time.ToString("yyyy-MM-dd HH:mm zzz"));
        }).ToArray());
    }
}
