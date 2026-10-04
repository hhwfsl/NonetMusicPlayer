using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayer.Core.Lyrics;

/// <summary>时间标注的纯业务模型；使用音频时钟而非界面刷新位置，空行和元数据不参与标注。</summary>
public sealed class LyricsTimingSession
{
    private static readonly Regex Metadata = new(@"^\[(?:ar|ti|al|by|offset|re|ve|length|au):.*\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Tags = new(@"^(?:\[\d{1,4}:[0-5]\d(?:[.:;]\d{1,3})?\])+|<\d{1,4}:[0-5]\d(?:[.:;]\d{1,3})?>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private readonly double?[] _times;
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<double?> Times => Array.AsReadOnly(_times);
    public int NextLine { get; private set; } = 1;
    public bool Complete => NextLine == Lines.Count;
    public LyricsTimingSession(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 2_000_000) throw new InvalidDataException(LocalizationCatalog.Get("LyricsTiming.TooLarge"));
        var lines = text.TrimStart('\uFEFF').Replace("\r", "").Split('\n').Select(l => l.Trim())
            .Where(l => l.Length > 0 && !Metadata.IsMatch(l)).Select(l => Tags.Replace(l, "").Trim()).Where(l => l.Length > 0).ToArray();
        if (lines.Length is < 1 or > 5000 || lines.Any(l => l.Length > 4000)) throw new InvalidDataException(LocalizationCatalog.Get("LyricsTiming.InvalidLines"));
        Lines = Array.AsReadOnly(lines); _times = new double?[lines.Length]; _times[0] = 0;
    }
    public void Mark(double seconds)
    {
        if (Complete) return;
        seconds = Math.Round(seconds, 3, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(seconds) || seconds < 0 || seconds < _times[NextLine - 1])
            throw new InvalidOperationException(LocalizationCatalog.Get("LyricsTiming.TimeOrder"));
        _times[NextLine++] = seconds;
    }
    public void Undo()
    {
        if (NextLine <= 1) return;
        _times[--NextLine] = null;
    }
    public string ToLrc()
    {
        if (!Complete) throw new InvalidOperationException(LocalizationCatalog.Get("LyricsTiming.Unfinished"));
        var output = new StringBuilder();
        for (var i = 0; i < Lines.Count; i++) output.Append(FormatTimestamp(_times[i]!.Value)).Append(Lines[i]).Append('\n');
        return output.ToString();
    }
    public static string FormatTimestamp(double seconds)
    {
        var milliseconds = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"[{milliseconds / 60000:00}:{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}]");
    }
}
