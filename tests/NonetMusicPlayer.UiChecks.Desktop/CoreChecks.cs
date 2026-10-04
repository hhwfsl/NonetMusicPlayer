using System.Reflection;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

internal static class CoreChecks
{
    public static void Run(string output)
    {
        var storage = new AppStorage(Path.Combine(output, "core-checks-" + Guid.NewGuid().ToString("N")));
        var a = Track("a", "A", true); var b = Track("b", "B"); var c = Track("c", "C"); var d = Track("d", "D");
        var legacy = new AppState { Tracks = [a, b, c, d], LastTrackId = "a", LastPosition = 3.75, LastSourcePage = "favorites", Settings = new AppSettings { PlayMode = (PlayMode)0, Volume = 42 } };
        storage.Save(legacy);
        var audio = new CoreAudio();
        using (var vm = new MainViewModel(new MusicLibraryScanner(storage), audio))
        {
            var notifications = new List<UserNotificationEventArgs>(); var locate = 0;
            vm.UserNotification += (_, args) => notifications.Add(args); vm.LocatePlayingTrack += (_, _) => locate++;
            Require(vm.LikedPlaylist.Id == "liked" && vm.LikedPlaylist.IsSystem && vm.LikedPlaylist.TrackIds.SequenceEqual(["a"]), "Legacy favorites migrate to permanent liked playlist");
            Require(vm.Settings.PlayMode == PlayMode.RepeatAll && Enum.GetValues<PlayMode>().Length == 3, "Old numeric mode zero migrates; only three modes remain");
            Require(vm.Page == "playlist:liked" && vm.PlayingPlaylistId == "liked" && vm.CurrentTrack?.Id == "a", "Restore selected song and source playlist");
            Require(audio.LoadCount == 0 && audio.PlayCount == 0 && !vm.IsPlaying, "Launch never autoplays");
            Require(Math.Abs(vm.PlaybackPosition - 3.75) < .001 && vm.Volume == 42 && Math.Abs(audio.Volume - .42f) < .001, "Restore position and volume for display");
            Tick(vm); vm.Save(); Require(Math.Abs(storage.Load().LastPosition - 3.75) < .001, "Unloaded timer cannot overwrite saved position with zero");
            var positionWrites = audio.PositionWrites; vm.Seek(7); Require(audio.PositionWrites == positionWrites && vm.PlaybackPosition == 7, "Unloaded Seek changes pending restore only");
            vm.TogglePlayPauseCommand.Execute(null); Require(audio.LoadCount == 1 && audio.PlayCount == 1 && audio.Position.TotalSeconds == 7, "First play resumes stored position");
            vm.Seek(7); vm.Seek(7); Require(audio.PositionWrites == positionWrites + 3, "Seek always commits even when display value is unchanged");
            vm.TogglePlayPauseCommand.Execute(null); Require(!vm.IsPlaying && vm.VisibleTracks.Single().IsPlayingHere, "Paused current song retains source highlight");
            var original = vm.State.Tracks;
            var all = vm.CreatePlaylist("Fixture references"); vm.AddToPlaylist(all, original);
            a = original.Single(t => t.Id == "a"); b = original.Single(t => t.Id == "b"); c = original.Single(t => t.Id == "c"); d = original.Single(t => t.Id == "d");
            vm.DeletePlaylist(vm.LikedPlaylist); vm.RenamePlaylist(vm.LikedPlaylist, "不允许");
            Require(vm.State.Playlists.Contains(vm.LikedPlaylist) && vm.LikedPlaylist.Name == "我喜欢", "System playlist cannot be deleted or renamed");
            vm.UpdatePlaylistDetails(vm.LikedPlaylist, "仍不允许", "自己的歌单简介", null);
            Require(vm.LikedPlaylist.Name == "我喜欢" && vm.LikedPlaylist.Description == "自己的歌单简介", "System playlist description remains customizable");
            vm.AddToPlaylist(vm.LikedPlaylist, [b]); Require(b.IsFavorite && vm.LikedPlaylist.TrackIds[0] == "b", "Playlist membership updates favorite flag and inserts at head");
            c.IsFavorite = true; Require(vm.LikedPlaylist.TrackIds[0] == "c", "Favorite flag directly synchronizes playlist membership");
            vm.Navigate("favorites"); Require(vm.Page == "playlist:liked", "Legacy favorites route stays compatible");
            vm.MoveInPlaylist(a, -1); var likedOrder = vm.LikedPlaylist.TrackIds.ToArray(); vm.ToggleFavorite(a); vm.UndoCommand.Execute(null);
            Require(a.IsFavorite && vm.LikedPlaylist.TrackIds.SequenceEqual(likedOrder), "Favorite undo retains manual liked ordering");
            vm.RemoveTracks([b]); Require(!b.IsFavorite && vm.State.Tracks.Contains(b), "Remove from liked clears flag without deleting library item"); vm.UndoCommand.Execute(null); Require(b.IsFavorite, "Undo liked removal restores flag");
            var p = vm.CreatePlaylist("手动顺序"); vm.AddToPlaylist(p, [a, b, c]); vm.AddToPlaylist(p, [d]);
            Require(p.TrackIds.SequenceEqual(["d", "a", "b", "c"]), "New playlist songs insert at head, preserving batch order");
            vm.Navigate("playlist:" + p.Id); vm.MoveInPlaylist(b, -1); Require(p.TrackIds.SequenceEqual(["d", "b", "a", "c"]), "Manual order persisted");
            var beforeSort = p.TrackIds.ToArray(); vm.Sort("title"); Require(p.TrackIds.SequenceEqual(beforeSort) && vm.VisibleTracks.Select(t => t.Id).SequenceEqual(beforeSort), "Playlist sort does nothing");
            vm.SearchText = "A"; Require(vm.VisibleTracks.Count == 1 && a.DisplayIndex == 3, "Search retains original full-playlist song ordinal"); Wait(vm.PlayTrackAsync(a)); Require(audio.Position.TotalSeconds == 0, "Fresh track explicitly seeks zero even if backend Load left a stale position"); vm.PlayNextCommand.Execute(null);
            Require(vm.CurrentTrack?.Id == "c" && vm.PlayingPlaylistId == p.Id, "Normal play uses complete playlist rather than search-filtered queue");
            var other = vm.CreatePlaylist("相同歌曲另一个歌单"); vm.AddToPlaylist(other, [a, c]); vm.Navigate("playlist:" + other.Id);
            Require(vm.VisibleTracks.All(t => !t.IsPlayingHere) && vm.PlayingPlaylistId == p.Id, "Same song is not highlighted in unrelated source playlist");
            vm.PlayPreviousCommand.Execute(null); Require(vm.CurrentTrack?.Id == "a" && vm.PlayingPlaylistId == p.Id, "Navigation never changes playing source during next/previous");
            vm.Navigate("playlist:" + p.Id); Require(a.IsPlayingHere && a.DisplayIndex == 3 && locate >= 5, "Return to source highlights and requests scrolling to current song");
            audio.Complete(); vm.PlayNextCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Require(vm.CurrentTrack?.Id == "c" && vm.IsPlaying, "Queued stale completion cannot skip the newly selected track");
            audio.Fail(); vm.PlayPreviousCommand.Execute(null); Dispatcher.UIThread.RunJobs(); Require(vm.CurrentTrack?.Id == "a" && vm.IsPlaying, "Queued stale failure cannot stop a newer playback request");
            Wait(vm.PlayTrackAsync(a, [a, c])); vm.ToggleFavorite(b); vm.ToggleFavorite(b); vm.PlayNextCommand.Execute(null); Require(vm.CurrentTrack?.Id == "c", "Explicit filtered queue remains scoped across unrelated favorite updates");
            Wait(vm.PlayTrackAsync(c)); vm.SetPlayMode(PlayMode.RepeatAll); audio.Complete(); Dispatcher.UIThread.RunJobs();
            Require(vm.CurrentTrack?.Id == "d", "End of playlist wraps to first song");
            vm.SetPlayMode(PlayMode.RepeatOne); audio.Complete(); Dispatcher.UIThread.RunJobs(); Require(vm.CurrentTrack?.Id == "d", "Repeat-one completion replays current song");
            vm.CycleModeCommand.Execute(null); Require(vm.Settings.PlayMode == PlayMode.Shuffle, "Cycle to shuffle"); vm.CycleModeCommand.Execute(null); Require(vm.Settings.PlayMode == PlayMode.RepeatAll, "Cycle wraps through only three modes");
            vm.Seek(12); audio.ThrowPosition = true; Tick(vm); audio.ThrowPosition = false;
            Require(notifications.Any(n => !n.IsWarning) && !vm.IsPlaying && vm.PlaybackPosition == 12, "Timer backend exception is caught, notified and retains resume progress");
            audio.Fail(); Dispatcher.UIThread.RunJobs(); Require(vm.StatusText.Contains("音频播放失败"), "Runtime audio failure reaches unified notification safely");
            vm.ReportError("测试连接失败", new InvalidOperationException("https://example.test/song?token=private-token Authorization: Bearer secret-token"));
            Require(!vm.StatusText.Contains("private-token") && !vm.StatusText.Contains("secret-token"), "User-facing failure messages redact provider secrets");
            var cover = Path.Combine(output, "core-cover.png");
            using (var image = new WriteableBitmap(new Avalonia.PixelSize(8, 8), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul)) image.Save(cover, PngBitmapEncoderOptions.Default);
            vm.SetPlaylistCover(p, cover); Require(p.CoverPath?.StartsWith(storage.ArtworkFolder, StringComparison.Ordinal) == true && p.Artwork is not null, "Playlist cover copied into managed artwork storage");
            File.Delete(cover); Require(File.Exists(p.CoverPath), "Removing external image cannot break copied playlist artwork");
            vm.Save(); var saved = JsonSerializer.Serialize(storage.Load(), AppStorage.Json);
            Require(!saved.Contains("displayIndex") && !saved.Contains("isPlayingHere") && !saved.Contains("isSystem"), "Display-only properties never pollute persisted JSON");
            Require(saved.Contains("isFavorite") && saved.Contains("description") && saved.Contains("name") && saved.Contains("coverPath"), "JSON source generation includes observable persisted properties");
            Require(storage.Load().Playlists.Single(item => item.IsSystem).Description == "自己的歌单简介" && storage.Load().Tracks.Single(item => item.Id == "b").IsFavorite, "Favorite flags and customized playlist description survive persistence");
            Require(storage.Load().LastSourcePage == "playlist:" + p.Id && storage.Load().LastPosition == 12, "Playing source and progress persisted independently of viewed page");
        }
        using (var restored = new MainViewModel(new MusicLibraryScanner(storage), new CoreAudio()))
        {
            Require(!restored.IsPlaying && restored.PlaybackPosition == 12 && restored.CurrentTrack?.Id == "d", "Restart displays last song/position without automatic audio");
            restored.TogglePlayPauseCommand.Execute(null); Require(restored.IsPlaying && restored.PlaybackPosition == 12, "Restart play resumes from saved progress");
        }
        using (var brokenInitialization = new MainViewModel(new MusicLibraryScanner(storage), new CoreAudio { ThrowVolume = true }))
        {
            Require(brokenInitialization.PendingStartupNotifications.Count > 0 && brokenInitialization.StatusText.Contains("声音设置"), "Startup sound failure is retained until UI subscribes");
            var pendingShown = 0; brokenInitialization.UserNotification += (_, _) => pendingShown++; brokenInitialization.PublishStartupNotifications();
            Require(pendingShown > 0 && brokenInitialization.PendingStartupNotifications.Count == 0, "Startup notifications can be published after window initialization");
        }
        Console.WriteLine("Core checks passed: favorite migration/order, three modes, playing source, safe seek, restart and error notifications.");
    }
    private static TrackItem Track(string id, string title, bool favorite = false) => new(id, title, "艺人", "专辑", "virtual-" + id + ".wav", ".wav", 20) { IsFavorite = favorite, DurationSeconds = 20 };
    private static void Tick(MainViewModel vm) => typeof(MainViewModel).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, [null, EventArgs.Empty]);
    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class CoreAudio : IAudioPlayer
    {
        private TimeSpan _position; private bool _loaded; private float _volume;
        public bool IsAvailable => true; public bool IsPlaying { get; private set; }
        public bool ThrowPosition { get; set; }
        public bool ThrowVolume { get; set; }
        public TimeSpan Duration => _loaded ? TimeSpan.FromSeconds(20) : TimeSpan.Zero;
        public TimeSpan Position { get => ThrowPosition ? throw new InvalidOperationException("Fixture backend failed") : _position; set { _position = value; PositionWrites++; } }
        public float Volume { get => _volume; set { if (ThrowVolume) throw new InvalidOperationException("Fixture volume initialization failed"); _volume = value; } } public float Speed { get; set; } = 1;
        public int PositionWrites { get; private set; } public int LoadCount { get; private set; } public int PlayCount { get; private set; }
        public event EventHandler? PlaybackStopped; public event EventHandler<AudioPlaybackErrorEventArgs>? PlaybackFailed;
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default) { _position = TimeSpan.FromSeconds(13); _loaded = true; LoadCount++; return Task.CompletedTask; }
        public void Play() { IsPlaying = true; PlayCount++; } public void Pause() => IsPlaying = false;
        public void Stop() { IsPlaying = false; _position = TimeSpan.Zero; }
        public void Complete() { IsPlaying = false; _position = Duration; PlaybackStopped?.Invoke(this, EventArgs.Empty); }
        public void Fail() { IsPlaying = false; PlaybackFailed?.Invoke(this, new("fixture.decode.failed", "测试音频读取失败")); }
        public void Dispose() => Stop();
    }
}
