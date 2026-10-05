using System.Text;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Core.Library;

/// <summary>在旁路副本写标签，校验后原子替换；出现错误不覆盖源文件，并保留完整恢复备份。</summary>
public static class AudioLyricsTagWriter
{
    private static readonly HashSet<string> Supported = new([".mp3", ".flac", ".m4a", ".m4b", ".ogg", ".opus", ".ape", ".wma"], StringComparer.OrdinalIgnoreCase);
    public static async Task<string> EmbedAsync(string audioPath, string lyrics, string backupDirectory, CancellationToken cancellationToken = default, Func<bool>? allowCommit = null)
    {
        var source = Path.GetFullPath(audioPath); var extension = Path.GetExtension(source);
        if (!Supported.Contains(extension) || !File.Exists(source) || Encoding.UTF8.GetByteCount(lyrics) is 0 or > 2_000_000) throw new InvalidDataException("LyricsSearch.UnsupportedAudio");
        PluginPathPolicy.RejectLinkedAncestors(Path.GetDirectoryName(source)!); PluginPathPolicy.RejectLinkedAncestors(backupDirectory);
        var original = new FileInfo(source); var size = original.Length; var modified = original.LastWriteTimeUtc;
        if ((original.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked audio source is not supported");
        if ((original.Attributes & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("LyricsSearch.ReadOnlyAudio");
        Directory.CreateDirectory(backupDirectory);
        var suffix = Guid.NewGuid().ToString("N"); var title = Path.GetFileNameWithoutExtension(source); if (title.Length > 80) title = title[..80];
        var backup = Path.Combine(backupDirectory, title + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + suffix + extension);
        var temporary = Path.Combine(Path.GetDirectoryName(source)!, ".nonet-lyrics-" + suffix + extension);
        try
        {
            await CopyAsync(source, backup, cancellationToken); await CopyAsync(backup, temporary, cancellationToken);
            await Task.Run(() => { using var audio = TagLib.File.Create(temporary); audio.Tag.Lyrics = lyrics; audio.Save(); using var verify = TagLib.File.Create(temporary); if (verify.Tag.Lyrics != lyrics) throw new InvalidDataException("LyricsSearch.UnsupportedAudio"); }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (allowCommit is not null && !allowCommit()) throw new OperationCanceledException("Audio tag permission revoked");
            original.Refresh();
            if (original.Length != size || original.LastWriteTimeUtc != modified) throw new IOException("LyricsSearch.SourceChanged");
            File.Replace(temporary, source, destinationBackupFileName: null, ignoreMetadataErrors: true); return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task CopyAsync(string source, string target, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await input.CopyToAsync(output, token); await output.FlushAsync(token);
    }
}
