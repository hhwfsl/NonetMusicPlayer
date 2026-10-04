using System.Globalization;
using Avalonia;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>将英文 ID 目录接入 Avalonia 动态资源，修改文案不改变控件绑定标识。</summary>
public static class L10n
{
    private static readonly HashSet<string> Resources = new(StringComparer.Ordinal);
    public static string Language => LocalizationCatalog.Language;
    public static CultureInfo Culture => LocalizationCatalog.Culture;
    public static event EventHandler? LanguageChanged;
    static L10n() => LocalizationCatalog.LanguageChanged += (_, _) =>
    {
        if (Application.Current is { } app) foreach (var key in Resources) app.Resources["L10n_" + key] = T(key);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    };
    public static void SetLanguage(string? language) => LocalizationCatalog.SetLanguage(language);
    public static string T(string? key) => LocalizationCatalog.Get(key);
    public static string Format(string key, params object[] arguments) => LocalizationCatalog.Format(key, arguments);
    public static string Resource(string key)
    {
        Resources.Add(key);
        if (Application.Current is { } app) app.Resources["L10n_" + key] = T(key);
        return "L10n_" + key;
    }
    public static string LanguageName(string code) => code switch { "ja-JP" => "日本語", "en-US" => "English", _ => "简体中文" };
}
