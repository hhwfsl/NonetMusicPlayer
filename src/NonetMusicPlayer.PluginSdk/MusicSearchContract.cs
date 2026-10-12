using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>音源可选搜索协议；旧音源未声明时仍只提供目录和播放地址。</summary>
public sealed class MusicSearchSource
{
    public string Label { get; set; } = "";
    public int PageSize { get; set; } = 30;
    /// <summary>可选的按可见行批量获取封面；旧音源默认仍在搜索结果内返回图片。</summary>
    public bool DeferredArtwork { get; set; }
}

public sealed class MusicSearchPage
{
    public List<ProviderTrack> Tracks { get; set; } = [];
    public string? NextCursor { get; set; }
    public int? Total { get; set; }
    /// <summary>可公开展示的状态，不包含 Cookie、签名地址或原始接口响应。</summary>
    public string Message { get; set; } = "";
}

/// <summary>客户端、打包器与插件共用边界校验，不以特定平台或供应商为前提。</summary>
public static class MusicSearchContract
{
    public static void ValidateTrack(ProviderTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.Id) || track.Id.Length > 300 || track.Id.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(track.Title) || track.Title.Length > 1000 || track.Artist is null || track.Album is null || track.Format is null || track.Artist.Length > 1000 || track.Album.Length > 1000
            || !double.IsFinite(track.DurationSeconds) || track.DurationSeconds is < 0 or > 604800
            || track.Format.Length > 24 || track.Format.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
            || track.CoverBase64?.Length > 2_000_000 || track.Lyrics?.Length > 100_000 || track.LyricsTranslation?.Length > 100_000)
            throw new InvalidDataException("Invalid music source metadata.");
    }
    public static void ValidatePage(MusicSearchPage page, int limit)
    {
        if (page.Tracks.Count > limit || page.NextCursor?.Length > 512 || page.Total < 0 || page.Message.Length > 1000)
            throw new InvalidDataException("Invalid music search page.");
        foreach (var track in page.Tracks) ValidateTrack(track);
        if (page.Tracks.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != page.Tracks.Count)
            throw new InvalidDataException("Duplicate music search IDs.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MusicSearchPage))]
[JsonSerializable(typeof(ProviderTrack))]
[JsonSerializable(typeof(ProviderCatalog))]
[JsonSerializable(typeof(PlaybackSource))]
public partial class MusicSourceJson : JsonSerializerContext;
