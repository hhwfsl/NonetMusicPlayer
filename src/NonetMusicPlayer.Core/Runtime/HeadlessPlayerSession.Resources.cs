using System.Text;
using NonetMusicPlayer.Core.Diagnostics;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    /// <summary>播放时按稳定身份补封面和歌词；可选元数据失败不能阻止旧音源继续播放。</summary>
    private async Task PrepareTrackResourcesAsync(StoredMusicTrack track, CancellationToken token)
    {
        try
        {
            if (track.ProviderId?.StartsWith("lan:", StringComparison.Ordinal) == true && _lanPeers.TryGetValue(track.ProviderId[4..], out var peer))
            {
                var metadata = peer.Catalog.Tracks.FirstOrDefault(t => t.Id == track.ProviderTrackId); var lyric = Path.Combine(LyricsDirectory, LyricFileId(track) + ".lrc");
                if (metadata?.Lyrics == true && !State.DisabledLyrics.Contains(track.Id) && !File.Exists(lyric)) { Directory.CreateDirectory(LyricsDirectory); await File.WriteAllTextAsync(lyric, Encoding.UTF8.GetString(await peer.ReadAssetAsync("lyrics", track.ProviderTrackId!, token)), token); }
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
