using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly MusicLibraryScanner _scanner;
    private readonly IAudioPlayer _audio;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _playGate = new(1);
    private readonly Stack<Action> _undo = new();
    private List<TrackItem> _playingList = [];
    // 此实例覆盖当前会话的各歌单，切换页面不会清空随机历史。
    private readonly Core.Playback.WeightedShuffleSelector _shuffle = new();
    private List<TrackItem> _temporaryTracks = [];
    private readonly HashSet<string> _countedTemporaryIds = new(StringComparer.Ordinal);
    private HashSet<string>? _explicitQueueIds;
    private CancellationTokenSource? _scanCancellation;
    private bool _loadingAudio, _updatingPosition, _disposed;
    private bool _audioLoaded, _resumePending, _syncingFavorites;
    private readonly HashSet<TrackItem> _observedTracks = [];
    private readonly Dictionary<string, string> _sourceSorts = new(StringComparer.Ordinal);
    private string? _lastNotification;
    private DateTimeOffset _lastNotificationTime;
    private readonly List<UserNotificationEventArgs> _startupNotifications = [];
    private long _playRequest;
    private DateTimeOffset _lastSave = DateTimeOffset.UtcNow;
    private string? _maintainedBackground;
    public AppStorage Storage => _scanner.Storage;
    public AppState State { get; }
    public AppSettings Settings => State.Settings;
    public string VersionText { get; } = typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
    public LyricsService Lyrics => _scanner.Lyrics;
    public PluginManager Plugins { get; }
    public BulkObservableCollection<TrackItem> VisibleTracks { get; } = [];
    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<LyricLine> LyricLines { get; } = [];
    public event EventHandler? ViewChanged;
    public event EventHandler? SettingsChanged;
    public event EventHandler? LocatePlayingTrack;
    public event EventHandler? NavigationRequested;
    public event EventHandler<UserNotificationEventArgs>? UserNotification;
    public IReadOnlyList<UserNotificationEventArgs> PendingStartupNotifications => _startupNotifications;
    public bool IsSeeking { get; set; }
    public MainViewModel(MusicLibraryScanner scanner, IAudioPlayer audioPlayer, TimeProvider? timeProvider = null)
    {
        _listeningClock = timeProvider ?? TimeProvider.System;
        _scanner = scanner; _audio = audioPlayer; State = Storage.Load(); Plugins = new(Storage);
        _maintainedBackground = Storage.Root + "\0" + Settings.BackgroundImagePath;
        TrimHistory();
        AppLog.Initialize(Storage.Root);
        AppBackgroundService.CleanupUnused(Storage, Settings.BackgroundImagePath);
        foreach (var playlist in State.Playlists) Playlists.Add(playlist);
        ApplyLyricsDirectory();
        try { Volume = Settings.Volume; _audio.Volume = (float)(Volume / 100); _audio.DeviceName = Settings.DeviceName; }
        catch (Exception e) { ReportError(L10n.T("Playback.UnableToApplyAudioSettings"), e); }
        VisibleTracks.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(TrackCountText)); OnPropertyChanged(nameof(IsEmptyStateVisible)); };
        _audio.PlaybackStopped += PlaybackEnded;
        _audio.PlaybackFailed += PlaybackFailed;
        _audio.OutputDeviceChanged += OutputChanged;
        _audio.OutputDevicesChanged += DevicesChanged;
        ObserveTracks();
        PlayingSourcePage = NormalizeSourcePage(State.LastSourcePage);
        CurrentTrack = State.Tracks.Concat(State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == State.LastTrackId); // 启动时只恢复选择，不自动播放。
        if (CurrentTrack is null && State.LastTemporaryFile is { } temporary && File.Exists(temporary))
        {
            try { CurrentTrack = _scanner.ReadTrack(temporary); _temporaryTracks = [CurrentTrack]; PlayingSourcePage = "temporary"; }
            catch (Exception error) { ReportError(L10n.T("Playback.CouldNotReadTheLastTemporaryAudioFile"), error); }
        }
        if (PlayingSourcePage == "temporary" && CurrentTrack is not null)
        {
            _temporaryTracks = [CurrentTrack];
            if (State.LastTemporaryCounted && State.Tracks.Any(t => t.Id == CurrentTrack.Id)) _countedTemporaryIds.Add(CurrentTrack.Id);
        }
        _updatingPosition = true;
        PlaybackDuration = Math.Max(1, Math.Max(CurrentTrack?.DurationSeconds ?? 0, State.LastPosition));
        PlaybackPosition = CurrentTrack is null ? 0 : Math.Clamp(State.LastPosition, 0, PlaybackDuration);
        _updatingPosition = false;
        _resumePending = CurrentTrack is not null;
        _playingList = SourceTracks(PlayingSourcePage).ToList();
        Page = PlayingSourcePage == "temporary" ? "library" : PlayingSourcePage; PageTitle = LocalizedTitleForPage(Page);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, Tick); _timer.Start();
        if (Plugins.RecoveryMessage is { } pluginWarning) ReportWarning(pluginWarning);
        if (Settings.ValidationWarning is { } settingsWarning) ReportWarning(settingsWarning);
        StatusText = Storage.RecoveryMessage ?? _startupNotifications.LastOrDefault()?.Message ?? L10n.T("Library.ReadyDragSongsIntoAPlaylistOrAddA");
        ApplyFilter();
    }
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private TrackItem? _currentTrack;
    [ObservableProperty] private double _playbackPosition;
    [ObservableProperty] private double _playbackDuration = 1;
    [ObservableProperty] private double _volume = 80;
    [ObservableProperty] private string _page = "library";
    [ObservableProperty] private string _pageTitle = L10n.T("Library.MusicLibrary");
    [ObservableProperty] private bool _isEditingLayout;
    [ObservableProperty] private string _playingSourcePage = "library";
    public string TrackCountText => string.Format(L10n.T("Common.Tracks"), VisibleTracks.Count);
    public bool IsEmptyStateVisible => !IsBusy && VisibleTracks.Count == 0;
    public string EmptyStateTitle => L10n.T(SearchText.Length > 0 ? L10n.T("Common.NoMatchingSongs") : CurrentPlaylist?.IsSystem == true ? L10n.T("Common.KeepYourFavoriteSongsHere") : L10n.T("Library.MakeMusicPartOfYourDay"));
    public string EmptyStateDescription => L10n.T(SearchText.Length > 0 ? L10n.T("Library.SearchSongsArtistsAlbumsOrSourcesPressEscTo") : L10n.T("Playlists.AddMusicFilesOrDragSongsIntoAPlaylist"));
    public string CurrentTitle => CurrentTrack?.Title ?? L10n.T("Common.ChooseASongToPlay");
    public string CurrentArtist => CurrentTrack?.Artist ?? L10n.T("Statistics.YourMusicYourWay");
    public IImage? CurrentArtwork => CurrentTrack?.Artwork;
    public bool HasCurrentArtwork => CurrentTrack?.HasArtwork == true;
    public string CurrentFavoriteText => CurrentTrack?.FavoriteText ?? "♡";
    public bool CurrentIsFavorite => CurrentTrack?.IsFavorite == true;
    public bool HasCurrentTrack => CurrentTrack is not null;
    public bool IsMuted => Volume <= 0;
    public string VolumeText => $"{Volume:0}%";
    public string PositionText => FormatTime(PlaybackPosition);
    public string DurationText => CurrentTrack is null ? "0:00" : FormatTime(PlaybackDuration);
    public string ModeText => L10n.T(Settings.PlayMode switch { PlayMode.RepeatAll => L10n.T("Playback.RepeatAllAD3221"), PlayMode.RepeatOne => L10n.T("Playback.RepeatOne"), _ => L10n.T("Playback.Shuffle") });
    public string ModeDescription => L10n.T(Settings.PlayMode switch { PlayMode.RepeatAll => L10n.T("Playback.RepeatAll"), PlayMode.RepeatOne => L10n.T("Playback.RepeatOne8CDE79"), _ => L10n.T("Playback.ShuffleF34B7C") });
    public IconKind ModeIcon => Settings.PlayMode switch { PlayMode.RepeatAll => IconKind.RepeatAll, PlayMode.RepeatOne => IconKind.RepeatOne, _ => IconKind.Shuffle };
    public string LibraryPath => string.Join("；", State.MusicFolders);
    public Playlist? CurrentPlaylist => State.Playlists.FirstOrDefault(p => "playlist:" + p.Id == Page);
    public Playlist LikedPlaylist => State.Playlists.First(p => p.IsSystem);
    public string? PlayingPlaylistId => PlayingSourcePage.StartsWith("playlist:", StringComparison.Ordinal) ? PlayingSourcePage[9..] : null;
    public IReadOnlyList<string> AudioDevices => _audio.Devices;
    partial void OnSearchTextChanged(string value) { ApplyFilter(); if (State is not null) CompleteOperation("music.search"); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmptyStateVisible));
    partial void OnVolumeChanged(double value)
    {
        if (!_loadingAudio && !_disposed) try { _audio.Volume = (float)(value / 100); } catch (Exception e) { ReportError(L10n.T("Playback.UnableToChangeVolume"), e); }
        if (State is not null) Settings.Volume = value;
        OnPropertyChanged(nameof(IsMuted)); OnPropertyChanged(nameof(VolumeText));
        if (State is not null) CompleteOperation("player.volume");
    }
    partial void OnPlaybackPositionChanged(double value)
    {
        OnPropertyChanged(nameof(PositionText));
        if (!_updatingPosition && !_loadingAudio && _audioLoaded && CurrentTrack is not null)
            try { UpdateListeningStatistics(); _audio.Position = TimeSpan.FromSeconds(value); ResetListeningAnchor(); } catch (Exception e) { ReportError(L10n.T("Playback.UnableToSeek"), e); }
    }
    partial void OnPlaybackDurationChanged(double value) => OnPropertyChanged(nameof(DurationText));
    partial void OnCurrentTrackChanged(TrackItem? value)
    {
        foreach (var name in new[] { nameof(CurrentTitle), nameof(CurrentArtist), nameof(CurrentArtwork), nameof(HasCurrentArtwork), nameof(CurrentFavoriteText), nameof(CurrentIsFavorite), nameof(HasCurrentTrack) }) OnPropertyChanged(name);
        ReloadLyrics();
        RefreshPlayingRows();
    }
    partial void OnPlayingSourcePageChanged(string value) { OnPropertyChanged(nameof(PlayingPlaylistId)); RefreshPlayingRows(); }
    public void Navigate(string page, string? title = null)
    {
        Page = page == "favorites" ? "playlist:" + Playlist.LikedId : page == "temporary" ? "library" : page; SearchText = "";
        PageTitle = title ?? LocalizedTitleForPage(Page);
        OnPropertyChanged(nameof(CurrentPlaylist));
        ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty);
        NavigationRequested?.Invoke(this, EventArgs.Empty);
        RequestPlayingTrackLocation();
        CompleteOperation("navigate");
    }
    public void ApplyFilter()
    {
        if (Page is "terminal" or "settings" or "plugins" or "statistics" or "lyrics" || Page.StartsWith("plugin:", StringComparison.Ordinal)) { RefreshPlayingRows(); return; }
        ObserveTracks();
        IEnumerable<TrackItem> tracks = SourceTracks(Page);
        var query = SearchText.Trim();
        if (query.Length > 0) tracks = tracks.Where(t => new[] { t.Title, t.Artist, t.Album, t.ProviderId ?? L10n.T("Common.Local") }.Any(v => v.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        VisibleTracks.ReplaceAll(tracks);
        RefreshPlayingRows();
        OnPropertyChanged(nameof(EmptyStateTitle)); OnPropertyChanged(nameof(EmptyStateDescription));
    }
    public async Task<IReadOnlyList<TrackItem>> ImportAsync(IEnumerable<string> paths, bool favorite = false, Playlist? playlist = null, bool autoplay = false)
    {
        if (IsBusy) { ReportWarning(L10n.T("Common.AnImportIsInProgressWaitForItTo")); return []; }
        IsBusy = true; var cancellation = new CancellationTokenSource(); _scanCancellation = cancellation; var inputs = paths.ToArray(); StatusText = L10n.T("Lyrics.ScanningMusicArtworkAndLyrics");
        try
        {
            var tracks = await Task.Run(() => _scanner.ScanPaths(inputs, cancellation.Token));
            if (_disposed) return [];
            playlist ??= favorite ? LikedPlaylist : CurrentPlaylist;
            if (tracks.Count > 0 && playlist is null)
                playlist = State.Playlists.FirstOrDefault(p => !p.IsSystem && p.Name == L10n.T("Library.ImportedMusic")) ?? CreatePlaylist(L10n.T("Library.ImportedMusic"));
            var added = new List<TrackItem>();
            var existingIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var existingPaths = new Dictionary<string, int>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            for (var i = 0; i < State.Tracks.Count; i++)
            {
                existingIds.TryAdd(State.Tracks[i].Id, i);
                if (State.Tracks[i].ProviderId is null && LocalPath(State.Tracks[i].FilePath) is { } localPath) existingPaths.TryAdd(localPath, i);
            }
            foreach (var track in tracks)
            {
                var old = existingIds.GetValueOrDefault(track.Id, -1);
                if (old < 0 && track.ProviderId is null && LocalPath(track.FilePath) is { } localPath) old = existingPaths.GetValueOrDefault(localPath, -1);
                if (old >= 0)
                {
                    var replaced = State.Tracks[old];
                    // 便携迁移会改变按路径生成的 ID；保留资料库原标识，维护歌单、历史和歌词引用。
                    if (track.Id != replaced.Id)
                    {
                        if (string.IsNullOrWhiteSpace(Lyrics.Read(replaced.Id)) && Lyrics.Read(track.Id) is { Length: > 0 } importedLyrics) Lyrics.Save(replaced.Id, importedLyrics);
                        track.Id = replaced.Id;
                    }
                    track.IsFavorite = replaced.IsFavorite || favorite || playlist?.IsSystem == true;
                    track.LyricsSourcePath = replaced.LyricsSourcePath ?? track.LyricsSourcePath;
                    track.LyricsDisabled = replaced.LyricsDisabled;
                    replaced.PropertyChanged -= TrackChanged; _observedTracks.Remove(replaced);
                    if (CurrentTrack?.Id == track.Id) CurrentTrack = track; replaced.ReleaseArtwork(); State.Tracks[old] = track;
                }
                else { track.IsFavorite = favorite || playlist?.IsSystem == true; added.Add(track); }
            }
            State.Tracks.InsertRange(0, added);
            ObserveTracks();
            InsertPlaylistTracks(LikedPlaylist, tracks.Where(t => t.IsFavorite));
            if (playlist is not null) InsertPlaylistTracks(playlist, tracks);
            RefreshSourceQueue();
            foreach (var path in inputs.Where(Directory.Exists).Select(Path.GetFullPath)) if (!State.MusicFolders.Contains(path)) State.MusicFolders.Add(path);
            ApplyFilter(); Save();
            StatusText = L10n.Format("Common.ImportedSongs", tracks.Count);
            if (_scanner.Warnings.Count > 0) ReportWarning(L10n.Format("Plugins.ImportedSongsFilesCouldNotBeReadCheckTheir", tracks.Count, _scanner.Warnings.Count));
            ViewChanged?.Invoke(this, EventArgs.Empty);
            if (autoplay && tracks.FirstOrDefault() is { } first) await PlayTrackAsync(first, tracks);
            CompleteOperation("playlist.add"); _ = CompleteImportedLyricsAsync(tracks); return tracks;
        }
        catch (OperationCanceledException) { StatusText = L10n.T("Common.ImportCancelledYourLibraryIsUnchanged"); return []; }
        catch (Exception e) { ReportError(L10n.T("Common.ImportFailed"), e); return []; }
        finally { IsBusy = false; cancellation.Dispose(); if (ReferenceEquals(_scanCancellation, cancellation)) _scanCancellation = null; }
    }
    private static string? LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    public Task LoadFolderAsync(string path) => ImportAsync([path]);
    public async Task PlayTemporaryFilesAsync(IEnumerable<string> paths)
    {
        if (RejectLyricsPlaybackChange()) return;
        if (IsBusy) { ReportWarning(L10n.T("Common.AnImportIsInProgressWaitForItTo")); return; }
        var files = paths.Where(p => File.Exists(p) && MusicLibraryScanner.SupportedExtensions.Contains(Path.GetExtension(p))).ToArray();
        if (files.Length == 0) { ReportWarning(L10n.T("Playlists.SelectAPlaylistBeforeAddingMusic")); return; }
        IsBusy = true;
        try
        {
            var tracks = await Task.Run(() => _scanner.ScanPaths(files));
            if (_disposed || tracks.Count == 0) return;
            _temporaryTracks = tracks.ToList(); _countedTemporaryIds.Clear();
            await StartTrackAsync(tracks[0], tracks, "temporary", 0);
            if (IsPlaying) CompleteOperation("player.play");
        }
        catch (Exception error) { ReportError(L10n.T("Playback.CouldNotOpenTemporaryAudio"), error); }
        finally { IsBusy = false; }
    }
    public void CancelImport() { _scanCancellation?.Cancel(); CompleteOperation("import.cancel"); }
    public async Task PlayTrackAsync(TrackItem? track, IEnumerable<TrackItem>? list = null)
    {
        if (RejectLyricsPlaybackChange()) return;
        if (track is null || _disposed) return;
        var sourcePage = NormalizeSourcePage(Page);
        await StartTrackAsync(track, list?.ToArray(), sourcePage, 0);
        if (IsPlaying) CompleteOperation("player.play");
    }
    private async Task StartTrackAsync(TrackItem? track, IEnumerable<TrackItem>? list, string sourcePage, double resumePosition, bool preserveQueueScope = false)
    {
        await StartTrackWithRecoveryAsync(track, list, sourcePage, resumePosition, preserveQueueScope, false);
    }
    [RelayCommand] private void TogglePlayPause()
    {
        _ = TogglePlayPauseAsync();
    }
    private async Task TogglePlayPauseAsync()
    {
        if (_loadingAudio || _switchingOutput) return;
        try { await SetPlayingAsync(!IsPlaying); StatusText = IsPlaying ? L10n.Format("Playback.Playing", ModeDescription) : L10n.T("Playback.PausedPressPlayToResume"); }
        catch (Exception e) { IsPlaying = false; ReportError(L10n.T("Playback.UnableToChangePlaybackState"), e); }
    }
    [RelayCommand] private void PlayPrevious() => PlayRelative(-1, false);
    [RelayCommand] private void PlayNext() => PlayRelative(1, false);
    [RelayCommand] private void StopPlayback()
    {
        Interlocked.Increment(ref _playRequest);
        _failedPlaybackIds.Clear();
        UpdateListeningStatistics();
        try { if (!_loadingAudio) _audio.Stop(); }
        catch (Exception e) { ReportError(L10n.T("Playback.UnableToStopPlayback"), e); }
        IsPlaying = false; _resumePending = false;
        ResetListeningAnchor();
        _updatingPosition = true; PlaybackPosition = 0; _updatingPosition = false;
        Save(); StatusText = L10n.T("Playback.PlaybackStopped");
    }
    [RelayCommand] private void CycleMode() => SetPlayMode(Settings.PlayMode == PlayMode.Shuffle ? PlayMode.RepeatAll : (PlayMode)((int)Settings.PlayMode + 1));
    public void SetPlayMode(PlayMode mode)
    {
        if (RejectLyricsPlaybackChange()) return;
        if (!Enum.IsDefined(mode)) return;
        Settings.PlayMode = mode;
        OnPropertyChanged(nameof(ModeText)); OnPropertyChanged(nameof(ModeIcon)); OnPropertyChanged(nameof(ModeDescription));
        Save(); StatusText = L10n.Format("Playback.PlaybackMode12A803", ModeDescription);
        CompleteOperation("player.mode");
    }
    [RelayCommand] private void ToggleMute() { Volume = Volume == 0 ? _unmutedVolume : RememberVolume(); Save(); }
    private double _unmutedVolume = 80;
    private double RememberVolume() { _unmutedVolume = Volume; return 0; }
    private void PlayRelative(int direction, bool automatic)
        => _ = PlayRelativeSafelyAsync(direction, automatic);
    private async Task PlayRelativeSafelyAsync(int direction, bool automatic)
    {
        try { await PlayRelativeAsync(direction, automatic); }
        catch (Exception error) { ReportError(L10n.T("Playback.UnableToChangePlaybackState"), error); }
    }
    public async Task PlayRelativeAsync(int direction, bool automatic = false)
    {
        if (_lyricsPlaybackLease is not null)
        {
            if (automatic) { await SetPlayingAsync(false); return; }
            RejectLyricsPlaybackChange(); return;
        }
        if (_playingList.Count == 0)
        {
            if (CurrentTrack is not null) { StopPlayback(); ReportWarning(L10n.T("Playlists.TheCurrentSourceHasNoAvailableSongsSelectA")); }
            else if (VisibleTracks.FirstOrDefault() is { } first) await PlayTrackAsync(first);
            return;
        }
        var index = _playingList.FindIndex(t => t.Id == CurrentTrack?.Id);
        if (automatic && Settings.PlayMode == PlayMode.RepeatOne) { await StartTrackAsync(CurrentTrack, _playingList.ToArray(), PlayingSourcePage, 0, true); return; }
        var target = index < 0 ? direction > 0 ? 0 : _playingList.Count - 1 : index + direction;
        if (Settings.PlayMode == PlayMode.Shuffle)
        {
            var selected = _shuffle.Choose(_playingList.Select(t => t.Id), CurrentTrack?.Id);
            target = _playingList.FindIndex(t => t.Id == selected);
        }
        else if (target < 0 || target >= _playingList.Count)
        {
            target = (target + _playingList.Count) % _playingList.Count;
        }
        await StartTrackAsync(_playingList[target], _playingList.ToArray(), PlayingSourcePage, 0, true);
        if (IsPlaying) CompleteOperation(direction < 0 ? "player.previous" : "player.next");
    }
    public void ReloadLyrics()
    {
        LyricLines.Clear(); if (CurrentTrack is null) return;
        try { foreach (var line in LyricsService.Parse(CurrentTrack.LyricsDisabled ? "" : Lyrics.ReadForTrack(CurrentTrack.Id, CurrentTrack.FilePath))) LyricLines.Add(line); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { ReportError(L10n.T("Lyrics.UnableToReadLyricsCheckTheLyricsFolderPermissions"), e); }
    }
    public void ImportLyrics(string path) { if (CurrentTrack is null) throw new InvalidOperationException(L10n.T("Playback.SelectOrPlayASongFirst")); Lyrics.Import(CurrentTrack.Id, path); CurrentTrack.LyricsSourcePath = Path.GetFullPath(path); CurrentTrack.LyricsDisabled = false; ReloadLyrics(); Save(); StatusText = L10n.T("Lyrics.LyricsImportedIntoTheLyricsFolder"); CompleteOperation("lyrics.import"); }
    public void Seek(double seconds)
    {
        if (_loadingAudio || _disposed || CurrentTrack is null) return;
        if (!double.IsFinite(seconds)) { ReportWarning(L10n.T("Playback.PlaybackPositionMustBeAValidNumberOfSeconds")); return; }
        var position = Math.Clamp(seconds, 0, PlaybackDuration);
        try
        {
            // 即使界面显示的值未变化，也向音频后端提交跳转。
            UpdateListeningStatistics();
            if (_audioLoaded) _audio.Position = TimeSpan.FromSeconds(position);
            else _resumePending = true;
            _updatingPosition = true; PlaybackPosition = position; ResetListeningAnchor(); Save();
            CompleteOperation("player.seek");
        }
        catch (Exception error) { ReportError(L10n.T("Playback.UnableToSeek"), error); }
        finally { _updatingPosition = false; }
    }
    public void ApplySettings()
    {
        Settings.Validate();
        if (_lyricsPlaybackLease is { } lease) Settings.PlayMode = lease.SavedMode;
        if (Settings.ValidationWarning is { } settingsWarning) ReportWarning(settingsWarning);
        try
        {
            if (!_loadingAudio && _audio.DeviceName != Settings.DeviceName) _ = SwitchOutputAsync(Settings.DeviceName);
            TrimHistory();
            ApplyLyricsDirectory();
            Save(); SettingsChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("settings.set");
            var backgroundKey = Storage.Root + "\0" + Settings.BackgroundImagePath;
            if (_maintainedBackground != backgroundKey && !Storage.IsMigrating)
            {
                _maintainedBackground = backgroundKey;
                AppBackgroundService.CleanupUnused(Storage, Settings.BackgroundImagePath);
            }
        }
        catch (Exception error) { ReportError(L10n.T("Common.UnableToApplySettings"), error); }
    }
    public void ResetSettings()
    {
        var previous = Settings;
        State.Settings = new AppSettings { LyricsFolder = previous.LyricsFolder, BackupFolder = previous.BackupFolder };
        Volume = Settings.Volume; SetPlayMode(Settings.PlayMode); ApplySettings(); ReloadLyrics(); CompleteOperation("settings.reset");
    }
    private void ApplyLyricsDirectory()
    {
        try
        {
            var folder = string.IsNullOrWhiteSpace(Settings.LyricsFolder) ? Storage.DefaultLyricsFolder : Path.GetFullPath(Settings.LyricsFolder);
            Directory.CreateDirectory(folder); Lyrics.Folder = folder;
            if (!string.IsNullOrWhiteSpace(Settings.LyricsFolder)) Settings.LyricsFolder = folder;
            Lyrics.MirrorFolder = Storage.PendingRoot is { } pending && DataDirectoryService.Contains(Storage.Root, folder) ? DataDirectoryService.Remap(folder, Storage.Root, pending) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Lyrics.Folder = Storage.DefaultLyricsFolder;
            ReportError(L10n.T("Lyrics.TheCustomLyricsFolderIsUnavailableUsingTheDefault"), error);
        }
    }
    public async Task RefreshProviderAsync(PluginManifest plugin)
    {
        EnsureNotMigratingData();
        if (IsBusy) return; IsBusy = true; StatusText = L10n.T("Plugins.LoadingTheSourceLibraryThroughItsPlugin");
        try
        {
            var tracks = await Plugins.LoadCatalogAsync(plugin, Lyrics); if (_disposed) return;
            var favoriteIds = LikedPlaylist.TrackIds.ToHashSet();
            State.Tracks.RemoveAll(t => t.ProviderId == plugin.Id); foreach (var track in tracks) track.IsFavorite = favoriteIds.Contains(track.Id);
            State.Tracks.InsertRange(0, tracks); ObserveTracks(); RefreshSourceQueue();
            // 音源目录同样属于歌单引用模型，否则统计式歌曲浏览不会收录这些曲目。
            var sourceId = "provider:" + plugin.Id;
            var sourcePlaylist = State.Playlists.FirstOrDefault(p => p.Id == sourceId);
            if (sourcePlaylist is null) { sourcePlaylist = new Playlist { Id = sourceId, Name = plugin.Name }; State.Playlists.Add(sourcePlaylist); Playlists.Add(sourcePlaylist); }
            sourcePlaylist.TrackIds = tracks.Select(t => t.Id).ToList(); RefreshSourceQueue();
            Save(); ApplyFilter(); ViewChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.Format("Common.SourceUpdatedSongs", tracks.Count); CompleteOperation("plugins.refresh");
        }
        catch (Exception e) { ReportError(L10n.T("Common.UnableToLoadTheSourceCheckItsConfigurationAnd"), e); }
        finally { IsBusy = false; }
    }
    public void DisablePlugin(PluginManifest plugin)
    {
        EnsureNotMigratingData();
        if (CurrentTrack?.ProviderId == plugin.Id) StopPlayback(); Plugins.SetEnabled(plugin, false);
        _playingList.RemoveAll(t => t.ProviderId == plugin.Id); ApplyFilter(); SettingsChanged?.Invoke(this, EventArgs.Empty); StatusText = L10n.T("Plugins.PluginDisabledAndItsProcessStopped");
    }
    public async Task ImportM3uAsync(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')).ToArray();
        if (lines.Length > 50000) throw new InvalidDataException(L10n.T("Playlists.APlaylistImportCannotExceedSongs"));
        var entries = lines.Where(l => !Uri.TryCreate(l, UriKind.Absolute, out var uri) || uri.IsFile).Select(l => Uri.TryCreate(l, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : Path.IsPathRooted(l) ? l : Path.Combine(Path.GetDirectoryName(path)!, l)).ToArray();
        var tracks = await ImportAsync(entries); if (tracks.Count == 0) return;
        var playlist = CreatePlaylist(Path.GetFileNameWithoutExtension(path)); var ids = tracks.ToDictionary(t => t.FilePath, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var entry in entries) if (ids.TryGetValue(Path.GetFullPath(entry), out var track)) playlist.TrackIds.Add(track.Id);
        playlist.TrackIds = playlist.TrackIds.Distinct().ToList(); Save(); Navigate("playlist:" + playlist.Id); CompleteOperation("playlist.import");
    }
    public void ExportM3u(string path)
    {
        AppStorage.AtomicWrite(path, "#EXTM3U\n" + string.Join('\n', VisibleTracks.Where(t => t.ProviderId is null).Select(t => $"#EXTINF:{(int)t.DurationSeconds},{t.Artist} - {t.Title}\n{t.FilePath}")) + "\n");
        CompleteOperation("playlist.export");
    }
    private void PlaybackEnded(object? sender, EventArgs e)
    {
        var request = Interlocked.Read(ref _playRequest);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _loadingAudio || !_audioLoaded || request != Interlocked.Read(ref _playRequest)) return;
            if (e is AudioPlaybackEndedEventArgs ended && ended.PlaybackGeneration != 0 && ended.PlaybackGeneration != _audio.PlaybackGeneration) return;
            try { if (_audio.IsPlaying) return; UpdateListeningStatistics(); IsPlaying = false; _failedPlaybackIds.Clear(); PlayRelative(1, true); }
            catch (Exception error) { ReportError(L10n.T("Playback.UnableToAdvanceToTheNextSong"), error); }
        });
    }
    private void PlaybackFailed(object? sender, AudioPlaybackErrorEventArgs e)
    {
        var request = Interlocked.Read(ref _playRequest);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _loadingAudio || request != Interlocked.Read(ref _playRequest)) return;
            if (e.PlaybackGeneration != 0 && e.PlaybackGeneration != _audio.PlaybackGeneration) return;
            try { if (_audio.IsPlaying) return; }
            catch (Exception error) { AppLog.Warning("Audio", "旧错误事件校验失败", error); }
            var canRecover = _audioLoaded;
            UpdateListeningStatistics();
            _audioLoaded = false; IsPlaying = false; _resumePending = CurrentTrack is not null;
            ReportError(L10n.T("Playback.PlaybackFailed987DF3"), new InvalidOperationException(e.Message));
            if (canRecover) RecoverRuntimeFailure();
        });
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (_loadingAudio || _disposed) return;
        try
        {
            UpdateListeningStatistics();
            _updatingPosition = true;
            if (_audioLoaded && !IsSeeking && _audio.Duration.TotalSeconds > 0) { PlaybackPosition = _audio.Position.TotalSeconds; PlaybackDuration = Math.Max(1, _audio.Duration.TotalSeconds); }
            IsPlaying = _audioLoaded && _audio.IsPlaying;
            if (IsPlaying && PlaybackPosition >= _recoveryStartPosition + 3) _failedPlaybackIds.Clear();
            if (DateTimeOffset.UtcNow - _lastSave > TimeSpan.FromSeconds(15)) { Save(playbackOnly: true); _lastSave = DateTimeOffset.UtcNow; }
        }
        catch (Exception error) { _audioLoaded = false; IsPlaying = false; _resumePending = CurrentTrack is not null; ReportError(L10n.T("Playback.UnableToReadPlaybackState"), error); }
        finally { _updatingPosition = false; }
    }
    /// <summary>焦点恢复立即同步后端时钟，不等待节流计时器或异步页面重建。</summary>
    public void RefreshPlaybackState() => Tick(this, EventArgs.Empty);
    public void Save(bool playbackOnly = false)
    {
        State.LastTrackId = CurrentTrack?.Id; State.LastPosition = PlaybackPosition;
        State.LastTemporaryFile = PlayingSourcePage == "temporary" && CurrentTrack?.ProviderId is null ? CurrentTrack?.FilePath : null;
        State.LastTemporaryCounted = PlayingSourcePage == "temporary" && CurrentTrack is not null && _countedTemporaryIds.Contains(CurrentTrack.Id);
        State.LastSourcePage = PlayingSourcePage; State.LastPlaylistId = PlayingPlaylistId;
        try { Storage.Save(State, playbackOnly); } catch (Exception e) { ReportError(L10n.T("Plugins.UnableToSaveSettingsCheckDataFolderPermissionsAnd"), e); }
    }
    public void ReportError(string action, Exception exception)
    {
        ++ErrorRevision;
        // 用户可见状态不得包含音源 URL 或会话令牌。
        action = L10n.T(action);
        StatusText = AppLog.Redact(L10n.T(action) + ": " + L10n.T(exception is InvalidDataException or InvalidOperationException or FileNotFoundException ? exception.Message : exception is OperationCanceledException ? L10n.T("Common.TheRequestTimedOutOrWasCancelledTryAgain") : L10n.T("Plugins.TheOperationFailedCheckPermissionsFilesOrConnectionsAnd")));
        AppLog.Error("Client", action, exception); NotifyUser(L10n.T(action), StatusText, false);
        OperationCompleted?.Invoke(this, NonetMusicPlayer.Core.Commands.CommandResults.Failed("operation", StatusText));
    }
    public void ReportWarning(string message)
    {
        StatusText = AppLog.Redact(L10n.T(message)); AppLog.Warning("Client", message); NotifyUser(L10n.T("Common.Notice"), StatusText, true);
    }
    private void NotifyUser(string title, string message, bool warning)
    {
        if (UserNotification is null)
        {
            if (_startupNotifications.Count < 16 && !_startupNotifications.Any(item => item.Message == message)) _startupNotifications.Add(new(title, message, warning));
            return;
        }
        if (_lastNotification == message && DateTimeOffset.UtcNow - _lastNotificationTime < TimeSpan.FromSeconds(30)) return;
        _lastNotification = message; _lastNotificationTime = DateTimeOffset.UtcNow;
        try { UserNotification?.Invoke(this, new(title, message, warning)); }
        catch (Exception error) { AppLog.Error("UI", "显示错误提示失败", error); }
    }
    public void PublishStartupNotifications()
    {
        if (UserNotification is null) return;
        var pending = _startupNotifications.ToArray(); _startupNotifications.Clear();
        foreach (var notification in pending) NotifyUser(notification.Title, notification.Message, notification.IsWarning);
    }
    private static string FormatTime(double seconds) => TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public void Dispose()
    {
        if (_disposed) return; _lyricsPlaybackLease?.Dispose(); UpdateListeningStatistics(); _disposed = true; _lyricsSearchLifetime.Cancel(); Interlocked.Increment(ref _playRequest); _scanCancellation?.Cancel(); _timer.Stop(); Save();
        _audio.PlaybackStopped -= PlaybackEnded; _audio.PlaybackFailed -= PlaybackFailed;
        _audio.OutputDeviceChanged -= OutputChanged; _audio.OutputDevicesChanged -= DevicesChanged;
        try { _audio.Dispose(); Plugins.Dispose(); } catch (Exception error) { AppLog.Error("Shutdown", "关闭播放资源失败", error); }
        foreach (var track in _observedTracks) track.PropertyChanged -= TrackChanged;
        foreach (var track in State.Tracks) track.ReleaseArtwork(); foreach (var playlist in State.Playlists) playlist.ReleaseArtwork();
        AppBackgroundService.Clear();
        AppBackgroundService.CleanupUnused(Storage, Settings.BackgroundImagePath, previousToKeep: 0);
    }
}
