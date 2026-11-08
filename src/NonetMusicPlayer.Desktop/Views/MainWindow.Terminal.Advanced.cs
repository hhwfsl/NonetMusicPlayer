using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private sealed partial class DesktopCommandBackend
    {
        // 命令复用按钮使用的业务入口，不另外维护队列、歌词或插件状态。
        private async Task<JsonNode?> ExecuteAdvancedAsync(PlayerCommand command, CancellationToken token)
        {
            var vm = window._vm!;
            string Arg(int n) => CommandResults.Required(command, n);
            PluginManifest Plugin() => vm.Plugins.Installed.FirstOrDefault(p => p.Id == Arg(0)) ?? throw new InvalidDataException(L10n.T("Commands.PluginNotFound"));
            switch (command.Name)
            {
                case "player.stop": vm.StopPlaybackCommand.Execute(null); return null;
                case "queue.select": await vm.PlayQueueTrackAsync(vm.PlaybackQueue.FirstOrDefault(t => t.Id == Arg(0)) ?? throw new InvalidDataException(L10n.T("Commands.NotInPlaylist"))); return null;
                case "queue.move":
                    var before = vm.PlaybackQueue.Select(t => t.Id).ToArray(); var after = before.ToList();
                    if (!after.Remove(Arg(0))) throw new InvalidDataException(L10n.T("Commands.NotInPlaylist"));
                    after.Insert(CommandResults.Integer(command, 1, 1, before.Length) - 1, Arg(0));
                    if (!vm.ReorderPlaybackQueue(before, after)) throw new InvalidOperationException("Queue changed; retry the command."); return Tracks(vm.PlaybackQueue);
                case "queue.show": case "queue.hide": case "queue.toggle": window.SetPlaybackQueueOpen(command.Name == "queue.show" || command.Name == "queue.toggle" && !window.IsPlaybackQueueOpen); return null;
                case "player.locate": vm.Navigate(vm.PlayingSourcePage); window.LocatePlayingTrack(window, EventArgs.Empty); return null;
                case "playlist.tracks":
                    var list = vm.Playlists.FirstOrDefault(p => p.Id == Arg(0)) ?? throw new InvalidDataException(L10n.T("Commands.PlaylistNotFound"));
                    var byId = vm.State.Tracks.ToDictionary(t => t.Id); return Tracks(list.TrackIds.Select(id => byId.GetValueOrDefault(id)).OfType<TrackItem>());
                case "playlist.from-folders": return new JsonArray((await vm.CreatePlaylistsFromFoldersAsync(command.Arguments)).Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["count"] = p.TrackIds.Count }).ToArray());
                case "music.sources": return new JsonArray(new[] { new JsonObject { ["id"] = "local", ["name"] = L10n.T("Library.MusicLibrary") } }.Concat(vm.Plugins.MusicSearchSources.Select(p => new JsonObject { ["id"] = p.Id, ["name"] = p.MusicSearch!.Label })).Cast<JsonNode>().ToArray());
                case "music.query":
                    if (Arg(0) != "local" && !vm.Plugins.MusicSearchSources.Any(p => p.Id == Arg(0))) throw new InvalidDataException("Music search source unavailable.");
                    await vm.SearchMusicAsync(Arg(0) == "local" ? "" : Arg(0), Arg(1)); return SearchResults();
                case "music.more": await vm.LoadMoreMusicResultsAsync(); return SearchResults();
                case "music.results": return SearchResults();
                case "album.list": case "artist.list":
                    var kind = command.Name.Split('.')[0]; return new JsonArray(vm.State.Tracks.GroupBy(t => ViewModels.MainViewModel.GroupDisplayName(kind, kind == "album" ? t.Album : t.Artist)).OrderBy(g => g.Key).Select(g => (JsonNode)new JsonObject { ["name"] = g.Key, ["count"] = g.Count() }).ToArray());
                case "album.tracks": case "artist.tracks": var type = command.Name.Split('.')[0]; return Tracks(vm.State.Tracks.Where(t => ViewModels.MainViewModel.GroupDisplayName(type, type == "album" ? t.Album : t.Artist) == Arg(0)));
                case "lyrics.reload": vm.ReloadLyrics(); return TracksLyrics();
                case "lyrics.clear": if (vm.CurrentTrack is null) throw new InvalidOperationException(L10n.T("Commands.SelectTrack")); vm.CancelLyricsAssociation(); return null;
                case "lyrics.export":
                    if (vm.CurrentTrack is null || vm.CurrentTrack.LyricsDisabled) throw new FileNotFoundException("No associated lyrics.");
                    var lyric = vm.Lyrics.ReadForTrack(vm.CurrentTrack.Id, vm.CurrentTrack.FilePath); if (string.IsNullOrWhiteSpace(lyric)) throw new FileNotFoundException("No associated lyrics.");
                    using (var stream = new FileStream(System.IO.Path.GetFullPath(Arg(0)), FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(stream)) await writer.WriteAsync(lyric.AsMemory(), token); return null;
                case "statistics.hours":
                    vm.RefreshPlaybackState(); var hours = command.Arguments.Count == 0 ? 24 : CommandResults.Integer(command, 0, 1, 72); var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
                    return new JsonArray(Enumerable.Range(0, hours).Select(i => now - hours + 1 + i).Select(h => (JsonNode)new JsonObject { ["utcHour"] = h, ["start"] = DateTimeOffset.FromUnixTimeSeconds(h * 3600).ToString("o"), ["seconds"] = vm.State.HourlyListeningEntries.Where(e => e.UtcHour == h).Sum(e => e.Seconds) }).ToArray());
                case "plugins.info": var plugin = Plugin(); return new JsonObject { ["id"] = plugin.Id, ["name"] = plugin.Name, ["version"] = plugin.Version, ["runtime"] = plugin.Runtime, ["enabled"] = plugin.Enabled, ["permissions"] = Strings(plugin.Permissions), ["services"] = Strings(plugin.ProvidedServices), ["search"] = plugin.MusicSearch is not null };
                case "plugins.trust": var trusted = Plugin(); if (trusted.Runtime != "managed") throw new InvalidOperationException("Managed plugin required."); trusted.ManagedExecutionConsent = true; vm.Plugins.Save(); return null;
                case "plugins.reload": var reload = Plugin(); var enabled = reload.Enabled; vm.DisablePlugin(reload); if (enabled) vm.Plugins.SetEnabled(reload, true); vm.ApplySettings(); return null;
                case "plugins.state": var state = vm.Plugins.Extension(Plugin()); await state.StartAsync(); return state.Frame.State.DeepClone();
                case "plugins.invoke": var extension = vm.Plugins.Extension(Plugin()); await extension.StartAsync(); await extension.InvokeAsync(Arg(1), command.Arguments.Count > 2 ? JsonNode.Parse(Arg(2)) as JsonObject ?? throw new InvalidDataException("JSON object required.") : new(), userGesture: true); return extension.Frame.State.DeepClone();
                case "window.default-size": window.RestoreDefaultWindowSize(); return null;
                case "layout.edit": window.Workspace.SetEditing(bool.Parse(Arg(0))); return null;
                case "layout.export": using (var file = new FileStream(System.IO.Path.GetFullPath(Arg(0)), FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(file)) await writer.WriteAsync(window.LayoutConfiguration.AppliedJson.AsMemory(), token); return null;
                case "layout.apply": if (new FileInfo(Arg(0)).Length > UiLayoutService.MaximumBytes) throw new InvalidDataException("Layout file is too large."); window.ApplyLayoutConfiguration(await File.ReadAllTextAsync(Arg(0), token)); return null;
                case "updates.check":
                    // 终端直接返回检查结果，网络错误按普通命令错误显示，不伪装成“没有更新”。
                    window._availableUpdate = await window._updates.CheckAsync(window.VersionNumber, cancellationToken: token); window._newVersion!.IsVisible = window._availableUpdate is not null;
                    return new JsonObject { ["current"] = window.VersionNumber, ["available"] = window._availableUpdate?.Version };
                case "updates.install":
                    if (vm.IsMigratingData) throw new InvalidOperationException(L10n.T("Storage.AppDataIsBeingCopiedWaitForCompletionBefore"));
                    var release = window._availableUpdate ?? await window._updates.CheckAsync(window.VersionNumber, cancellationToken: token) ?? throw new InvalidOperationException(L10n.T("Update.LatestOrNoRelease"));
                    window._preparedUpdate ??= await window._updates.DownloadAsync(release, vm.Storage.Root, new Progress<double>(), token);
                    token.ThrowIfCancellationRequested(); vm.Save(); using (UpdateInstaller.LaunchHelper(window._preparedUpdate, vm.Storage.Root, vm.Storage.BackupFolder)) { }
                    Dispatcher.UIThread.Post(window.ExitApplication); return null;
                case "network.play":
                    var peer = vm.LanPeers.FirstOrDefault(p => p.DeviceId == Arg(0)) ?? throw new InvalidDataException(L10n.T("Lan.Disconnected"));
                    var remoteTracks = vm.LanPeerTracks(peer.DeviceId).ToArray();
                    if (command.Arguments.Count > 1) { var remoteList = peer.Catalog.Playlists.FirstOrDefault(p => p.Id == Arg(1)) ?? throw new InvalidDataException(L10n.T("Commands.PlaylistNotFound")); remoteTracks = remoteList.TrackIds.Select(id => remoteTracks.FirstOrDefault(t => t.ProviderTrackId == id)).OfType<TrackItem>().ToArray(); }
                    var selected = command.Arguments.Count > 2 ? remoteTracks.FirstOrDefault(t => t.Id == Arg(2) || t.ProviderTrackId == Arg(2)) : remoteTracks.FirstOrDefault();
                    if (selected is null) throw new InvalidDataException(L10n.T("Commands.TrackNotFound"));
                    await vm.PlayTrackAsync(selected, remoteTracks); return Tracks(vm.PlaybackQueue);
                default: throw new InvalidOperationException("Command is not implemented by the desktop host.");
            }
            JsonObject SearchResults() => new() { ["source"] = vm.Page == "cloud-search" ? vm.LastMusicSearchSource : "local", ["query"] = vm.Page == "cloud-search" ? vm.LastMusicSearchQuery : vm.SearchText, ["hasMore"] = vm.Page == "cloud-search" && vm.HasMoreMusicResults, ["items"] = Tracks(vm.VisibleTracks) };
            JsonArray TracksLyrics() => new(vm.LyricLines.Select(l => (JsonNode)new JsonObject { ["seconds"] = l.Seconds, ["text"] = l.Text }).ToArray());
        }
    }
}
