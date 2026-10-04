using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NonetMusicPlayer.Desktop.Services;
public sealed record LyricWord(double Seconds, string Text, double EndSeconds = double.NaN);
public sealed record LyricLine(double Seconds, string Text, string Translation = "")
{
    public bool Timed { get; init; } = Seconds >= 0;
    public IReadOnlyList<LyricWord> Words { get; init; } = [];
    public IReadOnlyList<LyricWord> TranslationWords { get; init; } = [];
}
public sealed class LyricsService
{
    public string Folder { get; set; }
    internal string? MirrorFolder { get; set; }
    public LyricsService(string folder) { Folder = folder; Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
    public string PathFor(string id, string extension = ".lrc") => Path.Combine(Folder, MusicLibraryScanner.StableId(id) + extension);
    public string? ExistingPath(string id) => new[] { ".lrc", ".txt" }.Select(ext => PathFor(id, ext)).FirstOrDefault(File.Exists);
    public string Read(string id)
    {
        foreach (var ext in new[] { ".lrc", ".txt" })
        {
            var path = PathFor(id, ext); if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("歌词文件过大（最大 2 MB）。");
            var bytes = File.ReadAllBytes(path);
            try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding("GB18030").GetString(bytes); }
        }
        return "";
    }
    public void Import(string id, string source)
    {
        if (new FileInfo(source).Length > 2_000_000) throw new InvalidDataException("歌词文件过大（最大 2 MB）。");
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not (".lrc" or ".txt")) throw new InvalidDataException("请选择 LRC 或 TXT 歌词。");
        ImportInto(Folder, id, source, extension);
        if (MirrorFolder is { } mirror) ImportInto(mirror, id, PathFor(id, extension), extension);
    }
    /// <summary>手动/同名外部歌词优先，其次直接读取音频标签，不生成内嵌歌词副本。</summary>
    public string ReadForTrack(string id, string audioPath)
    {
        var external = Read(id); if (external.Length > 0 || !File.Exists(audioPath)) return external;
        try { using var audio = TagLib.File.Create(audioPath); var text = audio.Tag.Lyrics ?? ""; return Encoding.UTF8.GetByteCount(text) <= 2_000_000 ? text : ""; }
        catch (Exception e) when (e is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or IOException) { return ""; }
    }
    /// <summary>返回已唱完的 UTF-16 字符数及当前字的分数；文本元素边界由呈现器处理。</summary>
    public static double CharacterProgress(IReadOnlyList<LyricWord> words, double time)
    {
        var result = 0d;
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i]; if (time < word.Seconds) break;
            var end = double.IsFinite(word.EndSeconds) ? word.EndSeconds : i + 1 < words.Count ? words[i + 1].Seconds : word.Seconds + .4;
            result += word.Text.Length * (end <= word.Seconds ? 1 : Math.Clamp((time - word.Seconds) / (end - word.Seconds), 0, 1));
        }
        return result;
    }
    /// <summary>翻译有自身时间戳时使用自身时序；无时序时按原文已唱比例推进，普通逐行歌词保持整行高亮。</summary>
    public static double LineCharacterProgress(LyricLine line, double time, bool translation = false)
    {
        if (!translation) return line.Words.Count > 0 ? CharacterProgress(line.Words, time) : line.Text.Length;
        if (line.TranslationWords.Count > 0) return CharacterProgress(line.TranslationWords, time);
        return line.Words.Count > 0 && line.Text.Length > 0
            ? line.Translation.Length * Math.Clamp(CharacterProgress(line.Words, time) / line.Text.Length, 0, 1)
            : line.Translation.Length;
    }
    private static void ImportInto(string folder, string id, string source, string extension)
    {
        Directory.CreateDirectory(folder); var stem = MusicLibraryScanner.StableId(id); var target = Path.Combine(folder, stem + extension);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) File.Copy(source, target, true);
        var other = Path.Combine(folder, stem + (extension == ".lrc" ? ".txt" : ".lrc")); if (File.Exists(other)) File.Delete(other);
    }
    public void Save(string id, string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > 2_000_000) throw new InvalidDataException("歌词文件过大（最大 2 MB）。");
        AppStorage.AtomicWrite(PathFor(id), content);
        if (MirrorFolder is { } mirror) AppStorage.AtomicWrite(Path.Combine(mirror, MusicLibraryScanner.StableId(id) + ".lrc"), content);
    }
    public static IReadOnlyList<LyricLine> Parse(string text)
    {
        var offsetMatch = Regex.Match(text, @"^\s*\[offset:([+-]?\d+)\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var offset = offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var ms) ? ms / 1000 : 0;
        var result = new List<LyricLine>(); var plain = new List<LyricLine>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim(); var position = 0; var times = new List<double>();
            while (TimeTag.Match(line, position) is { Success: true } tag && tag.Index == position)
            {
                times.Add(Time(tag) + offset); position += tag.Length;
            }
            if (times.Count == 0)
            {
                if (!Regex.IsMatch(line, @"^\[\w+:.*\]$") && !Regex.IsMatch(line, @"^\[\d+:" ) && line.Length > 0) plain.Add(new(-1, line));
                continue;
            }
            var content = line[position..].Trim();
            // 方括号内联时间戳（LDDC）与尖括号 Enhanced LRC 都按片段起止时间解析。
            var wordTags = InlineTag.Matches(content);
            var words = new List<LyricWord>();
            if (wordTags.Count > 0 && wordTags[0].Index > 0)
                words.Add(new(times[0], content[..wordTags[0].Index], Time(wordTags[0]) + offset));
            for (var i = 0; i < wordTags.Count; i++)
            {
                var word = wordTags[i]; var end = i + 1 < wordTags.Count ? wordTags[i + 1].Index : content.Length;
                var fragment = content[(word.Index + word.Length)..end];
                if (fragment.Length > 0) words.Add(new(Time(word) + offset, fragment, i + 1 < wordTags.Count ? Time(wordTags[i + 1]) + offset : double.NaN));
            }
            content = InlineTag.Replace(content, "").Trim();
            if (string.IsNullOrWhiteSpace(content)) continue;
            // 仅一个正文片段加末尾结束戳是逐行歌词，不误识别为逐字译文。
            if (words.Count <= 1) words.Clear();
            foreach (var time in times) result.Add(new(time, content) { Timed = true, Words = words.Select(word => word with { Seconds = word.Seconds + time - times[0], EndSeconds = word.EndSeconds + time - times[0] }).ToArray() });
        }
        // 相同时间戳按原文和译文分组，保持文件顺序，不按字符猜测语言。
        return result.Count > 0 ? result.GroupBy(x => x.Seconds).OrderBy(g => g.Key).Select(group =>
        {
            var lines = group.Where(x => !string.IsNullOrWhiteSpace(x.Text)).DistinctBy(x => x.Text).ToArray();
            return lines.Length == 0 ? new LyricLine(group.Key, "") { Timed = true } : lines[0] with { Translation = string.Join('\n', lines.Skip(1).Select(x => x.Text)), TranslationWords = lines.Length == 2 ? lines[1].Words : [] };
        }).ToArray() : plain;
    }
    private static readonly Regex TimeTag = new(@"\[(\d{1,4}):([0-5]\d)(?:[.:;](\d{1,3}))?\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InlineTag = new(@"[\[<](\d{1,4}):([0-5]\d)(?:[.:;](\d{1,3}))?[\]>]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static double Time(Match match) => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60
        + double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
        + (match.Groups[3].Success ? double.Parse("0." + match.Groups[3].Value, CultureInfo.InvariantCulture) : 0);
}
