using System.Security.Cryptography;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using SkiaSharp;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isMigratingData;
    public bool IsMigratingData => _isMigratingData;
    private void EnsureNotMigratingData()
    {
        if (IsMigratingData) throw new InvalidOperationException(L10n.T("Storage.DataMigrationIsInProgressWaitBeforeChangingFolders"));
    }
    public DataDirectoryChangeResult ConfigureDataDirectory(string newRoot, string? backupFolder = null, bool copyExisting = true)
    {
        EnsureNotMigratingData();
        if (IsBusy) throw new InvalidOperationException(L10n.T("Storage.AnotherOperationIsInProgressWaitBeforeMigratingData"));
        UpdateListeningStatistics();
        State.LastTrackId = CurrentTrack?.Id; State.LastPosition = PlaybackPosition; State.LastSourcePage = PlayingSourcePage; State.LastPlaylistId = PlayingPlaylistId;
        Storage.Save(State);
        var result = Storage.ConfigureDataDirectory(newRoot, backupFolder, copyExisting);
        Lyrics.MirrorFolder = DataDirectoryService.Contains(result.OldRoot, Lyrics.Folder) ? DataDirectoryService.Remap(Lyrics.Folder, result.OldRoot, result.NewRoot) : null;
        Storage.Save(State); StatusText = L10n.T("Common.DataCopiedTheOriginalFolderIsRetainedTheNew");
        SettingsChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("data.directory"); return result;
    }
    public async Task<DataDirectoryChangeResult> ConfigureDataDirectoryAsync(string newRoot, string? backupFolder = null, bool copyExisting = true)
    {
        var started = false;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                EnsureNotMigratingData();
                if (IsBusy || _disposed) throw new InvalidOperationException(L10n.T("Storage.ThePlayerIsBusyOrClosingTryMigratingData"));
                IsBusy = true; started = true; _isMigratingData = true; Storage.SetMigrationActive(true); OnPropertyChanged(nameof(IsMigratingData)); StatusText = L10n.T("Statistics.CopyingDataPlaybackAndStatisticsContinueTheOriginalFolder");
                UpdateListeningStatistics();
                State.LastTrackId = CurrentTrack?.Id; State.LastPosition = PlaybackPosition; State.LastSourcePage = PlayingSourcePage; State.LastPlaylistId = PlayingPlaylistId;
                Storage.Save(State);
            });
            // 后台任务只复制存储文件并更新引导路径，不枚举或修改界面模型状态。
            var result = await Task.Run(() => Storage.ConfigureDataDirectory(newRoot, backupFolder, copyExisting));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Lyrics.MirrorFolder = DataDirectoryService.Contains(result.OldRoot, Lyrics.Folder) ? DataDirectoryService.Remap(Lyrics.Folder, result.OldRoot, result.NewRoot) : null;
                UpdateListeningStatistics();
                State.LastTrackId = CurrentTrack?.Id; State.LastPosition = PlaybackPosition; State.LastSourcePage = PlayingSourcePage; State.LastPlaylistId = PlayingPlaylistId;
                Storage.Save(State); StatusText = L10n.T("Common.DataCopiedTheOriginalFolderIsRetainedTheNew");
                SettingsChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("data.directory");
            });
            return result;
        }
        finally { if (started) await Dispatcher.UIThread.InvokeAsync(() => { Storage.SetMigrationActive(false); IsBusy = false; _isMigratingData = false; OnPropertyChanged(nameof(IsMigratingData)); }); }
    }
    public void SetBackupFolder(string folder)
    {
        EnsureNotMigratingData();
        Storage.SetBackupFolder(folder); Settings.BackupFolder = Storage.BackupFolder; Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.T("Storage.BackupFolderUpdatedTheChangeIsEffectiveNow"); CompleteOperation("settings.set");
    }
    public void RestoreLibraryBackup(string? path = null)
    {
        EnsureNotMigratingData();
        // 先完整解析并验证备份，再修改活动对象；不修改用户选中的备份文件。
        var selectedJson = Storage.ReadBackupText(path); var restored = Storage.ParseBackup(selectedJson);
        UpdateListeningStatistics();
        State.LastTrackId = CurrentTrack?.Id; State.LastPosition = PlaybackPosition; State.LastSourcePage = PlayingSourcePage; State.LastPlaylistId = PlayingPlaylistId;
        Storage.ArchiveRestore(State, selectedJson);
        foreach (var playlist in restored.Playlists)
            if (playlist.CoverPath is { } cover && File.Exists(cover) && !DataDirectoryService.Contains(Storage.ArtworkFolder, cover)) playlist.CoverPath = StoreManagedCover("playlist-" + playlist.Id, cover);
        foreach (var key in restored.GroupCovers.Keys.ToArray())
            if (File.Exists(restored.GroupCovers[key]) && !DataDirectoryService.Contains(Storage.ArtworkFolder, restored.GroupCovers[key])) restored.GroupCovers[key] = StoreManagedCover("group-" + key, restored.GroupCovers[key]);
        foreach (var track in restored.Tracks)
            if (track.CoverPath is { } cover && File.Exists(cover) && !DataDirectoryService.Contains(Storage.ArtworkFolder, cover)) track.CoverPath = StoreManagedCover("track-" + track.Id, cover);
        var oldLyrics = !string.IsNullOrWhiteSpace(restored.Settings.LyricsFolder) && Path.IsPathRooted(restored.Settings.LyricsFolder) ? restored.Settings.LyricsFolder
            : !string.IsNullOrWhiteSpace(restored.ManagedDataRoot) ? Path.Combine(restored.ManagedDataRoot, "Lyrics") : null;
        if (oldLyrics is null || !Directory.Exists(oldLyrics))
        {
            var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(path ?? Storage.BackupPath))!;
            oldLyrics = new[] { Path.Combine(sourceDirectory, "Lyrics"), Path.Combine(Path.GetDirectoryName(sourceDirectory) ?? sourceDirectory, "Lyrics") }.FirstOrDefault(Directory.Exists);
        }
        if (oldLyrics is not null && Directory.Exists(oldLyrics))
        {
            var oldService = new LyricsService(oldLyrics);
            var restoredLyrics = new LyricsService(Storage.DefaultLyricsFolder) { MirrorFolder = Storage.PendingRoot is { } pending ? Path.Combine(pending, "Lyrics") : null };
            foreach (var track in restored.Tracks)
            {
                if (File.Exists(restoredLyrics.PathFor(track.Id)) || File.Exists(restoredLyrics.PathFor(track.Id, ".txt"))) continue;
                foreach (var extension in new[] { ".lrc", ".txt" })
                    if (File.Exists(oldService.PathFor(track.Id, extension))) { restoredLyrics.Import(track.Id, oldService.PathFor(track.Id, extension)); break; }
            }
        }
        StopPlayback();
        foreach (var track in State.Tracks) track.ReleaseArtwork(); foreach (var playlist in State.Playlists) playlist.ReleaseArtwork();
        State.Tracks = restored.Tracks; State.Playlists = restored.Playlists; State.MusicFolders = restored.MusicFolders; State.History = restored.History;
        State.RecentTemporaryTracks = restored.RecentTemporaryTracks;
        State.Settings = restored.Settings; State.Settings.BackupFolder = Storage.BackupFolder; State.Settings.LyricsFolder = "";
        if (State.Settings.BackgroundImagePath is { } background && File.Exists(background) && !DataDirectoryService.Contains(Storage.Root, background)) State.Settings.BackgroundImagePath = AppBackgroundService.Import(Storage, background);
        State.ListeningEntries = restored.ListeningEntries; State.GroupCovers = restored.GroupCovers; State.SoftwareDefaultGroupCovers = restored.SoftwareDefaultGroupCovers;
        State.LastTrackId = restored.LastTrackId; State.LastPosition = restored.LastPosition; State.LastSourcePage = restored.LastSourcePage; State.LastPlaylistId = restored.LastPlaylistId;
        State.LastTemporaryFile = restored.LastTemporaryFile; State.LastTemporaryCounted = restored.LastTemporaryCounted; _temporaryTracks.Clear(); _countedTemporaryIds.Clear();
        Playlists.Clear(); foreach (var playlist in State.Playlists) Playlists.Add(playlist); ObserveTracks(); _undo.Clear();
        TrimHistory();
        _explicitQueueIds = null; PlayingSourcePage = NormalizeSourcePage(State.LastSourcePage); CurrentTrack = State.Tracks.Concat(State.RecentTemporaryTracks).FirstOrDefault(track => track.Id == State.LastTrackId);
        if (PlayingSourcePage == "temporary" && CurrentTrack is not null)
        {
            _temporaryTracks = [CurrentTrack];
            if (State.LastTemporaryCounted) _countedTemporaryIds.Add(CurrentTrack.Id);
        }
        _updatingPosition = true;
        try { PlaybackDuration = Math.Max(1, Math.Max(CurrentTrack?.DurationSeconds ?? 0, State.LastPosition)); PlaybackPosition = CurrentTrack is null ? 0 : Math.Clamp(State.LastPosition, 0, PlaybackDuration); }
        finally { _updatingPosition = false; }
        _audioLoaded = false; _resumePending = CurrentTrack is not null; _playingList = SourceTracks(PlayingSourcePage).ToList();
        _sessionListeningSeconds = 0; _sessionCounted = false; ResetListeningAnchor();
        Volume = Settings.Volume; ApplySettings(); Navigate(PlayingSourcePage); StatisticsChanged?.Invoke(this, EventArgs.Empty); Save();
        StatusText = L10n.T("Storage.LibraryRestoredPreviousDataAndTheSelectedBackupWere"); CompleteOperation("data.restore");
    }
    public IImage? GetGroupCover(string groupType, string name)
    {
        var key = GroupCoverKey(groupType, name);
        return State.GroupCovers.TryGetValue(key, out var path) ? ArtworkCache.Shared.GetImage(path) : null;
    }
    public void SetGroupCover(string groupType, string name, string? path)
    {
        var key = GroupCoverKey(groupType, name);
        var stored = string.IsNullOrWhiteSpace(path) ? null : StoreManagedCover("group-" + key, path);
        if (State.GroupCovers.TryGetValue(key, out var old)) ArtworkCache.Shared.Invalidate(old);
        if (stored is null) State.GroupCovers.Remove(key); else State.GroupCovers[key] = stored;
        State.SoftwareDefaultGroupCovers.Remove(key);
        Save(); ViewChanged?.Invoke(this, EventArgs.Empty); StatusText = stored is null ? L10n.T("Common.DefaultCoverRestored") : L10n.T("Storage.CustomCoverSavedToTheDataFolder"); CompleteOperation(groupType.StartsWith("artist") ? "artist.cover" : "album.cover");
    }
    public bool UsesSoftwareDefaultGroupCover(string groupType, string name) => State.SoftwareDefaultGroupCovers.Contains(GroupCoverKey(groupType, name));
    public IImage? ResolveGroupCover(string groupType, string name) => ResolveGroupCover(groupType, name, SourceTracks(GroupCoverKey(groupType, name)));
    public IImage? ResolveGroupCover(string groupType, string name, IEnumerable<TrackItem> tracks)
    {
        if (UsesSoftwareDefaultGroupCover(groupType, name)) return null;
        if (GetGroupCover(groupType, name) is { } custom) return custom;
        // 仅在可见卡片或标题需要时解码封面；首首歌曲无封面时继续查找分类内其他歌曲。
        foreach (var track in tracks)
            if (!string.IsNullOrWhiteSpace(track.CoverPath) && track.Artwork is { } artwork) return artwork;
        return null;
    }
    public void SetGroupSoftwareDefaultCover(string groupType, string name)
    {
        var key = GroupCoverKey(groupType, name);
        if (State.GroupCovers.Remove(key, out var old)) ArtworkCache.Shared.Invalidate(old);
        State.SoftwareDefaultGroupCovers.Add(key);
        Save(); ViewChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.T("Common.ApplicationDefaultCoverApplied"); CompleteOperation(groupType.StartsWith("artist") ? "artist.cover" : "album.cover");
    }
    private static string GroupCoverKey(string groupType, string name)
    {
        var type = groupType.ToLowerInvariant() switch { "artist" or "artists" => "artist", "album" or "albums" => "album", _ => throw new InvalidDataException(L10n.T("Common.CoverTypeMustBeArtistOrAlbum")) };
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException(L10n.T("Library.TheArtistOrAlbumNameCannotBeEmpty"));
        return type + ":" + name;
    }
    private string StoreManagedCover(string key, string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException(L10n.T("Common.TheCoverFileDoesNotExistChooseAnotherImage"));
        if (new FileInfo(source).Length > 20 * 1024 * 1024) throw new InvalidDataException(L10n.T("Common.CoverImagesCannotExceedMB"));
        using var stream = File.OpenRead(source); var contentHash = Convert.ToHexString(SHA256.HashData(stream)); stream.Position = 0;
        var file = Path.Combine(Storage.ArtworkFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16] + "-" + contentHash[..16] + ".png");
        if (File.Exists(file)) return file;
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            int width, height;
            using (var probe = File.OpenRead(source))
            using (var codec = SKCodec.Create(probe))
            {
                if (codec is null) throw new InvalidDataException(L10n.T("Common.TheCoverImageFormatIsNotSupported"));
                width = codec.Info.Width; height = codec.Info.Height;
            }
            if (width <= 0 || height <= 0 || (long)width * height > 64 * 1024 * 1024) throw new InvalidDataException(L10n.T("Common.TheCoverImageDimensionsAreTooLargeResizeIt"));
            using var image = width >= height ? Bitmap.DecodeToWidth(stream, Math.Min(320, width)) : Bitmap.DecodeToHeight(stream, Math.Min(320, height));
            image.Save(temporary, PngBitmapEncoderOptions.Default); File.Move(temporary, file, true); return file;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
