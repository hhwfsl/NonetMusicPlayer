using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>根据用户主题色推导交互状态，同时保持文字和表面的可读对比度。</summary>
public static class ThemeService
{
    public static void Apply(AppSettings settings, IReadOnlyDictionary<string, string>? pluginTokens = null)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = settings.Theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        var light = app.ActualThemeVariant == ThemeVariant.Light;
        var accent = Color.Parse(settings.Accent);
        if (pluginTokens?.TryGetValue("Accent", out var overrideAccent) == true && Color.TryParse(overrideAccent, out var parsedAccent)) accent = parsedAccent;
        var background = Color.Parse(light ? "#F6F7FB" : "#101117");
        var surface = Color.Parse(light ? "#FFFFFF" : "#171920");
        if (pluginTokens?.TryGetValue("Background", out var bg) == true && Color.TryParse(bg, out var parsedBg)) background = parsedBg;
        if (pluginTokens?.TryGetValue("Surface", out var fg) == true && Color.TryParse(fg, out var parsedFg)) surface = parsedFg;
        var primaryText = ContrastText(surface);
        if (pluginTokens?.TryGetValue("Text", out var text) == true && Color.TryParse(text, out var parsedText) && Contrast(parsedText, surface) >= 4.5) primaryText = parsedText;
        var darkSurface = Luminance(surface) < .45;
        var neutral = darkSurface ? Colors.White : Colors.Black;
        // 主题色接近背景色时仍须提供可辨识的交互状态。
        var interactionTint = Contrast(accent, surface) < 1.25 ? Mix(accent, ContrastText(surface), .35) : accent;
        var hasBackground = !string.IsNullOrWhiteSpace(settings.BackgroundImagePath);
        Color Panel(Color color) => hasBackground ? Color.FromArgb(215, color.R, color.G, color.B) : color;
        Color Region(double opacity) => hasBackground ? Color.FromArgb((byte)Math.Round(opacity * 255), surface.R, surface.G, surface.B) : surface;
        Set("TitleBarBackgroundBrush", Region(settings.TitleBarOpacity)); Set("NavigationBackgroundBrush", Region(settings.NavigationOpacity));
        Set("ContentBackgroundBrush", Region(settings.ContentOpacity)); Set("PlayerBackgroundBrush", Region(settings.PlayerOpacity));
        // 大面积面板使用克制的主题色调，保持中性明度和可读对比，避免高饱和底色。
        var card = Mix(surface, interactionTint, darkSurface ? .065 : .035);
        var raised = Mix(Mix(surface, neutral, darkSurface ? .055 : .04), interactionTint, darkSurface ? .085 : .055);
        Set("AppBackgroundBrush", background); Set("SurfaceBrush", Panel(card));
        Set("SurfaceRaisedBrush", Panel(raised));
        // 独立 Popup 需要不透明底色，避免背景参与文字抗锯齿时产生黑色光晕。
        Set("TooltipBackgroundBrush", raised);
        app.Resources["AppFontSize"] = settings.FontSize;
        // 终端只调整底色的不透明度，文字和插入点保持清晰；无背景图时也独立生效。
        Set("TerminalBackgroundBrush", Color.FromArgb((byte)Math.Round(settings.TerminalOpacity * 255), raised.R, raised.G, raised.B));
        var hover = Mix(surface, interactionTint, darkSurface ? .18 : .10);
        var selected = Mix(surface, interactionTint, darkSurface ? .25 : .15);
        Color Readable(Color color) => EnsureContrast(EnsureContrast(EnsureContrast(EnsureContrast(EnsureContrast(color, surface, 4.5), card, 4.5), raised, 4.5), hover, 4.5), selected, 4.5);
        Set("SurfaceHoverBrush", hover); Set("NavSelectedBrush", selected);
        Set("TextPrimaryBrush", Readable(primaryText)); Set("TextSecondaryBrush", Readable(Mix(surface, primaryText, .63)));
        Set("TextMutedBrush", Readable(Mix(surface, primaryText, .55)));
        Set("DividerBrush", Mix(surface, primaryText, .16));
        Set("AccentBrush", accent); Set("AccentTextBrush", Readable(accent));
        Set("AccentForegroundBrush", ContrastText(accent));
        Set("AccentHoverBrush", Mix(accent, ContrastText(accent), .10));
        Set("AccentPressedBrush", Mix(accent, ContrastText(accent), .18));
        Set("AccentSecondaryBrush", Mix(accent, surface, .30));
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            Set("ExpanderHeaderBackground" + state, state is "PointerOver" or "Pressed" ? Mix(surface, interactionTint, .18) : Mix(surface, neutral, .055));
            Set("ExpanderHeaderForeground" + state, primaryText);
            Set("ExpanderHeaderBorderBrush" + state, Mix(surface, primaryText, .16));
            Set("ExpanderChevronForeground" + state, primaryText);
            Set("ExpanderChevronBackground" + state, surface);
        }
        Set("ExpanderContentBackground", surface); Set("ExpanderContentBorderBrush", Mix(surface, primaryText, .16));
        Set("ErrorBrush", EnsureContrast(Color.Parse("#F0526C"), surface, 4.5));
        var fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (fluent is not null) foreach (var palette in fluent.Palettes.Values) palette.Accent = accent;
        app.Resources["ControlCornerRadius"] = new CornerRadius(settings.ControlCornerRadius);
        app.Resources["ButtonMinHeight"] = settings.TouchMode ? 44d : 38d;
        app.Resources["ButtonMinWidth"] = settings.TouchMode ? 44d : 30d;
        app.Resources["IconSize"] = settings.TouchMode ? 22d : 20d;
        app.Resources["SliderHorizontalHeight"] = settings.TouchMode ? 44d : 32d;
        app.Resources["SliderHorizontalThumbWidth"] = settings.TouchMode ? 18d : 12d;
        app.Resources["SliderHorizontalThumbHeight"] = settings.TouchMode ? 18d : 12d;
        app.Resources["TrackRowMinHeight"] = settings.TouchMode ? 72d : 64d;
    }

    private static void Set(string key, Color color)
    {
        var resources = Application.Current!.Resources;
        if (resources.TryGetValue(key, out var current) && current is SolidColorBrush brush) brush.Color = color;
        else resources[key] = new SolidColorBrush(color);
    }
    public static Color Mix(Color a, Color b, double amount) => Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * amount), (byte)Math.Round(a.G + (b.G - a.G) * amount), (byte)Math.Round(a.B + (b.B - a.B) * amount));
    public static double Luminance(Color color)
    {
        static double Channel(byte n) { var v = n / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
    public static double Contrast(Color a, Color b) { var first = Luminance(a); var second = Luminance(b); return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05); }
    public static Color ContrastText(Color background) => Contrast(Colors.White, background) >= Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
    private static Color EnsureContrast(Color color, Color background, double ratio)
    {
        var destination = ContrastText(background);
        for (var step = 0; step <= 20; step++) { var candidate = Mix(color, destination, step / 20d); if (Contrast(candidate, background) >= ratio) return candidate; }
        return destination;
    }
}
