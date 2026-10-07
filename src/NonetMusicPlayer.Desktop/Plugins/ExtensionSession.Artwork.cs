using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>封面按已知对象 ID 查询，仅返回有界缩略图，不向插件暴露本机路径。</summary>
public sealed partial class ExtensionSession
{
    private async Task<JsonObject> ArtworkServiceAsync(JsonObject args)
    {
        if (!Manifest.Permissions.Contains("music-read")) throw new InvalidDataException("Music read permission required.");
        var vm = _manager.ExtensionViewModel;
        var kind = args["type"]?.GetValue<string>() ?? "track";
        var id = args["id"]?.GetValue<string>() ?? "";
        string? path = kind switch
        {
            "track" => vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == id)?.CoverPath,
            "playlist" => vm.Playlists.FirstOrDefault(p => p.Id == id) is { } playlist ? vm.GetPlaylistArtworkPath(playlist) : null,
            _ => throw new InvalidDataException("Unsupported artwork type.")
        };
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new() { ["success"] = true, ["data"] = null };
        var size = Math.Clamp(args["size"]?.GetValue<int>() ?? 256, 32, 512);
        var data = await Task.Run(() =>
        {
            PluginPathPolicy.RejectLinkedAncestors(path);
            if (new FileInfo(path).Length > 32_000_000) throw new InvalidDataException("Artwork input exceeds limit.");
            using var source = File.OpenRead(path); using var bitmap = Bitmap.DecodeToWidth(source, size);
            using var output = new MemoryStream(); bitmap.Save(output, PngBitmapEncoderOptions.Default);
            if (output.Length > 2_000_000) throw new InvalidDataException("Artwork thumbnail exceeds limit.");
            return Convert.ToBase64String(output.ToArray());
        }, _lifetime.Token);
        return new() { ["success"] = true, ["mimeType"] = "image/png", ["data"] = data };
    }
}
