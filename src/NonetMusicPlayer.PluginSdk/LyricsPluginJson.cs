using System.Text.Json;
using System.Text.Json.Serialization;
namespace NonetMusicPlayer.Core.Plugins;

/// <summary>宿主在裁剪发布中使用明确的歌词协议类型，不依赖反射发现 DTO。</summary>
public static class LyricsPluginJson
{
    public static JsonSerializerOptions Options => LyricsJsonContext.Default.Options;
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LyricsQuery))]
[JsonSerializable(typeof(LyricsCandidate))]
[JsonSerializable(typeof(LyricsSearchResult))]
[JsonSerializable(typeof(LyricsFetchResult))]
internal partial class LyricsJsonContext : JsonSerializerContext;
