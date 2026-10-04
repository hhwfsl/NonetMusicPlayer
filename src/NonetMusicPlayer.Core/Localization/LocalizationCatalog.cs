using System.Globalization;
using System.Text.Json;

namespace NonetMusicPlayer.Core.Localization;

/// <summary>桌面和命令行共用的英文资源 ID 目录，不引用图形框架。</summary>
public static class LocalizationCatalog
{
    static LocalizationCatalog() => Plugins.PluginMessages.Translate = Get;
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Entries = Load();
    public static string Language { get; private set; } = "zh-CN";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);
    public static IEnumerable<string> Keys => Entries.Keys;
    public static event EventHandler? LanguageChanged;

    /// <summary>即时切换资源语言，不改写歌曲标签或用户自己命名的内容。</summary>
    public static void SetLanguage(string? language)
    {
        var next = language is "en-US" or "ja-JP" ? language : "zh-CN";
        if (Language == next) return;
        Language = next; CultureInfo.CurrentUICulture = Culture; LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static IReadOnlyDictionary<string, string> Translations(string key) => Entries[key];

    /// <summary>只按英文 ID 精确查询；非资源字符串原样传递，不做中文原文匹配。</summary>
    public static string Get(string? key) => string.IsNullOrEmpty(key) ? "" : Entries.TryGetValue(key, out var values) ? values[Language] : key;
    public static string Format(string key, params object[] arguments) => string.Format(Culture, Get(key), arguments);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var entries = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var assembly = typeof(LocalizationCatalog).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Localization.Resources.") && n.EndsWith(".json")))
        {
            using var stream = assembly.GetManifestResourceStream(name)!; using var document = JsonDocument.Parse(stream);
            foreach (var item in document.RootElement.EnumerateObject())
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(item.Name, "^[A-Za-z][A-Za-z0-9]*\\.[A-Za-z][A-Za-z0-9]*$")) throw new InvalidDataException("Invalid resource ID: " + item.Name);
                var values = item.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
                if (new[] { "zh-CN", "en-US", "ja-JP" }.Any(language => !values.TryGetValue(language, out var value) || string.IsNullOrWhiteSpace(value))) throw new InvalidDataException("Incomplete resource: " + item.Name);
                if (!entries.TryAdd(item.Name, values)) throw new InvalidDataException("Duplicate resource: " + item.Name);
            }
        }
        return entries;
    }
}
