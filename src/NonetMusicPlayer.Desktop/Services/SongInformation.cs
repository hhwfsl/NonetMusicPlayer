using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>信息弹窗的只读快照：按需读取本地文件，不扩充资料库模型，也不请求云端音频。</summary>
internal sealed record SongInformation
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AlbumArtist { get; init; } = "";
    public string Composer { get; init; } = "";
    public string Genre { get; init; } = "";
    public uint Year { get; init; }
    public uint Track { get; init; }
    public uint TrackCount { get; init; }
    public uint Disc { get; init; }
    public uint DiscCount { get; init; }
    public string Codec { get; init; } = "";
    public int Bitrate { get; init; }
    public int SampleRate { get; init; }
    public int BitsPerSample { get; init; }
    public int Channels { get; init; }
    public double Duration { get; init; }
    public long FileSize { get; init; }
    public DateTime? Modified { get; init; }
    public bool FileAvailable { get; init; }
    public string NoticeKey { get; init; } = "";
    public static SongInformation Cached(TrackItem track) => new()
    {
        Title = track.Title, Artist = track.Artist, Album = track.Album,
        Duration = track.DurationSeconds, FileSize = track.FileSize
    };

    public static SongInformation Read(TrackItem track)
    {
        var snapshot = Cached(track);
        // 插件和局域网曲目仅展示已有资料，不把 URI 或临时播放地址当作本地文件探测。
        if (track.ProviderId is not null) return snapshot;
        try
        {
            var file = new FileInfo(track.FilePath);
            if (!file.Exists) return snapshot with { NoticeKey = "SongInfo.FileUnavailable" };
            snapshot = snapshot with { FileAvailable = true, FileSize = file.Length, Modified = file.LastWriteTime };
            using var tagged = TagLib.File.Create(track.FilePath);
            var tag = tagged.Tag; var properties = tagged.Properties;
            return snapshot with
            {
                Title = Fallback(tag.Title, snapshot.Title), Artist = Fallback(Join(tag.Performers), snapshot.Artist), Album = Fallback(tag.Album, snapshot.Album),
                AlbumArtist = Join(tag.AlbumArtists), Composer = Join(tag.Composers), Genre = Join(tag.Genres), Year = tag.Year,
                Track = tag.Track, TrackCount = tag.TrackCount, Disc = tag.Disc, DiscCount = tag.DiscCount,
                Codec = properties.Description ?? "", Bitrate = properties.AudioBitrate, SampleRate = properties.AudioSampleRate,
                BitsPerSample = properties.BitsPerSample, Channels = properties.AudioChannels,
                Duration = properties.Duration.TotalSeconds > 0 ? properties.Duration.TotalSeconds : snapshot.Duration
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            // 元数据缺失或损坏仍保留已有信息；不写标签，也不把单次读取失败升级为操作错误弹窗。
            return snapshot with { NoticeKey = "SongInfo.MetadataUnavailable" };
        }
    }
    private static string Join(IEnumerable<string> values) => string.Join(" / ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
    private static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
