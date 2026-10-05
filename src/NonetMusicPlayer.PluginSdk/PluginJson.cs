using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>独立 SDK 的清单序列化信息，不依赖播放器的状态类型或反射。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(PluginManifest))]
internal partial class PluginJsonContext : JsonSerializerContext;

/// <summary>宿主可注入资源查询；独立开发工具使用内建英文提示。</summary>
public static class PluginMessages
{
    public static Func<string, string>? Translate { get; set; }
    public static string Get(string key) => Translate?.Invoke(key) ?? key switch
    {
        "Library.UnknownArtist" => "Unknown artist", "Library.UnknownAlbum" => "Unknown album",
        "Plugins.PluginPackagesMustUseTheImppExtension" => "Plugin packages must use the .impp extension.",
        "Plugins.NoPlatformPackage" => "No plugin package is available for the current platform.",
        "Plugins.PluginConfigurationNestingIsTooDeep" => "Plugin configuration nesting is too deep.", _ => key
    };
}
