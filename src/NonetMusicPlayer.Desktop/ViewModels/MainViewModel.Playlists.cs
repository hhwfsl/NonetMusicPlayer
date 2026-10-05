using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string NormalizeSourcePage(string? source)
    {
        if (source == "favorites") source = "playlist:" + Playlist.LikedId;
        if (source?.StartsWith("playlist:", StringComparison.Ordinal) == true)
            return State.Playlists.Any(p => p.Id == source[9..]) ? source : "library";
        return source is "library" or "songs" or "history" or "temporary" || source?.StartsWith("album:", StringComparison.Ordinal) == true || source?.StartsWith("artist:", StringComparison.Ordinal) == true ? source! : "library";
    }
    private string TitleForPage(string page) => page switch
    {
        "library" => L10n.T("Library.MusicLibrary"), "favorites" => L10n.T("Playlists.LikedSongs"), "history" => L10n.T("Playback.RecentlyPlayed"), "albums" => L10n.T("Library.Albums"), "artists" => L10n.T("Library.Artists"),
        "lyrics" => L10n.T("Playback.NowPlaying"), "settings" => L10n.T("Common.Settings"), "plugins" => L10n.T("Plugins.PluginManager"), "statistics" => L10n.T("Statistics.ListeningStatistics"),
        _ => page.StartsWith("album:") ? page[6..] : page.StartsWith("artist:") ? page[7..] : State.Playlists.FirstOrDefault(p => "playlist:" + p.Id == page) is { } p ? p.IsSystem ? L10n.T("Playlists.LikedSongs") : p.Name : L10n.T("Playlists.Playlists")
    };
    private IEnumerable<TrackItem> SourceTracks(string page)
    {
        if (page == "temporary") return _temporaryTracks;
        var providers = Plugins.Installed.Where(p => p.Enabled).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        bool Available(TrackItem t) => t.ProviderId is null || providers.Contains(t.ProviderId);
        var byId = TrackIndex();
        var referenced = State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
        var available = State.Tracks.Where(t => Available(t) && referenced.Contains(t.Id));
        var playlist = State.Playlists.FirstOrDefault(p => "playlist:" + p.Id == page || page == "favorites" && p.IsSystem);
        IEnumerable<TrackItem> result = available;
        if (playlist is not null) result = playlist.TrackIds.Select(id => byId.GetValueOrDefault(id)).OfType<TrackItem>().Where(Available);
        else if (page == "history")
        {
            var recent = State.RecentTemporaryTracks.ToDictionary(t => t.Id, StringComparer.Ordinal);
            result = State.History.Select(id => byId.GetValueOrDefault(id) ?? recent.GetValueOrDefault(id)).OfType<TrackItem>().Where(Available);
        }
        else if (page.StartsWith("album:", StringComparison.Ordinal)) result = available.Where(t => t.Album == page[6..]);
        else if (page.StartsWith("artist:", StringComparison.Ordinal)) result = available.Where(t => t.Artist == page[7..]);
        if (playlist is null && _sourceSorts.TryGetValue(page, out var sort))
            result = sort switch { "artist" => result.OrderBy(t => t.Artist), "album" => result.OrderBy(t => t.Album), "duration" => result.OrderBy(t => t.DurationSeconds), _ => result.OrderBy(t => t.Title) };
        return result;
    }
    private void RefreshPlayingRows()
    {
        if (_highlightedTrack is not null) _highlightedTrack.IsPlayingHere = false;
        _highlightedTrack = null;
        Dictionary<string, int>? playlistOrdinals = null;
        if (CurrentPlaylist is { } playlist)
        {
            playlistOrdinals = new(StringComparer.Ordinal);
            for (var index = 0; index < playlist.TrackIds.Count; index++) playlistOrdinals.TryAdd(playlist.TrackIds[index], index + 1);
        }
        for (var i = 0; i < VisibleTracks.Count; i++)
        {
            var track = VisibleTracks[i]; track.DisplayIndex = playlistOrdinals?.GetValueOrDefault(track.Id, i + 1) ?? i + 1;
            track.IsPlayingHere = Page == PlayingSourcePage && track.Id == CurrentTrack?.Id;
            if (track.IsPlayingHere) _highlightedTrack = track;
        }
    }
    private TrackItem? _highlightedTrack;
    private Dictionary<string, TrackItem> _trackIndex = new(StringComparer.Ordinal);
    private List<TrackItem>? _indexedTracks;
    private Dictionary<string, TrackItem> TrackIndex()
    {
        if (!ReferenceEquals(_indexedTracks, State.Tracks) || _trackIndex.Count != State.Tracks.Count)
        {
            _indexedTracks = State.Tracks; _trackIndex = State.Tracks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        }
        return _trackIndex;
    }
    public string? GetPlaylistArtworkPath(Playlist playlist)
        => !string.IsNullOrWhiteSpace(playlist.CoverPath) ? playlist.CoverPath
            : !playlist.IsSystem && playlist.TrackIds.Count > 0 ? TrackIndex().GetValueOrDefault(playlist.TrackIds[0])?.CoverPath : null;
    public Avalonia.Media.IImage? GetPlaylistArtwork(Playlist playlist)
    {
        if (!string.IsNullOrWhiteSpace(playlist.CoverPath)) return playlist.Artwork;
        if (playlist.IsSystem || playlist.TrackIds.Count == 0) return null;
        return TrackIndex().GetValueOrDefault(playlist.TrackIds[0])?.Artwork;
    }
    private void RequestPlayingTrackLocation()
    {
        try { LocatePlayingTrack?.Invoke(this, EventArgs.Empty); }
        catch (Exception error) { AppLog.Warning("UI", "定位当前播放歌曲失败", error); }
    }
    private void RefreshSourceQueue()
    {
        if (_lyricsPlaybackLease is { } lease) { _playingList = [lease.Track]; return; }
        if (PlayingPlaylistId is not null) _playingList = SourceTracks(PlayingSourcePage).Where(t => _explicitQueueIds is null || _explicitQueueIds.Contains(t.Id)).ToList();
    }
    private void ObserveTracks()
    {
        _indexedTracks = null;
        var present = State.Tracks.ToHashSet();
        foreach (var removed in _observedTracks.Where(t => !present.Contains(t)).ToArray()) { removed.PropertyChanged -= TrackChanged; _observedTracks.Remove(removed); }
        foreach (var track in State.Tracks) if (_observedTracks.Add(track)) track.PropertyChanged += TrackChanged;
    }
    private void TrackChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_syncingFavorites || args.PropertyName != nameof(TrackItem.IsFavorite) || sender is not TrackItem track) return;
        if (track.IsFavorite) { if (!LikedPlaylist.TrackIds.Contains(track.Id)) LikedPlaylist.TrackIds.Insert(0, track.Id); }
        else LikedPlaylist.TrackIds.RemoveAll(id => id == track.Id);
        FavoriteLabelsChanged(); RefreshSourceQueue();
        if (CurrentPlaylist?.IsSystem == true) ApplyFilter();
        Save();
    }
    private void FavoriteLabelsChanged() { OnPropertyChanged(nameof(CurrentFavoriteText)); OnPropertyChanged(nameof(CurrentIsFavorite)); }
    private void SyncFavoriteFlags()
    {
        var ids = LikedPlaylist.TrackIds.ToHashSet(StringComparer.Ordinal); _syncingFavorites = true;
        try { foreach (var track in State.Tracks) track.IsFavorite = ids.Contains(track.Id); }
        finally { _syncingFavorites = false; }
        FavoriteLabelsChanged();
    }
    private static void InsertPlaylistTracks(Playlist playlist, IEnumerable<TrackItem> tracks)
    {
        var existing = playlist.TrackIds.ToHashSet(StringComparer.Ordinal);
        var additions = tracks.Select(t => t.Id).Where(id => existing.Add(id)).ToArray();
        playlist.TrackIds.InsertRange(0, additions);
    }
    public void ToggleFavorite(TrackItem? track)
    {
        if (track is null) return;
        var undoCount = _undo.Count;
        EnsureStored([track]);
        var before = LikedPlaylist.TrackIds.ToList();
        _undo.Push(() => { LikedPlaylist.TrackIds = before; SyncFavoriteFlags(); });
        ObserveTracks(); track.IsFavorite = !track.IsFavorite;
        if (!track.IsFavorite) PruneUnreferencedTracks(); GroupUndoActions(undoCount);
        StatusText = track.IsFavorite ? L10n.T("Playlists.AddedToLikedSongsCtrlZToUndo") : L10n.T("Playlists.RemovedFromLikedSongsCtrlZToUndo");
        CompleteOperation(track.IsFavorite ? "favorite.add" : "favorite.remove");
    }
    public void FavoriteTracks(IEnumerable<TrackItem> tracks) => AddToPlaylist(LikedPlaylist, tracks);
    public void UnfavoriteTracks(IEnumerable<TrackItem> tracks)
    {
        var undoCount = _undo.Count;
        var ids = tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal); var before = LikedPlaylist.TrackIds.ToList();
        LikedPlaylist.TrackIds.RemoveAll(ids.Contains); SyncFavoriteFlags();
        _undo.Push(() => { LikedPlaylist.TrackIds = before; SyncFavoriteFlags(); });
        PruneUnreferencedTracks(); GroupUndoActions(undoCount);
        RefreshSourceQueue(); Save(); ApplyFilter(); CompleteOperation("favorite.remove");
    }
    public void ReorderPlaylistTracks(Playlist playlist, IEnumerable<string> movingIds, string targetId, bool before)
    {
        if (!State.Playlists.Contains(playlist) || !playlist.TrackIds.Contains(targetId)) return;
        var selected = movingIds.ToHashSet(StringComparer.Ordinal);
        var moving = playlist.TrackIds.Where(selected.Contains).ToArray();
        if (moving.Length == 0 || selected.Contains(targetId)) return;
        var previous = playlist.TrackIds.ToList(); playlist.TrackIds.RemoveAll(selected.Contains);
        var index = playlist.TrackIds.IndexOf(targetId) + (before ? 0 : 1); playlist.TrackIds.InsertRange(index, moving);
        _undo.Push(() => playlist.TrackIds = previous); RefreshSourceQueue(); Save(); ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty);
        CompleteOperation("playlist.move");
    }
    public void ReorderPlaylists(string movingId, string targetId, bool before)
    {
        if (movingId == targetId) return;
        var moving = State.Playlists.FirstOrDefault(p => p.Id == movingId); var target = State.Playlists.FirstOrDefault(p => p.Id == targetId);
        if (moving is null || target is null) return;
        var previous = State.Playlists.ToArray(); State.Playlists.Remove(moving);
        State.Playlists.Insert(State.Playlists.IndexOf(target) + (before ? 0 : 1), moving);
        void Sync() { Playlists.Clear(); foreach (var p in State.Playlists) Playlists.Add(p); }
        Sync(); _undo.Push(() => { State.Playlists = previous.ToList(); Sync(); }); Save(); ViewChanged?.Invoke(this, EventArgs.Empty);
        CompleteOperation("playlist.reorder");
    }
    public Playlist CreatePlaylist(string name)
    {
        var playlist = new Playlist { Name = ValidName(name) }; State.Playlists.Add(playlist); Playlists.Add(playlist);
        Save(); ViewChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("playlist.create", new System.Text.Json.Nodes.JsonObject { ["id"] = playlist.Id }); return playlist;
    }
    public void RenamePlaylist(Playlist playlist, string name)
    {
        if (playlist.IsSystem) { ReportWarning(L10n.T("Playlists.LikedSongsCannotBeRenamedOrDeletedItsDescription")); return; }
        playlist.Name = ValidName(name); Save();
        if (Page == "playlist:" + playlist.Id) PageTitle = playlist.Name;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        CompleteOperation("playlist.rename");
    }
    private static string ValidName(string name) { name = name.Trim(); if (name.Length is 0 or > 80) throw new InvalidDataException(L10n.T("Playlists.PlaylistNamesMustContainCharacters")); return name; }
    public void UpdatePlaylistDetails(Playlist playlist, string name, string description, string? coverPath)
    {
        if (!State.Playlists.Contains(playlist)) throw new InvalidDataException(L10n.T("Playlists.ThePlaylistNoLongerExists"));
        var validName = playlist.IsSystem ? L10n.T("Playlists.LikedSongs") : ValidName(name);
        description = description.Trim(); if (description.Length > 500) throw new InvalidDataException(L10n.T("Playlists.PlaylistDescriptionsCannotExceedCharacters"));
        var storedCover = string.IsNullOrWhiteSpace(coverPath) ? null : playlist.CoverPath == coverPath ? coverPath : StorePlaylistCover(playlist, coverPath);
        playlist.Name = validName; playlist.Description = description; playlist.CoverPath = storedCover;
        if (CurrentPlaylist == playlist) PageTitle = playlist.Name;
        Save(); ViewChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.T("Playlists.PlaylistDetailsUpdated");
        CompleteOperation("playlist.describe"); CompleteOperation("playlist.cover");
    }
    public void SetPlaylistCover(Playlist playlist, string? coverPath) => UpdatePlaylistDetails(playlist, playlist.Name, playlist.Description, coverPath);
    private string StorePlaylistCover(Playlist playlist, string source) => StoreManagedCover("playlist-" + playlist.Id, source);
    public void DeletePlaylist(Playlist playlist)
    {
        if (playlist.IsSystem) { ReportWarning(L10n.T("Playlists.LikedSongsIsABuiltInPlaylistAndCannot")); return; }
        var index = State.Playlists.IndexOf(playlist); if (index < 0) return;
        var undoCount = _undo.Count;
        State.Playlists.Remove(playlist); Playlists.Remove(playlist);
        _undo.Push(() => { State.Playlists.Insert(Math.Min(index, State.Playlists.Count), playlist); Playlists.Insert(Math.Min(index, Playlists.Count), playlist); });
        if (PlayingPlaylistId == playlist.Id) { PlayingSourcePage = "library"; _explicitQueueIds = null; _playingList = SourceTracks("library").ToList(); }
        PruneUnreferencedTracks();
        GroupUndoActions(undoCount);
        if (Page == "playlist:" + playlist.Id) Navigate("library");
        else { ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty); }
        Save(); StatusText = L10n.T("Playlists.PlaylistDeletedMusicFilesKeptCtrlZToUndo");
        CompleteOperation("playlist.delete");
    }
    public void AddToPlaylist(Playlist playlist, IEnumerable<TrackItem> tracks)
    {
        var before = playlist.TrackIds.ToList(); var items = tracks.DistinctBy(t => t.Id).ToArray();
        EnsureStored(items);
        InsertPlaylistTracks(playlist, items); if (playlist.IsSystem) SyncFavoriteFlags();
        _undo.Push(() => { playlist.TrackIds = before; if (playlist.IsSystem) SyncFavoriteFlags(); });
        RefreshSourceQueue(); Save(); ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.Format("Playlists.AddedToNewSongsAppearFirstCtrlZTo", playlist.Name);
        CompleteOperation(playlist.IsSystem ? "favorite.add" : "playlist.add");
    }
    private void EnsureStored(IEnumerable<TrackItem> tracks)
    {
        var ids = State.Tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            if (ids.Add(track.Id)) State.Tracks.Insert(0, track);
            if (PlayingSourcePage == "temporary" && _temporaryTracks.Any(t => t.Id == track.Id))
            {
                _countedTemporaryIds.Add(track.Id);
                if (CurrentTrack?.Id == track.Id) { StartListeningSession(); State.History.Remove(track.Id); State.History.Insert(0, track.Id); TrimHistory(); }
            }
        }
        TrimHistory();
        ObserveTracks();
    }
    public void RemoveTracks(IEnumerable<TrackItem> tracks)
        => RemoveTracksCore(tracks, true);
    public void RemoveLibraryTracks(IEnumerable<TrackItem> tracks)
        => RemoveTracksCore(tracks, false);
    private void RemoveTracksCore(IEnumerable<TrackItem> tracks, bool useCurrentPlaylist)
    {
        var ids = tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal); if (ids.Count == 0) return;
        if (useCurrentPlaylist && Page == "history") { RemoveRecentTracks(ids); return; }
        if (useCurrentPlaylist && (Page.StartsWith("album:") || Page.StartsWith("artist:") || Page is "albums" or "artists")) return;
        var undoCount = _undo.Count;
        if (useCurrentPlaylist && CurrentPlaylist is { } playlist)
        {
            var before = playlist.TrackIds.ToList(); playlist.TrackIds.RemoveAll(ids.Contains); if (playlist.IsSystem) SyncFavoriteFlags();
            _undo.Push(() => { playlist.TrackIds = before; if (playlist.IsSystem) SyncFavoriteFlags(); });
            if (PlayingPlaylistId == playlist.Id) RefreshSourceQueue();
            PruneUnreferencedTracks();
        }
        else
        {
            var before = State.Tracks.ToList(); var orders = State.Playlists.ToDictionary(p => p.Id, p => p.TrackIds.ToList());
            State.Tracks.RemoveAll(t => ids.Contains(t.Id)); foreach (var p in State.Playlists) p.TrackIds.RemoveAll(ids.Contains);
            CleanupTrackData(before.Where(t => ids.Contains(t.Id)).ToArray());
            _undo.Push(() => { State.Tracks = before; foreach (var p in State.Playlists) if (orders.TryGetValue(p.Id, out var order)) p.TrackIds = order; ObserveTracks(); SyncFavoriteFlags(); });
            ObserveTracks(); _playingList.RemoveAll(t => ids.Contains(t.Id));
            if (CurrentTrack is not null && ids.Contains(CurrentTrack.Id)) { StopPlayback(); _audioLoaded = false; CurrentTrack = null; }
        }
        GroupUndoActions(undoCount);
        Save(); ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.T("Library.RemovedFromThisListFilesKeptCtrlZTo");
        CompleteOperation(useCurrentPlaylist && CurrentPlaylist is not null ? "playlist.remove" : "music.remove");
    }
    /// <summary>最近播放是独立历史集合，删除只改变历史，绝不改变歌单或音乐集合。</summary>
    public void RemoveRecentTracks(IEnumerable<string> trackIds)
    {
        var ids = trackIds.ToHashSet(StringComparer.Ordinal); var history = State.History.ToList(); var temporary = State.RecentTemporaryTracks.ToList();
        State.History.RemoveAll(ids.Contains); State.RecentTemporaryTracks.RemoveAll(t => ids.Contains(t.Id));
        _undo.Push(() => { State.History = history; State.RecentTemporaryTracks = temporary; });
        Save(); ApplyFilter(); CompleteOperation("history.remove");
    }
    private void PruneUnreferencedTracks()
    {
        var references = State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
        var removed = State.Tracks.Where(t => !references.Contains(t.Id)).ToArray();
        if (removed.Length == 0) return;
        State.Tracks.RemoveAll(t => !references.Contains(t.Id));
        CleanupTrackData(removed);
        _undo.Push(() => { foreach (var track in removed) if (!State.Tracks.Any(t => t.Id == track.Id)) State.Tracks.Add(track); });
        ObserveTracks(); RefreshSourceQueue();
    }
    private void CleanupTrackData(IReadOnlyList<TrackItem> removed)
    {
        var ids = removed.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var history = State.History.ToList(); var recent = State.RecentTemporaryTracks.ToList(); var statistics = State.ListeningEntries.ToList();
        _undo.Push(() => { State.History = history; State.RecentTemporaryTracks = recent; State.ListeningEntries = statistics; });
        State.History.RemoveAll(ids.Contains); State.RecentTemporaryTracks.RemoveAll(t => ids.Contains(t.Id)); State.ListeningEntries.RemoveAll(e => ids.Contains(e.TrackId));
        foreach (var track in removed)
        {
            // 只回收可证明由应用生成的精确文件，不删除音频、相邻原始歌词或用户指定的源文件。
            foreach (var ext in new[] { ".lrc", ".txt" })
            {
                var file = Lyrics.PathFor(track.Id, ext);
                var source = track.LyricsSourcePath ?? (string.IsNullOrWhiteSpace(track.FilePath) ? null : Path.ChangeExtension(track.FilePath, ext));
                if (source is not null && string.Equals(Path.GetFullPath(file), Path.GetFullPath(source), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                RemoveGeneratedFile(file, Lyrics.Folder);
            }
            if (track.CoverPath is { } cover && !State.Tracks.Any(t => t.CoverPath == cover) && !State.Playlists.Any(p => p.CoverPath == cover) && !State.GroupCovers.Values.Contains(cover)) RemoveGeneratedFile(cover, Storage.ArtworkFolder);
            track.ReleaseArtwork();
        }
    }
    private void RemoveGeneratedFile(string file, string folder)
    {
        if (!File.Exists(file) || !DataDirectoryService.Contains(folder, file)) return;
        try
        {
            DataDirectoryService.RejectLinkedAncestors(file);
            var archive = Path.Combine(Storage.BackupFolder, "RemovedTrackData", Guid.NewGuid().ToString("N"), Path.GetFileName(file));
            DataDirectoryService.RejectLinkedAncestors(archive);
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!); File.Copy(file, archive, false); File.Delete(file);
            _undo.Push(() => { if (!File.Exists(file)) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.Copy(archive, file, false); } });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { AppLog.Warning("Library", "Generated data cleanup incomplete", error); ReportWarning(L10n.T("Library.CleanupIncomplete")); }
    }
    /// <summary>一次用户删除对应一次撤销；回收文件、元数据与歌单关联统一恢复。</summary>
    private void GroupUndoActions(int previousCount)
    {
        var actions = new List<Action>(); while (_undo.Count > previousCount) actions.Add(_undo.Pop());
        if (actions.Count > 0) _undo.Push(() => { foreach (var action in actions) action(); });
    }
    public void MoveInPlaylist(TrackItem track, int delta)
    {
        if (CurrentPlaylist is not { } playlist) return; var from = playlist.TrackIds.IndexOf(track.Id); var to = from + delta;
        if (from < 0 || to < 0 || to >= playlist.TrackIds.Count) return;
        (playlist.TrackIds[from], playlist.TrackIds[to]) = (playlist.TrackIds[to], playlist.TrackIds[from]);
        if (PlayingPlaylistId == playlist.Id) RefreshSourceQueue();
        Save(); ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty);
        CompleteOperation("playlist.move");
    }
    [RelayCommand] private void Undo()
    {
        if (!_undo.TryPop(out var action)) { ReportWarning(L10n.T("Common.NothingToUndo")); return; }
        try { action(); ObserveTracks(); RefreshSourceQueue(); Save(); ApplyFilter(); FavoriteLabelsChanged(); ViewChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.T("Common.LastOperationUndone"); CompleteOperation("undo"); }
        catch (Exception error) { ReportError(L10n.T("Common.UndoFailed"), error); }
    }
    public void Sort(string field)
    {
        if (CurrentPlaylist is not null) { ReportWarning(L10n.T("Playlists.PlaylistsUseManualOrderingMoveSongsUpOrDown")); return; }
        _sourceSorts[Page] = field; ApplyFilter(); StatusText = L10n.T("Playback.ListSortedPlaybackFollowsTheFullListSearchingDoes");
    }
}
