using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>两个宿主共用的输入约束；命令不得以静默修正替代错误报告。</summary>
public static class SettingsContract
{
    private static readonly IReadOnlyDictionary<string, (double Min, double Max)> Ranges = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
    {
        ["volume"] = (0, 100), ["historyLimit"] = (0, 100000), ["fontSize"] = (11, 18), ["sidebarWidth"] = (170, 320), ["playerHeight"] = (96, 180),
        ["terminalScrollbackLines"] = (1, 10000), ["terminalFontSize"] = (10, 32),
        ["backgroundImageOpacity"] = (0, 1), ["titleBarOpacity"] = (0, 1), ["navigationOpacity"] = (0, 1), ["contentOpacity"] = (0, 1), ["playerOpacity"] = (0, 1), ["terminalOpacity"] = (0, 1), ["uiOpacity"] = (.35, 1),
        ["controlCornerRadius"] = (0, 22), ["desktopLyricsFontSize"] = (14, 48), ["desktopLyricsWidth"] = (360, 2000), ["desktopLyricsHeight"] = (120, 2000), ["lyricOffset"] = (-60, 60)
    };
    public static void Validate(string key, JsonNode? value)
    {
        if (Ranges.TryGetValue(key, out var range))
        {
            if (value is not JsonValue number || number.GetValueKind() != System.Text.Json.JsonValueKind.Number || !double.TryParse(number.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed < range.Min || parsed > range.Max) throw new InvalidDataException(Localization.LocalizationCatalog.Format("Commands.SettingRange", key, range.Min, range.Max));
            if ((key.Equals("historyLimit", StringComparison.OrdinalIgnoreCase) || key.Equals("terminalScrollbackLines", StringComparison.OrdinalIgnoreCase)) && parsed != Math.Truncate(parsed)) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.IntegerRequired"));
        }
        if (key.Equals("language", StringComparison.OrdinalIgnoreCase) && value?.GetValue<string>() is not ("zh-CN" or "en-US" or "ja-JP")) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnsupportedLanguage"));
        if (key.Equals("terminalMinimumLogLevel", StringComparison.OrdinalIgnoreCase) && value?.GetValue<string>() is not ("INFO" or "WARN" or "ERROR")) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Terminal.InvalidLevel"));
        if (key.Equals("theme", StringComparison.OrdinalIgnoreCase) && value?.GetValue<string>() is not ("System" or "Dark" or "Light")) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnsupportedTheme"));
        if (key.Equals("playMode", StringComparison.OrdinalIgnoreCase) && value?.GetValue<int>() is not (1 or 2 or 3)) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ModeRange"));
        if (key.Equals("accent", StringComparison.OrdinalIgnoreCase) && !System.Text.RegularExpressions.Regex.IsMatch(value?.GetValue<string>() ?? "", "^#[0-9A-Fa-f]{6}$")) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.AccentFormat"));
    }
}
