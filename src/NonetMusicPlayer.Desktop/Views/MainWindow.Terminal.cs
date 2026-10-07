using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    public PlayerCommandRouter Commands { get; private set; } = null!;
    public PlayerTerminalSession Terminal { get; private set; } = null!;
    private int _executingCommand;
    private bool _lastLyricsVisible, _lastLyricsLocked;

    private void DetachPlayerCommands()
    {
        if (_vm is null) return;
        Terminal?.Dispose();
        _vm.OperationCompleted -= PlayerOperationCompleted;
        _vm.Plugins.OperationCompleted -= PlayerOperationCompleted;
        PropertyChanged -= WindowCommandStateChanged;
    }

    private void InitializePlayerCommands()
    {
        if (_vm is null) return;
        Commands = new(new DesktopCommandBackend(this));
        Commands.BeforeExecute = async (command, token) => command.Name is "app.exit" or "window.close" || !(await _vm.Plugins.EvaluateHooksAsync("command.before", new() { ["operation"] = command.Name }, token)).Cancel;
        Terminal = new(Commands, GetTerminalOptions());
        _lastLyricsVisible = _vm.Settings.DesktopLyricsVisible; _lastLyricsLocked = _vm.Settings.DesktopLyricsLocked;
        _vm.OperationCompleted += PlayerOperationCompleted;
        _vm.Plugins.OperationCompleted += PlayerOperationCompleted;
        PropertyChanged += WindowCommandStateChanged;
    }

    private TerminalOptions GetTerminalOptions() => new(_vm!.Settings.TerminalScrollbackLines, _vm.Settings.TerminalFontSize, TerminalOptions.ParseLevel(_vm.Settings.TerminalMinimumLogLevel));

    /// <summary>原生窗口按钮、托盘和命令操作统一发布窗口状态变更。</summary>
    private void WindowCommandStateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty) PlayerOperationCompleted(this, CommandResults.Completed(WindowState switch { WindowState.Minimized => "window.minimize", WindowState.Maximized => "window.maximize", _ => "window.restore" }));
        if (e.Property == IsVisibleProperty) PlayerOperationCompleted(this, CommandResults.Completed(IsVisible ? "window.show" : "window.hide"));
    }

    private void PlayerOperationCompleted(object? sender, CommandResult result)
    {
        // 命令执行期间由路由器发布最终结果，避免业务层事件重复输出；鼠标和快捷键动作直接进入同一结果流。
        if (_executingCommand == 0) Commands.Publish(result);
    }

    /// <summary>只记录歌词可见性和锁定状态的变化，置顶检查和逐帧歌词刷新不产生终端输出。</summary>
    private void DesktopLyricsOperationChanged()
    {
        if (_vm is null || Commands is null) return;
        var visible = _vm.Settings.DesktopLyricsVisible; var locked = _vm.Settings.DesktopLyricsLocked;
        if (visible != _lastLyricsVisible) PlayerOperationCompleted(this, CommandResults.Completed("desktop-lyrics.toggle"));
        if (locked != _lastLyricsLocked) PlayerOperationCompleted(this, CommandResults.Completed("desktop-lyrics.lock"));
        _lastLyricsVisible = visible; _lastLyricsLocked = locked;
    }

    private sealed class DesktopCommandBackend(MainWindow window) : IPlayerCommandBackend
    {
        public bool HasWindow => true;
        public async Task<CommandResult> ExecuteAsync(PlayerCommand command, CancellationToken cancellationToken)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return await Dispatcher.UIThread.InvokeAsync(() => ExecuteAsync(command, cancellationToken));
            var vm = window._vm ?? throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlayerNotReady"));
            string Arg(int n) => CommandResults.Required(command, n);
            TrackItem Track(int n = 0) => vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == Arg(n)) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.TrackNotFound"));
            Playlist Playlist(int n = 0) => vm.Playlists.FirstOrDefault(p => p.Id == Arg(n)) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaylistNotFound"));
            PluginManifest Plugin() => vm.Plugins.Installed.FirstOrDefault(p => p.Id == Arg(0)) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PluginNotFound"));
            JsonNode? data = null;
            var errorsBefore = vm.ErrorRevision;
            ++window._executingCommand;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (command.Name)
                {
                    case "status": data = new JsonObject { ["title"] = vm.CurrentTrack?.Title, ["trackId"] = vm.CurrentTrack?.Id, ["playing"] = vm.IsPlaying, ["position"] = vm.PlaybackPosition, ["duration"] = vm.PlaybackDuration, ["volume"] = vm.Volume, ["source"] = vm.PlayingSourcePage }; break;
                    case "player.play":
                        if (command.Arguments.Count == 0) await vm.SetPlayingAsync(true);
                        else if (File.Exists(Arg(0))) await vm.PlayTemporaryFilesAsync([Arg(0)]);
                        else await vm.PlayTrackAsync(Track(), vm.State.Tracks);
                        if (!vm.IsPlaying) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaybackStartFailed")); break;
                    case "player.pause": await vm.SetPlayingAsync(false); break;
                    case "player.resume": await vm.SetPlayingAsync(true); break;
                    case "player.next": await vm.PlayRelativeAsync(1); break;
                    case "player.previous": await vm.PlayRelativeAsync(-1); break;
                    case "player.seek": vm.Seek(CommandResults.Number(command, 0, 0, vm.PlaybackDuration)); break;
                    case "player.volume": vm.Volume = CommandResults.Number(command, 0, 0, 100); vm.Save(); break;
                    case "player.mute": vm.ToggleMuteCommand.Execute(null); break;
                    case "player.mode": vm.SetPlayMode(Arg(0) switch { "repeat-all" => PlayMode.RepeatAll, "repeat-one" => PlayMode.RepeatOne, "shuffle" => PlayMode.Shuffle, _ => throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownMode")) }); break;
                    case "player.devices": data = Strings(vm.AudioDevices); break;
                    case "player.device": if (!vm.AudioDevices.Contains(Arg(0))) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.OutputNotFound")); await vm.SwitchOutputAsync(Arg(0)); break;
                    case "queue.list": data = Tracks(vm.PlaybackQueue); break;
                    case "queue.play": await vm.PlayPlaylistAsync(Playlist(), command.Arguments.Count > 1 ? Arg(1) : null); break;
                    case "music.list": data = Tracks(vm.State.Tracks.Where(t => command.Arguments.Count == 0 || (t.Title + " " + t.Artist + " " + t.Album).Contains(Arg(0), StringComparison.CurrentCultureIgnoreCase))); break;
                    case "music.info": data = TrackJson(Track()); break;
                    case "music.remove": vm.RemoveLibraryTracks(command.Arguments.Select(id => vm.State.Tracks.FirstOrDefault(t => t.Id == id) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.TrackNotFound")))); break;
                    case "playlist.list": data = new JsonArray(vm.Playlists.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.IsSystem ? L10n.T("Playlists.LikedSongs") : p.Name, ["description"] = p.Description, ["count"] = p.TrackIds.Count }).ToArray()); break;
                    case "playlist.create": data = new JsonObject { ["id"] = vm.CreatePlaylist(Arg(0)).Id }; break;
                    case "playlist.rename": if (Playlist().IsSystem) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.LikedRenameDenied")); vm.RenamePlaylist(Playlist(), Arg(1)); break;
                    case "playlist.describe": var described = Playlist(); vm.UpdatePlaylistDetails(described, described.Name, Arg(1), described.CoverPath); break;
                    case "playlist.delete": if (Playlist().IsSystem) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.LikedDeleteDenied")); vm.DeletePlaylist(Playlist()); break;
                    case "playlist.add":
                        var target = Playlist(); var input = command.Arguments.Skip(1).ToArray(); if (input.Length == 0) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ProvideInputs"));
                        var known = input.Where(id => vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).Any(t => t.Id == id)).ToArray();
                        var paths = input.Except(known).ToArray();
                        if (paths.Length > 0 && paths.Any(path => !File.Exists(path) && !Directory.Exists(path))) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.InputMissing"));
                        if (paths.Length > 0) await vm.ImportAsync(paths, playlist: target);
                        if (known.Length > 0) vm.AddToPlaylist(target, known.Select(id => vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).First(t => t.Id == id))); break;
                    case "playlist.remove": vm.RemovePlaylistTracks(Playlist(), command.Arguments.Skip(1).Select(id => vm.State.Tracks.First(t => t.Id == id))); break;
                    case "playlist.move":
                        var ordered = Playlist(); var movingId = Arg(1); var old = ordered.TrackIds.IndexOf(movingId); if (old < 0) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.NotInPlaylist"));
                        var position = CommandResults.Integer(command, 2, 1, ordered.TrackIds.Count) - 1;
                        vm.ReorderPlaylistTracks(ordered, [movingId], ordered.TrackIds[position], old > position); break;
                    case "playlist.reorder": var list = Playlist(); var index = CommandResults.Integer(command, 1, 1, vm.Playlists.Count) - 1; var at = vm.Playlists[index]; vm.ReorderPlaylists(list.Id, at.Id, vm.Playlists.IndexOf(list) > index); break;
                    case "playlist.cover": vm.SetPlaylistCover(Playlist(), Arg(1) == "default" ? null : Arg(1)); break;
                    case "favorite.add": if (!Track().IsFavorite) vm.ToggleFavorite(Track()); break;
                    case "favorite.remove": if (Track().IsFavorite) vm.ToggleFavorite(Track()); break;
                    case "history.list": data = Strings(vm.State.History); break;
                    case "history.clear": vm.ClearHistory(); break;
                    case "history.remove": vm.RemoveRecentTracks(command.Arguments); break;
                    case "statistics":
                        var stats = vm.GetStatistics(command.Arguments.Count == 0 ? null : DateOnly.ParseExact(Arg(0), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), command.Arguments.Count > 1 ? Arg(1) : "all");
                        data = JsonSerializer.SerializeToNode(stats, AppStorage.Json); break;
                    case "settings.list": data = JsonSerializer.SerializeToNode(vm.Settings, AppStorage.Json); break;
                    case "settings.get": var settings = JsonSerializer.SerializeToNode(vm.Settings, AppStorage.Json)!.AsObject(); var key = settings.Select(p => p.Key).FirstOrDefault(k => k.Equals(Arg(0), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownSetting")); data = settings[key]?.DeepClone(); break;
                    case "settings.set":
                        var updated = JsonSerializer.SerializeToNode(vm.Settings, AppStorage.Json)!.AsObject(); var setting = updated.Select(p => p.Key).FirstOrDefault(k => k.Equals(Arg(0), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownSetting"));
                        if (vm.IsLyricsTimingActive && setting.Equals("playMode", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L10n.T("LyricsTiming.PlaybackRestricted"));
                        var value = CommandResults.ParseSettingValue(Arg(1)); SettingsContract.Validate(setting, value); updated[setting] = value;
                        var replacement = updated.Deserialize<AppSettings>(AppStorage.Json) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.InvalidSettings"));
                        replacement.Validate(); vm.State.Settings = replacement; vm.Volume = replacement.Volume; vm.ApplySettings(); break;
                    case "settings.reset": vm.ResetSettings(); break;
                    case "data.backup": if (File.Exists(Arg(0))) throw new IOException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.BackupExists")); vm.Save(); File.Copy(Path.Combine(vm.Storage.Root, "state.json"), Path.GetFullPath(Arg(0)), false); break;
                    case "data.restore": vm.RestoreLibraryBackup(Arg(0)); break;
                    case "lyrics.show": data = new JsonArray(vm.LyricLines.Select(l => (JsonNode)new JsonObject { ["seconds"] = l.Seconds, ["text"] = l.Text }).ToArray()); break;
                    case "lyrics.import": vm.ImportLyrics(Arg(0)); break;
                    case "lyrics.path": data = JsonValue.Create(vm.CurrentTrack is { } current ? vm.Lyrics.ExistingPath(current.Id) : null); break;
                    case "plugins.list": data = new JsonArray(vm.Plugins.Installed.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["version"] = p.Version, ["type"] = p.Type, ["enabled"] = p.Enabled }).ToArray()); break;
                    case "plugins.install":
                        if (Uri.TryCreate(Arg(0), UriKind.Absolute, out var url) && url.Scheme == "https") await window.ImportRemotePluginAsync(Arg(0), cancellationToken);
                        else await vm.Plugins.InstallAsync(Arg(0)); vm.ApplySettings(); window.RefreshPluginNavigation(); window.ShowPage(); break;
                    case "plugins.enable": vm.Plugins.SetEnabled(Plugin(), true); vm.ApplySettings(); break;
                    case "plugins.disable": vm.DisablePlugin(Plugin()); break;
                    case "plugins.uninstall": if (command.Arguments.Count > 1 && Arg(1) is not ("keep-files" or "delete-files")) throw new InvalidDataException("keep-files | delete-files"); vm.DisablePlugin(Plugin()); vm.Plugins.Uninstall(Plugin(), command.Arguments.Count > 1 && Arg(1) == "delete-files"); window.RefreshPluginNavigation(); break;
                    case "plugins.config": if (command.Arguments.Count > 1) vm.Plugins.Configure(Plugin(), Arg(1)); else data = JsonNode.Parse(Plugin().Configuration); break;
                    case "plugins.schema": data = vm.Plugins.ReadConfigurationSchema(Plugin()); break;
                    case "plugins.refresh": await vm.RefreshProviderAsync(Plugin()); break;
                    case "layout.show": data = JsonNode.Parse(window.LayoutConfiguration.AppliedJson); break;
                    case "layout.apply": if (new FileInfo(Arg(0)).Length > UiLayoutService.MaximumBytes) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.LayoutTooLarge")); window.ApplyLayoutConfiguration(File.ReadAllText(Arg(0))); break;
                    case "layout.reset": window.RestoreDefaultLayout(); break;
                    case "window.show": window.RestoreFromTray(); break;
                    case "window.hide": window.Hide(); break;
                    case "window.minimize": window.WindowState = WindowState.Minimized; break;
                    case "window.maximize": window.WindowState = WindowState.Maximized; break;
                    case "window.restore": window.WindowState = WindowState.Normal; break;
                    case "window.close": window.Close(); break;
                    case "desktop-lyrics.toggle": window._desktopLyrics?.Toggle(); break;
                    case "desktop-lyrics.lock": if (bool.Parse(Arg(0))) window._desktopLyrics?.LockLyrics(); else window._desktopLyrics?.UnlockLyrics(); break;
                    case "album.cover": case "artist.cover": var type = command.Name.Split('.')[0]; if (Arg(1) == "default") vm.SetGroupSoftwareDefaultCover(type, Arg(0)); else vm.SetGroupCover(type, Arg(0), Arg(1) == "auto" ? null : Arg(1)); break;
                    case "navigate": vm.Navigate(Arg(0)); break;
                    case "undo": vm.UndoCommand.Execute(null); break;
                    case "music.search": vm.Navigate("songs"); vm.SearchText = Arg(0); break;
                    case "import.cancel": vm.CancelImport(); break;
                    case "playlist.import": await vm.ImportM3uAsync(Arg(0)); break;
                    case "playlist.export": if (File.Exists(Arg(0))) throw new IOException(L10n.T("Commands.BackupExists")); vm.ExportM3u(Arg(0)); break;
                    case "data.directory": await vm.ConfigureDataDirectoryAsync(Arg(0)); break;
                    case "help.open": window.OpenUserManual(); break;
                    case "selection.begin": window.SetBatchMode(true); break;
                    case "selection.end": window.SetBatchMode(false); break;
                    case "selection.all": window.SetBatchMode(true); window.TracksList.SelectAll(); break;
                    case "selection.clear": window.TracksList.SelectedItems?.Clear(); break;
                    case "selection.select":
                        var selected = command.Arguments.Select(id => vm.VisibleTracks.FirstOrDefault(t => t.Id == id) ?? throw new InvalidDataException(L10n.T("Commands.TrackNotFound"))).ToArray();
                        window.SetBatchMode(true); window.TracksList.SelectedItems?.Clear(); foreach (var item in selected) window.TracksList.SelectedItems?.Add(item); break;
                    case "selection.list": data = Tracks(window.SelectedTracks); break;
                    case "app.exit": Dispatcher.UIThread.Post(window.ExitApplication); break;
                    case "terminal.exit": vm.Navigate("library"); break;
                    default: throw new InvalidOperationException("Command is not implemented by the desktop host.");
                }
                return vm.ErrorRevision == errorsBefore ? CommandResults.Completed(command.Name, data) : CommandResults.Failed(command.Name, vm.StatusText);
            }
            finally { --window._executingCommand; }
        }
    }

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
    private static JsonObject TrackJson(TrackItem t) => new() { ["id"] = t.Id, ["title"] = t.Title, ["artist"] = t.Artist, ["album"] = t.Album, ["path"] = t.FilePath, ["duration"] = t.DurationSeconds, ["favorite"] = t.IsFavorite, ["coverPath"] = t.CoverPath };
    private static JsonArray Tracks(IEnumerable<TrackItem> tracks) => new(tracks.Select(t => (JsonNode)TrackJson(t)).ToArray());

    private async Task ImportRemotePluginAsync(string link, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var notification = ShowPluginDownload(cancellation.Cancel);
        using var downloader = new PluginReleaseDownloader();
        var assets = await downloader.ResolveAsync(link, cancellation.Token); var selected = assets[0];
        if (assets.Count > 1)
        {
            var name = await PlayerDialog.Choose(this, L10n.T("Plugins.ChoosePluginAsset"), L10n.T("Plugins.ChooseAnImppPackageForThisOperatingSystem"), assets.Select(a => a.Name).ToArray());
            if (name is null) throw new OperationCanceledException(); selected = assets.First(a => a.Name == name);
        }
        notification.Downloading(selected.Name);
        using var package = await downloader.DownloadAsync(selected, _vm!.Storage.PluginsFolder, notification, cancellation.Token);
        await _vm.Plugins.InstallAsync(package.Path); notification.Complete(selected.Name);
    }
}
