using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>文件事务由宿主选择器取得路径；请求不能提交任意本机路径，返回值不包含源路径。</summary>
public sealed partial class ExtensionSession
{
    private async Task<JsonObject> FilesServiceAsync(JsonObject args)
    {
        if (!_gesture || !Manifest.Permissions.Contains("user-files")) throw new InvalidDataException("A user gesture and user-files permission are required.");
        var operation = args["operation"]?.GetValue<string>() ?? "";
        var vm = _manager.ExtensionViewModel;
        if (operation is "data-directory" or "backup-directory")
        {
            if (!Manifest.Permissions.Contains("settings-write")) throw new InvalidDataException("Settings permission required.");
            var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = Manifest.Name });
            var folder = folders.FirstOrDefault()?.Path.LocalPath; if (folder is null) return new() { ["success"] = false };
            if (!await ConfirmExtensionAsync("data.directory", true, L10n.T(operation == "backup-directory" ? "Storage.ChangeBackupFolder" : "Storage.AppDataIsBeingCopiedWaitForCompletionBefore"))) return new() { ["success"] = false };
            if (operation == "backup-directory") vm.SetBackupFolder(folder); else await vm.ConfigureDataDirectoryAsync(folder); return new() { ["success"] = true };
        }
        var permission = operation == "temporary-play" ? "player-control" : operation is "font" or "background" ? "settings-write" : operation == "plugin-import" ? "plugins-control" : operation == "lyrics-import" ? "lyrics-write" : "library-write";
        if (!Manifest.Permissions.Contains(permission)) throw new InvalidDataException("File operation permission denied.");
        if (operation is "data-backup" or "data-restore")
        {
            if (operation == "data-backup")
            {
                var target = await Owner.SaveFileAsync(Manifest.Name, "Nonet-backup.json", "json");
                if (target is null) return new() { ["success"] = false };
                vm.Save(); await File.WriteAllTextAsync(target, await File.ReadAllTextAsync(Path.Combine(vm.Storage.Root, "state.json"), _lifetime.Token), _lifetime.Token);
            }
            else
            {
                var files = await Owner.OpenFilesAsync(Manifest.Name, ["*.json"], false);
                if (files.Length == 0 || !await ConfirmExtensionAsync("data.restore", true, Manifest.Name + " · " + L10n.T("Storage.ImportLibraryBackup"))) return new() { ["success"] = false };
                vm.RestoreLibraryBackup(files[0]);
            }
            return new() { ["success"] = true };
        }
        if (operation == "plugin-import")
        {
            var files = await Owner.OpenFilesAsync(Manifest.Name, ["*.impp"], false);
            if (files.Length == 0) return new() { ["success"] = false };
            var incoming = PluginManager.Inspect(files[0]);
            // 安装新可执行代码始终要求信任确认，不受调用插件的自动审批模式影响。
            if (!await PlayerDialog.Confirm(Owner, incoming.Name, incoming.Author + "\n" + incoming.Description + "\n" + string.Join(" / ", incoming.Permissions), L10n.T("Common.TrustAndInstall"))) return new() { ["success"] = false };
            await _manager.InstallAsync(files[0]); vm.ApplySettings(); Owner.RefreshPluginNavigation();
            return new() { ["success"] = true, ["pluginId"] = incoming.Id };
        }
        if (operation == "folder-import")
        {
            var playlist = vm.Playlists.FirstOrDefault(p => p.Id == args["playlistId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown playlist.");
            var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = Manifest.Name });
            var folder = folders.FirstOrDefault()?.Path.LocalPath; if (folder is null) return new() { ["success"] = false };
            await vm.ImportAsync([folder], playlist: playlist); return new() { ["success"] = true };
        }
        if (operation == "cover-reset")
        {
            var type = args["type"]?.GetValue<string>(); var software = args["softwareDefault"]?.GetValue<bool>() == true;
            if (type == "playlist") vm.SetPlaylistCover(vm.Playlists.FirstOrDefault(p => p.Id == args["playlistId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown playlist."), null);
            else if (type is "album" or "artist")
            {
                var name = args["name"]?.GetValue<string>() ?? throw new InvalidDataException("Missing group.");
                if (software) vm.SetGroupSoftwareDefaultCover(type, name); else vm.SetGroupCover(type, name, null);
            }
            else throw new InvalidDataException("Unknown cover type.");
            return new() { ["success"] = true };
        }
        if (operation is "import" or "temporary-play")
        {
            var target = operation == "import" ? vm.Playlists.FirstOrDefault(p => p.Id == args["playlistId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown playlist.") : null;
            var files = await Owner.OpenFilesAsync(Manifest.Name, MusicLibraryScanner.SupportedExtensions.Select(e => "*" + e).ToArray(), true);
            if (files.Length == 0) return new() { ["success"] = false };
            if (target is null) await vm.PlayTemporaryFilesAsync(files); else await vm.ImportAsync(files, playlist: target);
            return new() { ["success"] = true };
        }
        if (operation == "lyrics-import")
        {
            var track = vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == args["trackId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown track.");
            var files = await Owner.OpenFilesAsync(Manifest.Name, ["*.lrc", "*.txt"], false);
            if (files.Length == 0) return new() { ["success"] = false };
            vm.Lyrics.Import(track.Id, files[0]); track.LyricsDisabled = false; track.LyricsSourcePath = vm.Lyrics.PathFor(track.Id);
            if (vm.CurrentTrack?.Id == track.Id) vm.ReloadLyrics(); vm.Save(); return new() { ["success"] = true };
        }
        if (operation is "background" or "playlist-cover" or "album-cover" or "artist-cover")
        {
            var files = await Owner.OpenFilesAsync(Manifest.Name, ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"], false);
            if (files.Length == 0) return new() { ["success"] = false };
            var image = await CoverCropDialog.Show(Owner, vm.Storage, files[0]); if (image is null) return new() { ["success"] = false };
            if (operation == "background") { vm.Settings.BackgroundImagePath = AppBackgroundService.Import(vm.Storage, image); vm.ApplySettings(); }
            else if (operation == "playlist-cover") vm.SetPlaylistCover(vm.Playlists.FirstOrDefault(p => p.Id == args["playlistId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown playlist."), image);
            else vm.SetGroupCover(operation == "album-cover" ? "album" : "artist", args["name"]?.GetValue<string>() ?? throw new InvalidDataException("Missing group."), image);
            return new() { ["success"] = true };
        }
        if (operation == "font")
        {
            var files = await Owner.OpenFilesAsync(Manifest.Name, ["*.ttf", "*.otf"], false);
            if (files.Length == 0) return new() { ["success"] = false };
            vm.Settings.FontFilePath = AppFontService.Import(vm.Storage, files[0]); vm.Settings.FontFamily = ""; vm.ApplySettings(); return new() { ["success"] = true };
        }
        if (operation == "playlist-import")
        {
            var files = await Owner.OpenFilesAsync(Manifest.Name, ["*.m3u", "*.m3u8"], false);
            if (files.Length == 0) return new() { ["success"] = false };
            await vm.ImportM3uAsync(files[0]); return new() { ["success"] = true };
        }
        if (operation is "playlist-export" or "lyrics-export")
        {
            var name = "lyrics.lrc";
            if (operation == "playlist-export")
            {
                var selected = vm.Playlists.FirstOrDefault(p => p.Id == args["playlistId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown playlist.");
                name = L10n.T("Playlists.ExportPrefix") + "_" + (selected.IsSystem ? L10n.T("Playlists.LikedSongs") : selected.Name);
                foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_'); name += ".m3u8";
            }
            var target = await Owner.SaveFileAsync(Manifest.Name, name, operation == "lyrics-export" ? "lrc" : "m3u8");
            if (target is null) return new() { ["success"] = false };
            if (operation == "playlist-export")
            {
                var playlist = vm.Playlists.FirstOrDefault(p => p.Id == args["playlistId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown playlist.");
                // 导出指定歌单，不强制改变用户当前页；路径仅来自本次保存选择器。
                var tracks = vm.State.Tracks.Where(t => playlist.TrackIds.Contains(t.Id) && t.ProviderId is null).ToDictionary(t => t.Id);
                var lines = new List<string> { "#EXTM3U" };
                foreach (var id in playlist.TrackIds) if (tracks.TryGetValue(id, out var track)) { lines.Add("#EXTINF:" + ((int)track.DurationSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + track.Title.Replace('\n', ' ').Replace('\r', ' ')); lines.Add(track.FilePath); }
                File.WriteAllLines(target, lines, new System.Text.UTF8Encoding(false));
            }
            else
            {
                var text = args["text"]?.GetValue<string>() ?? "";
                if (System.Text.Encoding.UTF8.GetByteCount(text) > 2_000_000) throw new InvalidDataException("Lyrics exceed limit.");
                File.WriteAllText(target, text, new System.Text.UTF8Encoding(false));
            }
            return new() { ["success"] = true };
        }
        throw new InvalidDataException("Unsupported file operation.");
    }
}
