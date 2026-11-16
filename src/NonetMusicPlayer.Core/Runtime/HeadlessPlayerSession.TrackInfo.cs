using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    /// <summary>CLI 信息保留原 JSON 字段，仅按本机已有标签追加音频/文件信息，不额外请求云端。</summary>
    private Task<JsonObject> ReadTrackInfoAsync(StoredMusicTrack track, CancellationToken token) => Task.Run(() => {
        token.ThrowIfCancellationRequested();
        var result = TrackJson(track);
        result["providerTrackId"] = track.ProviderTrackId; result["format"] = track.Metadata.Extension;
        result["fileSize"] = track.Metadata.FileSize; result["readOnly"] = IsLanTrack(track);
        if (IsLanTrack(track) && _lanPeers.TryGetValue(track.ProviderId![4..], out var peer)) result["sourceDevice"] = peer.Name;
        if (track.ProviderId is not null || !File.Exists(track.Metadata.FilePath)) return result;
        try {
            result["modifiedAt"] = new FileInfo(track.Metadata.FilePath).LastWriteTimeUtc.ToString("O");
            using var file = TagLib.File.Create(track.Metadata.FilePath);
            void Text(string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) result[name] = value; }
            void Number(string name, long value) { if (value > 0) result[name] = value; }
            Text("albumArtist", string.Join(" / ", file.Tag.AlbumArtists)); Text("genre", string.Join(" / ", file.Tag.Genres)); Text("composer", string.Join(" / ", file.Tag.Composers));
            Number("year", file.Tag.Year); Number("trackNumber", file.Tag.Track); Number("trackCount", file.Tag.TrackCount); Number("discNumber", file.Tag.Disc); Number("discCount", file.Tag.DiscCount);
            Number("bitrateKbps", file.Properties.AudioBitrate); Number("sampleRate", file.Properties.AudioSampleRate); Number("bitsPerSample", file.Properties.BitsPerSample); Number("channels", file.Properties.AudioChannels);
            Text("codec", file.Properties.Description);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or TagLib.UnsupportedFormatException or TagLib.CorruptFileException) { }
        return result;
    }, token);
}
