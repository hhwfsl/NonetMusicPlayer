using System.Text.Json;
using NonetMusicPlayer.Desktop.Services;
using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>音源和歌词角色不再与页面类型互斥；通过已声明的通用服务组合，不重建另一套进程协议。</summary>
public sealed partial class PluginManager
{
    public static bool HasMediaSource(PluginManifest plugin) => plugin.Type == "provider" || plugin.Type == "extension" && plugin.ProvidedServices.Contains("media-source");
    public static bool HasLyricsSource(PluginManifest plugin) => plugin.Type == "lyrics" || plugin.Type == "extension" && plugin.ProvidedServices.Contains("lyrics-source");
    private async Task<IReadOnlyList<ProviderTrack>> ReadExtensionCatalogAsync(PluginManifest plugin, CancellationToken token)
    {
        if (!plugin.Enabled || !plugin.Permissions.Contains("network")) throw new InvalidDataException("Media source must be enabled and declare network permission.");
        var tracks = new List<ProviderTrack>(); var cursors = new HashSet<string>(StringComparer.Ordinal); var cursor = "";
        do
        {
            if (!cursors.Add(cursor)) throw new InvalidDataException("Repeated media cursor.");
            var result = await Extension(plugin).InvokeProvidedServiceAsync("media-source", new() { ["method"] = "catalog.list", ["cursor"] = cursor }, token);
            var page = result.Deserialize<ProviderCatalog>(AppStorage.Json) ?? throw new InvalidDataException("Empty media catalog.");
            if (page.Tracks.Count > 1000 || tracks.Count + page.Tracks.Count > 50000 || page.Tracks.Any(t => t.Id.Length is 0 or > 300 || t.Title.Length is 0 or > 1000 || !double.IsFinite(t.DurationSeconds) || t.DurationSeconds < 0))
                throw new InvalidDataException("Invalid media catalog.");
            tracks.AddRange(page.Tracks); cursor = page.NextCursor ?? "";
        } while (cursor.Length > 0);
        return tracks.DistinctBy(t => t.Id).ToArray();
    }
    private async Task<JsonElement> CallExtensionLyricsAsync(PluginManifest plugin, string method, Dictionary<string, string> arguments, CancellationToken token)
    {
        if (!plugin.Permissions.Contains("network") || !plugin.Permissions.Contains("lyrics-write")) throw new InvalidDataException("Lyrics source permissions required.");
        var request = new JsonObject { ["method"] = method };
        foreach (var pair in arguments) request[pair.Key] = pair.Value;
        return await Extension(plugin).InvokeProvidedServiceAsync("lyrics-source", request, token);
    }
}
