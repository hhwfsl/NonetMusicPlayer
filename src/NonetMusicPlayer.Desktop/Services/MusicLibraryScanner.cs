using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>桌面扫描适配器：标签、封面及同名歌词交由共享核心读取，界面只负责模型和警告。</summary>
public sealed class MusicLibraryScanner
{
    public static readonly HashSet<string> SupportedExtensions = new(MusicFileReader.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
    public AppStorage Storage { get; }
    public LyricsService Lyrics { get; }
    public List<string> Warnings { get; } = [];
    private readonly object _warningGate = new();
    private void Warning(string text) { lock (_warningGate) Warnings.Add(text); }
    public MusicLibraryScanner(AppStorage? storage = null) { Storage = storage ?? new(); Lyrics = new(Storage.DefaultLyricsFolder); }
    public IReadOnlyList<TrackItem> Scan(string path, CancellationToken cancellationToken = default) => ScanPaths([path], cancellationToken);
    public IReadOnlyList<TrackItem> ScanPaths(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        lock (_warningGate) Warnings.Clear();
        var files = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var pending = new Stack<string>(paths.Select(Path.GetFullPath));
        while (pending.TryPop(out var path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path)) { if (SupportedExtensions.Contains(Path.GetExtension(path))) files.Add(path); continue; }
                if (!Directory.Exists(path)) { Warning(L10n.Format("Library.InputNotFound", path)); continue; }
                foreach (var file in Directory.EnumerateFiles(path)) if (SupportedExtensions.Contains(Path.GetExtension(file))) files.Add(file);
                foreach (var child in Directory.EnumerateDirectories(path)) if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Warning(L10n.Format("Library.InputUnavailable", path)); }
        }
        var tracks = new System.Collections.Concurrent.ConcurrentBag<TrackItem>();
        // 标签与封面最多使用四个工作项，避免磁盘争用及解码内存峰值。
        Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) }, file =>
        {
            try { tracks.Add(ReadTrack(file)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Warning(L10n.Format("Library.InputUnreadable", file)); }
        });
        return tracks.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.Title).ToArray();
    }
    public TrackItem ReadTrack(string path)
    {
        var reader = new MusicFileReader(Storage.ArtworkFolder, Lyrics.Folder, StableId);
        reader.Warning += (_, message) => Warning(message);
        var metadata = reader.Read(path);
        if (Lyrics.MirrorFolder is not null && Lyrics.ExistingPath(metadata.Id) is { } lyric) Lyrics.Import(metadata.Id, lyric);
        return new(metadata.Id, metadata.Title, metadata.Artist, metadata.Album, metadata.FilePath, metadata.Extension, metadata.FileSize)
        { DurationSeconds = metadata.DurationSeconds, CoverPath = metadata.CoverPath, LyricsSourcePath = new[] { ".lrc", ".txt" }.Select(ext => Path.ChangeExtension(path, ext)).FirstOrDefault(File.Exists) };
    }
    public static string StableId(string value) => MusicFileReader.StableId(value);
}
