using System.Text;
using NonetMusicPlayer.Core.Diagnostics;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    // 接收设备的远端歌词缓存独立于用户导入目录，不继承旧版的本机覆盖或禁用状态。
    private string RemoteLyricPath(StoredMusicTrack track) => Path.Combine(DataDirectory, "LanCache", "Lyrics", LyricFileId(track) + ".lrc");
    /// <summary>播放时按稳定身份补封面和歌词；可选元数据失败不能阻止旧音源继续播放。</summary>
    private async Task PrepareTrackResourcesAsync(StoredMusicTrack track, CancellationToken token)
    {
        try
        {
            if (track.ProviderId?.StartsWith("lan:", StringComparison.Ordinal) == true && _lanPeers.TryGetValue(track.ProviderId[4..], out var peer))
            {
                var metadata = peer.Catalog.Tracks.FirstOrDefault(t => t.Id == track.ProviderTrackId); var lyric = RemoteLyricPath(track);
                if (metadata?.Lyrics == true) { Directory.CreateDirectory(Path.GetDirectoryName(lyric)!); Persistence.AtomicFile.Write(lyric, Encoding.UTF8.GetString(await peer.ReadAssetAsync("lyrics", track.ProviderTrackId!, token))); }
                else if (File.Exists(lyric)) File.Delete(lyric);
                if (metadata?.Artwork == true && !File.Exists(track.Metadata.CoverPath))
                {
                    var cover = Path.Combine(ArtworkDirectory, LyricFileId(track) + ".image"); await File.WriteAllBytesAsync(cover, await peer.ReadAssetAsync("artwork", track.ProviderTrackId!, token), token); track.Metadata = track.Metadata with { CoverPath = cover };
                }
            }
            else if (track.ProviderId is { } provider && await Plugins.DescribeMusicAsync(provider, track.ProviderTrackId!, token) is { } description)
            {
                var enriched = StoreSourceTrack(provider, description); if (enriched.Metadata.CoverPath is not null) track.Metadata = track.Metadata with { CoverPath = enriched.Metadata.CoverPath };
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { AppLog.Warning("MusicSource", "CLI 可选封面/歌词暂不可用，继续解析播放地址"); }
    }
}
