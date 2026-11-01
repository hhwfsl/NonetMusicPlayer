using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>次数相同按最近访问排序；从未访问的歌单使用既有顺序补足，不伪造历史。</summary>
    public IReadOnlyList<Playlist> MostVisitedPlaylists(int limit = 4) => Playlists
        .OrderByDescending(p => State.PlaylistVisits.GetValueOrDefault(p.Id)?.Count ?? 0)
        .ThenByDescending(p => State.PlaylistVisits.GetValueOrDefault(p.Id)?.LastVisitedUtc ?? DateTimeOffset.MinValue)
        .Take(Math.Clamp(limit, 0, 100)).ToArray();

    private void RecordPlaylistVisit(string page)
    {
        if (!page.StartsWith("playlist:", StringComparison.Ordinal)) return;
        var id = page[9..];
        if (!State.Playlists.Any(p => p.Id == id)) return;
        if (!State.PlaylistVisits.TryGetValue(id, out var visit)) State.PlaylistVisits[id] = visit = new();
        if (visit.Count < long.MaxValue) visit.Count++;
        visit.LastVisitedUtc = DateTimeOffset.UtcNow;
        // 访问属于小型状态元数据，不扫描和重新保存整个曲目库。
        Save(playbackOnly: true);
    }
}
