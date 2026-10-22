using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>设备曲库复用宿主会话；插件不能读取配对秘密、指定任意地址或越权访问文件。</summary>
public sealed partial class ExtensionSession
{
    private async Task<JsonObject> LanServiceAsync(JsonObject args)
    {
        var vm = _manager.ExtensionViewModel;
        var operation = args["operation"]?.GetValue<string>() ?? "status";
        if (operation is "open" or "set-sharing" or "disconnect" or "set-approval-wait")
        {
            if (!Manifest.Permissions.Contains("network") || !_gesture) throw new InvalidDataException("Direct user action and network permission required.");
            if (operation == "open")
            {
                if (!Manifest.Permissions.Contains("navigation")) throw new InvalidDataException("Navigation permission required.");
                vm.Navigate("devices");
            }
            else
            {
                if (!await ConfirmExtensionAsync("network." + operation, true, Manifest.Name + " · " + L10n.T(operation == "set-sharing" ? "Lan.Share" : operation == "set-approval-wait" ? "Lan.ApprovalWait" : "Lan.Disconnect")))
                    return new() { ["success"] = false, ["reason"] = "User declined; do not retry." };
                if (operation == "set-approval-wait")
                {
                    var seconds = args["seconds"]?.GetValue<int>() ?? 0;
                    Core.Commands.SettingsContract.Validate("lanApprovalWaitSeconds", JsonValue.Create(seconds));
                    vm.Settings.LanApprovalWaitSeconds = seconds; vm.ApplySettings();
                    return new() { ["success"] = true, ["approvalWaitSeconds"] = seconds };
                }
                if (operation == "set-sharing")
                {
                    if (args["enabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out var value)) throw new InvalidDataException("Boolean enabled required.");
                    vm.SetLanSharing(value);
                    return new() { ["success"] = vm.LanSharing == value, ["sharing"] = vm.LanSharing };
                }
                var peer = vm.LanPeers.FirstOrDefault(p => p.DeviceId == args["peerId"]?.GetValue<string>());
                if (peer is null) throw new InvalidDataException("Unknown paired device.");
                vm.DisconnectLan(peer);
            }
            return new() { ["success"] = true };
        }
        if (!Manifest.Permissions.Contains("music-read")) throw new InvalidDataException("Music read permission required.");
        if (operation == "status") return new() { ["success"] = true, ["sharing"] = vm.LanSharing, ["connectedDevices"] = vm.LanPeers.Count, ["bidirectional"] = true, ["approvalWaitSeconds"] = vm.Settings.LanApprovalWaitSeconds, ["pendingRequests"] = vm.LanPendingRequests.Count() };
        var offset = args["offset"]?.GetValue<int>() ?? 0; var limit = args["limit"]?.GetValue<int>() ?? 100;
        if (offset < 0 || limit is < 1 or > 100) throw new InvalidDataException("Invalid query page.");
        IEnumerable<JsonNode?> rows; int total;
        if (operation == "peers") { total = vm.LanPeers.Count; rows = vm.LanPeers.Skip(offset).Take(limit).Select(p => (JsonNode?)new JsonObject { ["id"] = p.DeviceId, ["name"] = p.Name, ["deviceKind"] = p.Catalog.DeviceKind, ["reachable"] = p.Reachable, ["connectionState"] = p.Reachable ? "available" : "reconnecting", ["trackCount"] = p.Catalog.Total, ["playlistCount"] = p.Catalog.Playlists.Length }); }
        else
        {
            var peer = vm.LanPeers.FirstOrDefault(p => p.DeviceId == args["peerId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown paired device.");
            if (operation == "playlists") { total = peer.Catalog.Playlists.Length; rows = peer.Catalog.Playlists.Skip(offset).Take(limit).Select(p => (JsonNode?)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["description"] = p.Description, ["count"] = p.TrackIds.Length, ["page"] = "lan:" + peer.DeviceId + ":" + p.Id }); }
            else if (operation == "tracks") { var tracks = vm.LanPeerTracks(peer.DeviceId); total = tracks.Count; rows = tracks.Skip(offset).Take(limit).Select(t => (JsonNode?)new JsonObject { ["id"] = t.Id, ["title"] = t.Title, ["artist"] = t.Artist, ["album"] = t.Album, ["duration"] = t.DurationSeconds, ["source"] = "device", ["deviceKind"] = t.SourceDeviceKind }); }
            else throw new InvalidDataException("Unknown network operation.");
        }
        var data = rows.ToArray();
        return new() { ["success"] = true, ["data"] = new JsonArray(data), ["offset"] = offset, ["limit"] = limit, ["total"] = total };
    }
}
