namespace NonetMusicPlayer.Desktop.Models;

/// <summary>一次实际播放的元数据快照，不依赖歌曲、歌单或最近播放记录继续存在。</summary>
public sealed class ListeningSessionRecord
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Format { get; set; } = "";
    public long FileSize { get; set; }
    public string? LocalFilePath { get; set; }
    public double DurationSeconds { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string SourceType { get; set; } = "local";
    public string? ProviderId { get; set; }
    public string? ProviderTrackId { get; set; }
    public string SourceDeviceKind { get; set; } = "unknown";
    public string SourcePage { get; set; } = "";
    public string? PlaylistId { get; set; }
    public string? PlaylistName { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public int LocalOffsetMinutes { get; set; }
    public double StartPosition { get; set; }
    public double LastPosition { get; set; }
    public double PlayedSeconds { get; set; }
    public double CountedSeconds { get; set; }
    public int PlayCount { get; set; }
    public int EventCount { get; set; }
    public int IntervalCount { get; set; }
    public double InitialVolume { get; set; }
    public PlayMode InitialPlayMode { get; set; }
    public string InitialOutputDevice { get; set; } = "";
    public double Volume { get; set; }
    public PlayMode PlayMode { get; set; }
    public string OutputDevice { get; set; } = "";
    public string State { get; set; } = "playing";
    public string? EndReason { get; set; }
}

/// <summary>连续播放片段；暂停、跳转和时钟变更会分段，保留真实播放位置和有效秒数。</summary>
public sealed class ListeningIntervalRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset EndedUtc { get; set; }
    public int LocalOffsetMinutes { get; set; }
    public double StartPosition { get; set; }
    public double EndPosition { get; set; }
    public double Seconds { get; set; }
    public double CountedSeconds { get; set; }
}

/// <summary>播放行为时间线，只记录播放器自身状态，不存储请求头、密钥或临时播放 URL。</summary>
public sealed class ListeningEventRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public int Sequence { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public int LocalOffsetMinutes { get; set; }
    public string Type { get; set; } = "";
    public double Position { get; set; }
    public string Value { get; set; } = "";
}

/// <summary>批量事务使用的不可变快照，修订号用于写入成功后确认缓冲区。</summary>
public sealed record ListeningJournalDocument(string Kind, string Id, string SessionId, long Time, bool Closed, string Payload, long Revision = 0);
