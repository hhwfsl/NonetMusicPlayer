using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Playback;
using NonetMusicPlayer.Core.Plugins;

using NonetMusicPlayer.Core.Audio;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    public async Task<CommandResult> ExecuteAsync(PlayerCommand command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); AccountListening();
            string Arg(int n) => CommandResults.Required(command, n);
            JsonNode? data = null; var save = true;
            switch (command.Name)
            {
                case "status": data = new JsonObject { ["title"] = Current?.Metadata.Title, ["trackId"] = Current?.Id, ["playing"] = _loaded && _audio.IsPlaying, ["position"] = _loaded ? _audio.Position.TotalSeconds : State.LastPosition, ["duration"] = _loaded ? _audio.Duration.TotalSeconds : Current?.Metadata.DurationSeconds ?? 0, ["volume"] = State.Settings.Volume, ["source"] = State.LastPlaylistId, ["dataDirectory"] = DataDirectory }; save = false; break;
                case "app.exit": case "terminal.exit": ExitRequested = true; break;
                case "player.play":
                    if (command.Arguments.Count == 0) await ResumeAsync(cancellationToken);
                    else
                    {
                        StoredMusicTrack track;
                        if (File.Exists(Arg(0))) track = new() { Metadata = new MusicFileReader(ArtworkDirectory, LyricsDirectory).Read(Arg(0)) };
                        else track = FindTrack(Arg(0));
                        State.LastPlaylistId = null; _queue = [track]; _failed.Clear(); await StartAsync(track, 0, cancellationToken);
                        if (!_loaded || !_audio.IsPlaying) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaybackStartFailed"));
                    }
                    break;
                case "player.pause": _audio.Pause(); _listeningAnchor = System.Diagnostics.Stopwatch.GetTimestamp(); break;
                case "player.resume": await ResumeAsync(cancellationToken); break;
                case "player.next": await RelativeAsync(1, false, cancellationToken); break;
                case "player.previous": await RelativeAsync(-1, false, cancellationToken); break;
                case "player.seek": var duration = _loaded ? _audio.Duration.TotalSeconds : Current?.Metadata.DurationSeconds ?? 0; var seconds = CommandResults.Number(command, 0, 0, duration); if (_loaded) _audio.Position = TimeSpan.FromSeconds(seconds); State.LastPosition = seconds; break;
                case "player.volume": State.Settings.Volume = CommandResults.Number(command, 0, 0, 100); _audio.Volume = (float)(State.Settings.Volume / 100); break;
                case "player.mute": State.Settings.Volume = State.Settings.Volume > 0 ? 0 : 80; _audio.Volume = (float)(State.Settings.Volume / 100); break;
                case "player.mode": State.Settings.PlayMode = Arg(0) switch { "repeat-all" => PlaybackMode.RepeatAll, "repeat-one" => PlaybackMode.RepeatOne, "shuffle" => PlaybackMode.Shuffle, _ => throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownMode")) }; break;
                case "player.devices":
                    if (_audio is NativeAudioPlayer nativeAudio) nativeAudio.RefreshDevices();
                    data = Strings(_audio.Devices); save = false; break;
                case "player.device": if (!_audio.Devices.Contains(Arg(0))) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.OutputNotFound")); _audio.Pause(); _audio.DeviceName = Arg(0); State.Settings.DeviceName = Arg(0); break;
                case "queue.list": data = TracksJson(_queue); save = false; break;
                case "queue.play": var playlist = FindPlaylist(Arg(0)); var queue = PlaylistTracks(playlist).ToList(); if (queue.Count == 0) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.EmptyPlaylist")); var selected = command.Arguments.Count > 1 ? queue.FirstOrDefault(t => t.Id == Arg(1)) ?? throw new InvalidDataException(LocalizationCatalog.Get("Commands.NotInPlaylist")) : queue[0]; State.LastPlaylistId = playlist.Id; _queue = queue; _failed.Clear(); await StartAsync(selected, 0, cancellationToken); if (!_loaded || !_audio.IsPlaying) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaybackStartFailed")); break;
                case "music.list": data = TracksJson(State.Tracks.Where(t => command.Arguments.Count == 0 || (t.Metadata.Title + " " + t.Metadata.Artist + " " + t.Metadata.Album).Contains(Arg(0), StringComparison.CurrentCultureIgnoreCase))); save = false; break;
                case "music.info": data = TrackJson(FindTrack(Arg(0))); save = false; break;
                case "music.remove":
                    var removing = command.Arguments.ToHashSet(StringComparer.Ordinal); if (removing.Count == 0) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ProvideIds")); foreach (var id in removing) FindTrack(id);
                    State.Tracks.RemoveAll(t => removing.Contains(t.Id)); foreach (var list in State.Playlists) list.TrackIds.RemoveAll(removing.Contains);
                    _queue.RemoveAll(t => removing.Contains(t.Id)); if (Current is not null && removing.Contains(Current.Id)) { _audio.Stop(); _loaded = false; Current = null; State.LastPosition = 0; } break;
                case "playlist.list": data = new JsonArray(State.Playlists.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Id == "liked" ? LocalizationCatalog.Get("Playlists.LikedSongs") : p.Name, ["description"] = p.Description, ["count"] = p.TrackIds.Count, ["coverPath"] = p.CoverPath }).ToArray()); save = false; break;
                case "playlist.create": var created = new StoredPlaylist { Name = ValidName(Arg(0)) }; State.Playlists.Add(created); data = new JsonObject { ["id"] = created.Id }; break;
                case "playlist.rename": var renamed = FindPlaylist(Arg(0)); if (renamed.Id == "liked") throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.LikedRenameDenied")); renamed.Name = ValidName(Arg(1)); break;
                case "playlist.describe": var description = Arg(1).Trim(); if (description.Length > 500) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.DescriptionTooLong")); FindPlaylist(Arg(0)).Description = description; break;
                case "playlist.delete": var deleted = FindPlaylist(Arg(0)); if (deleted.Id == "liked") throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.LikedDeleteDenied")); State.Playlists.Remove(deleted); if (State.LastPlaylistId == deleted.Id) State.LastPlaylistId = null; break;
                case "playlist.add":
                    var target = FindPlaylist(Arg(0)); var inputs = command.Arguments.Skip(1).ToArray(); if (inputs.Length == 0) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ProvideInputs"));
                    var found = inputs.Where(id => State.Tracks.Concat(State.RecentTemporaryTracks).Any(t => t.Id == id)).Select(FindTrack).ToArray();
                    var files = inputs.Where(id => !found.Any(t => t.Id == id)).ToArray();
                    var imported = await ReadPathsAsync(files, cancellationToken); AddToPlaylist(target, found.Concat(imported)); break;
                case "playlist.remove": var listToChange = FindPlaylist(Arg(0)); var ids = command.Arguments.Skip(1).ToHashSet(StringComparer.Ordinal); if (ids.Count == 0) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ProvideIds")); listToChange.TrackIds.RemoveAll(ids.Contains); RefreshQueue(); break;
                case "playlist.move": var ordered = FindPlaylist(Arg(0)); var moving = Arg(1); if (!ordered.TrackIds.Contains(moving)) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.NotInPlaylist")); var index = CommandResults.Integer(command, 2, 1, ordered.TrackIds.Count) - 1; ordered.TrackIds.Remove(moving); ordered.TrackIds.Insert(index, moving); RefreshQueue(); break;
                case "playlist.reorder": var reordered = FindPlaylist(Arg(0)); var at = CommandResults.Integer(command, 1, 1, State.Playlists.Count) - 1; State.Playlists.Remove(reordered); State.Playlists.Insert(at, reordered); break;
                case "playlist.cover": FindPlaylist(Arg(0)).CoverPath = Arg(1) == "default" ? null : StoreCover(Arg(1)); break;
                case "favorite.add": AddToPlaylist(FindPlaylist("liked"), [FindTrack(Arg(0))]); break;
                case "favorite.remove": FindPlaylist("liked").TrackIds.Remove(FindTrack(Arg(0)).Id); RefreshQueue(); break;
                case "history.list": data = Strings(State.History); save = false; break;
                case "history.remove": var historyIds = command.Arguments.ToHashSet(StringComparer.Ordinal); State.History.RemoveAll(historyIds.Contains); State.RecentTemporaryTracks.RemoveAll(t => historyIds.Contains(t.Id)); break;
                case "history.clear": State.History.Clear(); State.RecentTemporaryTracks.RemoveAll(t => t.Id != Current?.Id); break;
                case "statistics": data = Statistics(command.Arguments.Count == 0 ? null : DateOnly.ParseExact(Arg(0), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), command.Arguments.Count > 1 ? Arg(1) : "all"); save = false; break;
                case "settings.list": data = JsonSerializer.SerializeToNode(State.Settings, CoreJson.Options); save = false; break;
                case "settings.get": var preferences = JsonSerializer.SerializeToNode(State.Settings, CoreJson.Options)!.AsObject(); var property = preferences.Select(p => p.Key).FirstOrDefault(k => k.Equals(Arg(0), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownSetting")); data = preferences[property]?.DeepClone(); save = false; break;
                case "settings.set":
                    var changed = JsonSerializer.SerializeToNode(State.Settings, CoreJson.Options)!.AsObject(); var setting = changed.Select(p => p.Key).FirstOrDefault(k => k.Equals(Arg(0), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnknownSetting"));
                    var settingValue = CommandResults.ParseSettingValue(Arg(1)); SettingsContract.Validate(setting, settingValue); changed[setting] = settingValue; var replacement = changed.Deserialize<HeadlessPlayerSettings>(CoreJson.Options) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.InvalidSettings")); replacement.Validate(); ApplySettings(replacement); break;
                case "settings.reset": ApplySettings(new HeadlessPlayerSettings { LyricsFolder = State.Settings.LyricsFolder, BackupFolder = State.Settings.BackupFolder }); break;
                case "data.backup": Save(); if (File.Exists(Arg(0))) throw new IOException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.BackupExists")); File.Copy(StatePath, Path.GetFullPath(Arg(0)), false); break;
                case "data.restore":
                    var restored = ParseState(Arg(0)); Save(); File.Copy(StatePath, StatePath + ".before-restore-" + DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmssfff"), false);
                    _audio.Stop(); _loaded = false; State = restored; Current = State.Tracks.Concat(State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == State.LastTrackId);
                    // 恢复后的播放队列仍使用来源歌单，不把多首歌的队列退化为单首循环。
                    _queue = State.LastPlaylistId is { } restoredId && State.Playlists.FirstOrDefault(p => p.Id == restoredId) is { } restoredPlaylist ? PlaylistTracks(restoredPlaylist).ToList() : Current is null ? [] : [Current];
                    ApplySettings(State.Settings); break;
                case "lyrics.show": var lyric = LyricPath(); data = lyric is null ? JsonValue.Create("") : JsonValue.Create(File.ReadAllText(lyric)); save = false; break;
                case "lyrics.path": data = JsonValue.Create(LyricPath()); save = false; break;
                case "lyrics.import": if (Current is null) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.SelectTrack")); var file = Arg(0); if (Path.GetExtension(file).ToLowerInvariant() is not (".lrc" or ".txt") || new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.LyricsSize")); Directory.CreateDirectory(LyricsDirectory); File.Copy(file, Path.Combine(LyricsDirectory, LyricFileId(Current) + Path.GetExtension(file).ToLowerInvariant()), true); break;
                case "plugins.list": data = new JsonArray(Plugins.Installed.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["type"] = p.Type, ["version"] = p.Version, ["enabled"] = p.Enabled }).ToArray()); save = false; break;
                case "plugins.install":
                    if (Uri.TryCreate(Arg(0), UriKind.Absolute, out var url) && url.Scheme == "https")
                    {
                        using var downloader = new PluginReleaseDownloader(); var assets = await downloader.ResolveAsync(Arg(0), cancellationToken);
                        if (assets.Count > 1) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.MultiplePackages"));
                        using var package = await downloader.DownloadAsync(assets[0], Path.Combine(DataDirectory, "Plugins"), new Progress<double>(percent => PluginDownloadProgress?.Invoke(this, percent)), cancellationToken);
                        Plugins.Install(package.Path);
                    }
                    else Plugins.Install(Arg(0)); break;
                case "plugins.enable": Plugins.SetEnabled(Plugins.Find(Arg(0)), true); break;
                case "plugins.disable": DisableProvider(Arg(0)); break;
                case "plugins.uninstall": if (command.Arguments.Count > 1 && Arg(1) is not ("keep-files" or "delete-files")) throw new InvalidDataException("keep-files | delete-files"); DisableProvider(Arg(0)); Plugins.Uninstall(Plugins.Find(Arg(0)), command.Arguments.Count > 1 && Arg(1) == "delete-files"); break;
                case "plugins.config": var plugin = Plugins.Find(Arg(0)); if (command.Arguments.Count > 1) Plugins.Configure(plugin, Arg(1)); else { data = JsonNode.Parse(plugin.Configuration); save = false; } break;
                case "plugins.schema": data = Plugins.Schema(Plugins.Find(Arg(0))); save = false; break;
                case "plugins.refresh": await RefreshProviderAsync(Arg(0), cancellationToken); break;
                default: throw new PlatformNotSupportedException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.HostUnsupported"));
            }
            if (save) Save(); return CommandResults.Completed(command.Name, data);
        }
        finally { _gate.Release(); }
    }
    private async Task ResumeAsync(CancellationToken token)
    {
        if (_loaded) { _audio.Play(); _listeningAnchor = System.Diagnostics.Stopwatch.GetTimestamp(); return; }
        if (Current is null) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.SelectTrack"));
        _failed.Clear(); await StartAsync(Current, State.LastPosition, token, false);
        if (!_loaded || !_audio.IsPlaying) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PlaybackResumeFailed"));
    }
    private void ApplySettings(HeadlessPlayerSettings settings)
    {
        settings.Validate(); Directory.CreateDirectory(string.IsNullOrWhiteSpace(settings.LyricsFolder) ? Path.Combine(DataDirectory, "Lyrics") : Path.GetFullPath(settings.LyricsFolder));
        if (_audio.DeviceName != settings.DeviceName) { _audio.Pause(); _audio.DeviceName = settings.DeviceName; }
        _audio.Volume = (float)(settings.Volume / 100); State.Settings = settings; LocalizationCatalog.SetLanguage(settings.Language); TrimHistory();
    }
    private void DisableProvider(string id)
    {
        if (Current?.ProviderId == id) { _audio.Pause(); _loaded = false; }
        Plugins.SetEnabled(Plugins.Find(id), false); _queue.RemoveAll(t => t.ProviderId == id);
    }
    private async Task RefreshProviderAsync(string id, CancellationToken token)
    {
        var provider = Plugins.Find(id); var tracks = await Plugins.CatalogAsync(provider, token); var parsed = new List<StoredMusicTrack>();
        foreach (var track in tracks)
        {
            var key = id + ":" + track.Id; string? cover = null;
            if (track.CoverBase64 is { Length: <= 2_000_000 } image) { cover = Path.Combine(ArtworkDirectory, MusicFileReader.StableId(key) + ".image"); File.WriteAllBytes(cover, Convert.FromBase64String(image)); }
            if (track.Lyrics is { Length: <= 100_000 } lyrics) { Directory.CreateDirectory(LyricsDirectory); AtomicFile.Write(Path.Combine(LyricsDirectory, MusicFileReader.StableId(key) + ".lrc"), lyrics); }
            parsed.Add(new() { ProviderId = id, ProviderTrackId = track.Id, Metadata = new MusicMetadata(key, track.Title, track.Artist, track.Album, "", "." + track.Format, 0, track.DurationSeconds, cover) });
        }
        State.Tracks.RemoveAll(t => t.ProviderId == id); State.Tracks.InsertRange(0, parsed); RefreshQueue();
    }
}
