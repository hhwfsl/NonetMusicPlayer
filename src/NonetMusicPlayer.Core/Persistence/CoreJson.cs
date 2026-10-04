using System.Text.Json;
using System.Text.Json.Serialization;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Core.Runtime;
using NonetMusicPlayer.Core.Commands;

namespace NonetMusicPlayer.Core.Persistence;

/// <summary>显式生成 JSON 类型信息，使精简发布不依赖运行时反射。</summary>
public static class CoreJson
{
    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All), TypeInfoResolver = CoreJsonContext.Default };
    public static JsonSerializerOptions Rpc { get; } = new(Options) { WriteIndented = false };
    public static JsonSerializerOptions Readable { get; } = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) };
}
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(List<PluginManifest>))]
[JsonSerializable(typeof(ProviderCatalog))]
[JsonSerializable(typeof(PlaybackSource))]
[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(HeadlessPlayerState))]
[JsonSerializable(typeof(HeadlessPlayerSettings))]
[JsonSerializable(typeof(CommandResult))]
internal partial class CoreJsonContext : JsonSerializerContext;
