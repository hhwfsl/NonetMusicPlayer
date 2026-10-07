using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>编辑软件内的歌曲元数据，不改源音频标签；数据事务、引用关系和统计由宿主维护。</summary>
public sealed partial class ExtensionSession
{
    private async Task<JsonObject> MetadataServiceAsync(JsonObject args)
    {
        if (!Manifest.Permissions.Contains("library-write")) throw new InvalidDataException("Library write permission required.");
        var vm = _manager.ExtensionViewModel;
        var track = vm.State.Tracks.FirstOrDefault(t => t.Id == args["trackId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown track.");
        var values = args["values"]?.AsObject() ?? throw new InvalidDataException("Missing metadata.");
        foreach (var pair in values)
            if (pair.Key is not ("title" or "artist" or "album") || pair.Value?.GetValue<string>() is not { } value || value.Length is 0 or > 1000 || value.Any(char.IsControl))
                throw new InvalidDataException("Invalid metadata field.");
        if (!await ConfirmExtensionAsync("metadata.update", true, Manifest.Name + " · " + track.Title)) return new() { ["success"] = false };
        vm.UpdateExtensionMetadata(track, values); return new() { ["success"] = true };
    }
}
