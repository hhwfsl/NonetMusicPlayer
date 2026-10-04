namespace NonetMusicPlayer.Core.Runtime;

public sealed record TuiItem(string Id, string Title, string Detail);
public sealed record TuiSnapshot(string Title, string Artist, bool Playing, double Position, double Duration, double Volume, string Mode,
    IReadOnlyList<TuiItem> Music, IReadOnlyList<TuiItem> Playlists, IReadOnlyList<TuiItem> Recent, IReadOnlyList<TuiItem> Plugins, double ListeningSeconds)
{
    public string? CurrentId { get; init; }
    public IReadOnlySet<string> Favorites { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, IReadOnlyList<TuiItem>> PlaylistTracks { get; init; } = new Dictionary<string, IReadOnlyList<TuiItem>>();
    public System.Text.Json.Nodes.JsonObject Settings { get; init; } = new();
}

public sealed partial class HeadlessPlayerSession
{
    /// <summary>在业务锁内复制纯文本视图，不直接枚举正在被命令/播放线程修改的集合。</summary>
    public async Task<TuiSnapshot> CaptureTuiAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var references = State.Playlists.SelectMany(p => p.TrackIds).ToHashSet(StringComparer.Ordinal);
            TuiItem Item(StoredMusicTrack t) => new(t.Id, t.Metadata.Title, t.Metadata.Artist + " · " + t.Metadata.Album);
            var index = State.Tracks.Concat(State.RecentTemporaryTracks).DistinctBy(t => t.Id).ToDictionary(t => t.Id);
            return new(Current?.Metadata.Title ?? "—", Current?.Metadata.Artist ?? "", _loaded && _audio.IsPlaying, _loaded ? _audio.Position.TotalSeconds : State.LastPosition,
                Current?.Metadata.DurationSeconds ?? 0, State.Settings.Volume, State.Settings.PlayMode.ToString(), State.Tracks.Where(t => references.Contains(t.Id)).Select(Item).ToArray(),
                State.Playlists.Select(p => new TuiItem(p.Id, Localization.LocalizationCatalog.Get(p.Name), p.TrackIds.Count.ToString())).ToArray(),
                State.History.Where(index.ContainsKey).Select(id => Item(index[id])).ToArray(),
                Plugins.Installed.Select(p => new TuiItem(p.Id, p.Name, p.Version + " · " + (p.Enabled ? "ON" : "OFF"))).ToArray(), State.ListeningEntries.Sum(e => e.Seconds))
            {
                CurrentId = Current?.Id,
                Favorites = State.Playlists.FirstOrDefault(p => p.Id == "liked")?.TrackIds.ToHashSet(StringComparer.Ordinal) ?? [],
                PlaylistTracks = State.Playlists.ToDictionary(p => p.Id, p => (IReadOnlyList<TuiItem>)p.TrackIds.Where(index.ContainsKey).Select(id => Item(index[id])).ToArray()),
                Settings = System.Text.Json.JsonSerializer.SerializeToNode(State.Settings, Persistence.CoreJson.Options)!.AsObject()
            };
        }
        finally { _gate.Release(); }
    }
}
