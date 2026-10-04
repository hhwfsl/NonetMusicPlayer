using System.Security.Cryptography;
using System.Text;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayer.Core.Library;

/// <summary>不包含界面对象的音乐元数据，供桌面和 CLI 共用。</summary>
public sealed record MusicMetadata(string Id, string Title, string Artist, string Album, string FilePath, string Extension, long FileSize, double DurationSeconds, string? CoverPath);

/// <summary>按文件读取标签和受限大小的封面；不会将整首音频读入内存。</summary>
public sealed class MusicFileReader(string artworkDirectory, string lyricsDirectory, Func<string, string>? lyricFileName = null)
{
    public static IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".aiff", ".aif", ".ape", ".alac", ".wv" };
    public event EventHandler<string>? Warning;
    public static string StableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 12));

    public MusicMetadata Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || !SupportedExtensions.Contains(file.Extension)) throw new InvalidDataException(LocalizationCatalog.Get("Commands.UnsupportedAudio"));
        Directory.CreateDirectory(artworkDirectory); Directory.CreateDirectory(lyricsDirectory);
        var name = Path.GetFileNameWithoutExtension(path); var pieces = name.Split(" - ", 2);
        var id = StableId(OperatingSystem.IsWindows() ? file.FullName.ToUpperInvariant() : file.FullName);
        // 宿主可以保留既有歌词命名规则，抽取共享核心时不重命名用户文件。
        var lyricId = lyricFileName?.Invoke(id) ?? id;
        var title = pieces.Length == 2 ? pieces[1] : name;
        var artist = pieces.Length == 2 ? pieces[0] : LocalizationCatalog.Get("Library.UnknownArtist");
        var album = file.Directory?.Name ?? LocalizationCatalog.Get("Library.LocalMusic");
        double duration = 0; string? cover = null;
        try
        {
            using var tagged = TagLib.File.Create(path);
            if (!string.IsNullOrWhiteSpace(tagged.Tag.Title)) title = tagged.Tag.Title;
            if (tagged.Tag.Performers.Length > 0) artist = string.Join(" / ", tagged.Tag.Performers);
            if (!string.IsNullOrWhiteSpace(tagged.Tag.Album)) album = tagged.Tag.Album;
            duration = tagged.Properties.Duration.TotalSeconds;
            var picture = tagged.Tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? tagged.Tag.Pictures.FirstOrDefault();
            if (picture is not null && picture.Data.Count is > 0 and <= 16_000_000)
            {
                var bytes = picture.Data.Data; cover = Path.Combine(artworkDirectory, Convert.ToHexString(SHA256.HashData(bytes)) + ".image");
                StoreContent(cover, bytes);
            }
        }
        catch (Exception e) when (e is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or InvalidDataException or ArgumentException)
        { Warning?.Invoke(this, LocalizationCatalog.Get("Library.TagsUnreadable")); }
        if (cover is null)
        {
            try
            {
            var adjacent = new[] { "cover.jpg", "folder.jpg", "cover.png", name + ".jpg", name + ".png" }.Select(n => Path.Combine(file.DirectoryName!, n)).FirstOrDefault(File.Exists);
            if (adjacent is not null && new FileInfo(adjacent).Length <= 16_000_000)
            {
                using var input = File.OpenRead(adjacent); cover = Path.Combine(artworkDirectory, Convert.ToHexString(SHA256.HashData(input)) + ".image");
                if (!File.Exists(cover)) StoreContent(cover, File.ReadAllBytes(adjacent));
            }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // 单独封面文件不可读不应丢弃可正常播放的歌曲。
                cover = null; Warning?.Invoke(this, LocalizationCatalog.Get("Library.ArtworkUnreadable"));
            }
        }
        // 已有用户歌词优先，同行歌词其次，嵌入歌词最后；避免导入时覆盖用户编辑。
        if (!File.Exists(Path.Combine(lyricsDirectory, lyricId + ".lrc")) && !File.Exists(Path.Combine(lyricsDirectory, lyricId + ".txt")))
        {
            var lyric = new[] { ".lrc", ".txt" }.Select(ext => Path.ChangeExtension(path, ext)).FirstOrDefault(File.Exists);
            if (lyric is not null)
            {
                if (new FileInfo(lyric).Length > 2 * 1024 * 1024) Warning?.Invoke(this, LocalizationCatalog.Get("Library.LyricsTooLarge"));
                else StoreContent(Path.Combine(lyricsDirectory, lyricId + Path.GetExtension(lyric).ToLowerInvariant()), File.ReadAllBytes(lyric));
            }
            // 内嵌歌词保留在原音频标签中，播放时读取；只有外部歌词才复制到应用目录。
        }
        return new(id, title, artist, album, file.FullName, file.Extension, file.Length, duration, cover);
    }

    /// <summary>先写唯一临时文件，再原子发布；同内容并发导入时只保留一份。</summary>
    private static void StoreContent(string target, byte[] bytes)
    {
        if (File.Exists(target)) return;
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); try { File.Move(temporary, target, false); } catch (IOException) when (File.Exists(target)) { } }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
