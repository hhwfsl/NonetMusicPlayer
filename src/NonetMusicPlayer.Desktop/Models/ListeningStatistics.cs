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
