using System.Diagnostics;
using System.Text.Json;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Playback;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Core.Runtime;

/// <summary>无窗口播放器会话，负责 CLI 的独立播放、队列、持久化和设备恢复。</summary>
public sealed partial class HeadlessPlayerSession : IPlayerCommandBackend, IAsyncDisposable
{
    private readonly IAudioPlayer _audio;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly FileStream _dataLease;
    private readonly Timer _timer;
    private readonly WeightedShuffleSelector _shuffle = new();
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private List<StoredMusicTrack> _queue = [];
    private bool _loaded, _disposed;
    private long _listeningAnchor = Stopwatch.GetTimestamp();
    private double _sessionSeconds;
    private bool _sessionCounted;
    private DateTimeOffset _lastSaved = DateTimeOffset.UtcNow;
    public bool HasWindow => false;
    public string DataDirectory { get; }
    public string ArtworkDirectory => Path.Combine(DataDirectory, "Artwork");
    public string LyricsDirectory => string.IsNullOrWhiteSpace(State.Settings.LyricsFolder) ? Path.Combine(DataDirectory, "Lyrics") : Path.GetFullPath(State.Settings.LyricsFolder);
    public HeadlessPlayerState State { get; private set; }
    public StoredMusicTrack? Current { get; private set; }
    public bool ExitRequested { get; private set; }
    public event EventHandler<CommandResult>? ResultPublished;
    public event EventHandler<double>? PluginDownloadProgress;
    public HeadlessPluginHost Plugins { get; }

    public HeadlessPlayerSession(string dataDirectory, IAudioPlayer? audio = null)
    {
        DataDirectory = Path.GetFullPath(dataDirectory); PluginPathPolicy.RejectLinkedAncestors(DataDirectory);
        // 独立宿主绝不能接管桌面数据；在创建目录、锁或日志之前先拒绝外来状态。
        if (File.Exists(Path.Combine(DataDirectory, "state.json")) || File.Exists(Path.Combine(DataDirectory, "library.db")))
            throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.SeparateData"));
        Directory.CreateDirectory(DataDirectory);
        _dataLease = new FileStream(Path.Combine(DataDirectory, ".session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Directory.CreateDirectory(ArtworkDirectory); Directory.CreateDirectory(Path.Combine(DataDirectory, "Lyrics"));
            State = LoadState(); State.Settings.Validate(); LocalizationCatalog.SetLanguage(State.Settings.Language);
            AppLog.Initialize(DataDirectory);
            Plugins = new(Path.Combine(DataDirectory, "Plugins"));
            _audio = audio ?? new NativeAudioPlayer(probeWhileIdle: false); _audio.Volume = (float)(State.Settings.Volume / 100); _audio.DeviceName = State.Settings.DeviceName;
            Current = State.Tracks.Concat(State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == State.LastTrackId);
            _queue = State.LastPlaylistId is { } id && State.Playlists.FirstOrDefault(p => p.Id == id) is { } playlist ? PlaylistTracks(playlist).ToList() : Current is null ? [] : [Current];
            _audio.PlaybackStopped += PlaybackEnded; _audio.PlaybackFailed += PlaybackFailed; _audio.OutputDeviceChanged += OutputChanged;
            _timer = new Timer(_ => _ = TickAsync(), null, 500, 500);
        }
        catch { _dataLease.Dispose(); throw; }
    }
    private string StatePath => Path.Combine(DataDirectory, "cli-state.json");
    private HeadlessPlayerState LoadState()
    {
        foreach (var path in new[] { StatePath, StatePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try { return ParseState(path); }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
            {
                if (path == StatePath) File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyy-MM-dd-HHmmssfff"), false);
            }
        }
        return new HeadlessPlayerState();
    }
    private static HeadlessPlayerState ParseState(string path)
    {
        if (new FileInfo(path).Length > 100 * 1024 * 1024) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.StateTooLarge"));
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            if (!document.RootElement.TryGetProperty("host", out var host) || host.GetString() != "cli")
                throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ForeignBackup"));
        var state = JsonSerializer.Deserialize<HeadlessPlayerState>(File.ReadAllText(path), CoreJson.Options) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.EmptyState"));
        if (state.SchemaVersion != 1 || state.Tracks is null || state.Playlists is null || state.Settings is null || state.History is null || state.ListeningEntries is null || state.RecentTemporaryTracks is null) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.InvalidState"));
        state.Settings.Validate();
        if (state.Tracks.Any(t => t is null || string.IsNullOrWhiteSpace(t.Id)) || state.Playlists.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || p.TrackIds is null)) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.InvalidLibrary"));
        if (state.Tracks.Concat(state.RecentTemporaryTracks).Any(t => t.Metadata is null || t.Id.Length > 500 || t.ProviderId is null && !System.Text.RegularExpressions.Regex.IsMatch(t.Id, "^[A-Fa-f0-9]{24}$"))) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.InvalidIdentity"));
        state.Tracks = state.Tracks.DistinctBy(t => t.Id).ToList(); state.Playlists = state.Playlists.DistinctBy(p => p.Id).ToList();
        if (!state.Playlists.Any(p => p.Id == "liked")) state.Playlists.Insert(0, new() { Id = "liked", Name = "Playlists.LikedSongs" });
        state.LastPosition = double.IsFinite(state.LastPosition) ? Math.Max(0, state.LastPosition) : 0; return state;
    }
    private void Save()
    {
        if (_loaded) State.LastPosition = Math.Max(0, _audio.Position.TotalSeconds);
        State.LastTrackId = Current?.Id;
        AtomicFile.Write(StatePath, JsonSerializer.Serialize(State, CoreJson.Options), true); _lastSaved = DateTimeOffset.UtcNow;
    }
    private IEnumerable<StoredMusicTrack> PlaylistTracks(StoredPlaylist playlist)
    {
        var tracks = State.Tracks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        return playlist.TrackIds.Select(id => tracks.GetValueOrDefault(id)).OfType<StoredMusicTrack>().Where(Available);
    }
    private bool Available(StoredMusicTrack track) => track.ProviderId is null || Plugins.Installed.Any(p => p.Id == track.ProviderId && p.Enabled);
    private async Task StartAsync(StoredMusicTrack? track, double position, CancellationToken token, bool newSession = true)
    {
        if (track is null) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.NoTrack"));
        AccountListening(); _loaded = false;
        var candidates = _queue.Where(Available).DistinctBy(t => t.Id).ToArray();
        if (candidates.Length == 0) candidates = [track];
        while (track is not null && !_failed.Contains(track.Id))
        {
            try
            {
                var source = track.ProviderId is null ? track.Metadata.FilePath : await Plugins.ResolveAsync(track.ProviderId, track.ProviderTrackId!, token);
                await _audio.LoadAsync(source, token); _audio.Position = TimeSpan.FromSeconds(Math.Clamp(position, 0, Math.Max(0, _audio.Duration.TotalSeconds)));
                _audio.Play(); _loaded = true; Current = track; State.LastPosition = _audio.Position.TotalSeconds;
                if (newSession) { _sessionSeconds = 0; _sessionCounted = false; AddHistory(track); }
                _listeningAnchor = Stopwatch.GetTimestamp(); Save(); return;
            }
            catch (AudioOutputUnavailableException error)
            {
                Current = track; State.LastPosition = position; _audio.Pause(); Publish(CommandResults.Failed("player.device", AppLog.Redact(error.Message))); return;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _failed.Add(track.Id); AppLog.Warning("Audio", "CLI audio failed", error);
                Publish(CommandResults.Failed("player.play", LocalizationCatalog.Format("Commands.AudioFailed", track.Metadata.Title)));
                var usable = candidates.Where(t => !_failed.Contains(t.Id)).ToArray();
                track = State.Settings.PlayMode == PlaybackMode.Shuffle
                    ? PickShuffled(usable, track.Id)
                    : Following(candidates, track.Id, 1, usable.Select(t => t.Id).ToHashSet(StringComparer.Ordinal));
                position = 0;
            }
        }
        _audio.Stop(); _loaded = false; Publish(CommandResults.Failed("player.play", NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.NoPlayableAudio")));
    }
    private static StoredMusicTrack? Following(IReadOnlyList<StoredMusicTrack> queue, string? current, int direction, HashSet<string>? usable = null)
    {
        var index = queue.ToList().FindIndex(t => t.Id == current);
        for (var offset = 1; offset <= queue.Count; offset++)
        {
            var target = queue[(index + direction * offset % queue.Count + queue.Count) % queue.Count];
            if (usable is null || usable.Contains(target.Id)) return target;
        }
        return null;
    }
    private async Task RelativeAsync(int direction, bool automatic, CancellationToken token)
    {
        AccountListening(); var queue = _queue.Where(Available).DistinctBy(t => t.Id).ToArray();
        if (queue.Length == 0) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.EmptyQueue"));
        _failed.Clear();
        var next = automatic && State.Settings.PlayMode == PlaybackMode.RepeatOne ? Current
            : State.Settings.PlayMode == PlaybackMode.Shuffle ? PickShuffled(queue, Current?.Id)
            : Following(queue, Current?.Id, direction);
        if (next is not null) await StartAsync(next, 0, token);
    }
    private StoredMusicTrack? PickShuffled(IReadOnlyList<StoredMusicTrack> tracks, string? current)
    {
        var id = _shuffle.Choose(tracks.Select(t => t.Id), current);
        return tracks.FirstOrDefault(t => t.Id == id);
    }
    private async Task TickAsync()
    {
        if (_disposed || !await _gate.WaitAsync(0)) return;
        try { AccountListening(); if (_loaded && DateTimeOffset.UtcNow - _lastSaved >= TimeSpan.FromSeconds(15)) Save(); }
        catch (Exception error) { AppLog.Warning("CLI", "CLI state tick failed", error); }
        finally { _gate.Release(); }
    }
    private void PlaybackEnded(object? sender, EventArgs args) => _ = OnEndedAsync(args);
    private async Task OnEndedAsync(EventArgs args)
    {
        if (_disposed) return; await _gate.WaitAsync();
        try
        {
            if (_disposed || !_loaded || args is AudioPlaybackEndedEventArgs ended && ended.PlaybackGeneration != 0 && ended.PlaybackGeneration != _audio.PlaybackGeneration || _audio.IsPlaying) return;
            AccountListening(); CountSession(); await RelativeAsync(1, true, CancellationToken.None); Publish(CommandResults.Completed("player.next"));
        }
        catch (Exception error) { AppLog.Warning("CLI", "Natural advance failed", error); Publish(CommandResults.Failed("player.next", AppLog.Redact(error.Message))); }
        finally { _gate.Release(); }
    }
    private void PlaybackFailed(object? sender, AudioPlaybackErrorEventArgs args) => _ = OnFailedAsync(args);
    private async Task OnFailedAsync(AudioPlaybackErrorEventArgs args)
    {
        if (_disposed) return; await _gate.WaitAsync();
        try
        {
            if (_disposed || !_loaded || args.PlaybackGeneration != 0 && args.PlaybackGeneration != _audio.PlaybackGeneration) return;
            AccountListening(); if (Current is null) return;
            _failed.Add(Current.Id); _loaded = false;
            var next = _queue.FirstOrDefault(t => Available(t) && !_failed.Contains(t.Id));
            if (next is not null) await StartAsync(next, 0, CancellationToken.None); else { _audio.Stop(); Publish(CommandResults.Failed("player.play", NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.NoPlayableAudio"))); }
        }
        catch (Exception error) { AppLog.Warning("CLI", "Playback recovery failed", error); }
        finally { _gate.Release(); }
    }
    private void OutputChanged(object? sender, AudioOutputChangedEventArgs args) => _ = OutputChangedAsync(args);
    private async Task OutputChangedAsync(AudioOutputChangedEventArgs args)
    {
        if (_disposed) return; await _gate.WaitAsync();
        try
        {
            if (_disposed || args.PlaybackGeneration != 0 && args.PlaybackGeneration != _audio.PlaybackGeneration) return;
            AccountListening(); _audio.Pause(); State.Settings.DeviceName = args.DeviceName; State.LastPosition = args.Position;
            _listeningAnchor = Stopwatch.GetTimestamp(); Save(); Publish(CommandResults.Completed("player.device")); Publish(CommandResults.Completed("player.pause"));
        }
        catch (Exception error) { AppLog.Warning("CLI", "Output recovery failed", error); }
        finally { _gate.Release(); }
    }
    private void Publish(CommandResult result) => ResultPublished?.Invoke(this, result);
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; await _timer.DisposeAsync(); await _gate.WaitAsync();
        try { AccountListening(); Save(); _audio.PlaybackStopped -= PlaybackEnded; _audio.PlaybackFailed -= PlaybackFailed; _audio.OutputDeviceChanged -= OutputChanged; _audio.Dispose(); Plugins.Dispose(); }
        finally { _dataLease.Dispose(); _gate.Release(); }
    }
}
