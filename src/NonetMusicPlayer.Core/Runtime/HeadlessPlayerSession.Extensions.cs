using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private PlayerCommandRouter? _extensionCommands;
    private sealed class ExtensionBackend(HeadlessPlayerSession owner) : IPlayerCommandBackend
    {
        public bool HasWindow => false;
        public Task<CommandResult> ExecuteAsync(PlayerCommand command, CancellationToken token) => owner.ExecuteLockedAsync(command, token);
    }
    /// <summary>扩展只经已有权限策略访问数据和命令，不授予路径、Shell 或隐式危险操作确认。</summary>
    private async Task<JsonObject> HandleExtensionRequestAsync(PluginManifest plugin, ExtensionHostRequest request, CancellationToken token)
    {
        var args = request.Arguments;
        if (request.Service == "catalog")
        {
            var catalog = UniversalPluginCommandPolicy.CatalogFor(plugin.Permissions);
            if (catalog["commands"] is JsonArray commands) foreach (var item in commands.Where(n => PlayerCommandRouter.Definitions.Any(d => d.DesktopOnly && d.Name == n?["operation"]?.GetValue<string>())).ToArray()) commands.Remove(item);
            var settings = System.Text.Json.JsonSerializer.SerializeToNode(State.Settings, CoreJson.Options)!.AsObject();
            if (catalog["settings"] is JsonArray keys) foreach (var key in keys.Where(k => !settings.ContainsKey(k!.GetValue<string>())).ToArray()) keys.Remove(key);
            return catalog;
        }
        if (request.Service == "commands")
        {
            var operation = args["operation"]!.GetValue<string>(); var arguments = args["arguments"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
            var encoded = UniversalPluginCommandPolicy.ValidateFor(new(request.Id, operation, arguments), plugin.Permissions);
            var definition = PlayerCommandRouter.Definitions.Single(d => d.Name == operation);
            if (definition.DesktopOnly) return new() { ["success"] = false, ["reason"] = "This operation needs a desktop window." };
            if (PluginCommandPolicy.RequiresConfirmation(operation)) return new() { ["success"] = false, ["reason"] = "Run and confirm this operation directly in the terminal." };
            var known = State.Tracks.Concat(State.RecentTemporaryTracks).Concat(_musicResults).Concat(_lanTracks.Values).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            if (operation is "player.play" or "music.info" or "favorite.add" or "favorite.remove" or "music.remove")
                foreach (var id in arguments) if (!known.Contains(id)) throw new InvalidDataException("Track identity required.");
            if (operation is "playlist.add" or "playlist.remove" or "playlist.move")
                foreach (var id in arguments.Skip(1).Take(operation == "playlist.move" ? 1 : int.MaxValue)) if (!known.Contains(id)) throw new InvalidDataException("Track identity required.");
            _extensionCommands ??= new(new ExtensionBackend(this)); var result = await _extensionCommands.ExecuteAsync(encoded, cancellationToken: token);
            return new() { ["success"] = result.Success, ["data"] = PluginCommandPolicy.Sanitize(result.Data) };
        }
        if (request.Service == "lyrics")
        {
            var track = FindTrack(args["trackId"]!.GetValue<string>()); var operation = args["operation"]?.GetValue<string>() ?? "read";
            var path = Path.Combine(LyricsDirectory, LyricFileId(track) + ".lrc");
            if (operation == "read")
            {
                if (!plugin.Permissions.Contains("music-read")) throw new InvalidDataException("Music read permission required.");
                return new() { ["success"] = true, ["text"] = State.DisabledLyrics.Contains(track.Id) || !File.Exists(path) ? "" : await File.ReadAllTextAsync(path, token), ["disabled"] = State.DisabledLyrics.Contains(track.Id) };
            }
            if (!plugin.Permissions.Contains("lyrics-write")) throw new InvalidDataException("Lyrics write permission required.");
            if (operation == "unlink") State.DisabledLyrics.Add(track.Id);
            else if (operation == "associate")
            {
                var text = args["text"]!.GetValue<string>(); if (text.Length is 0 or > 1_000_000) throw new InvalidDataException("Invalid lyrics text.");
                Directory.CreateDirectory(LyricsDirectory); AtomicFile.Write(path, text); State.DisabledLyrics.Remove(track.Id);
            }
            else throw new InvalidDataException("Unsupported lyrics operation.");
            Save(); return new() { ["success"] = true };
        }
        return new() { ["success"] = false, ["reason"] = "This service is not available in the headless host." };
    }
}
