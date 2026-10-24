using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>类型级外观协议。风格只携带数据，不解析 XAML、绑定表达式、程序集或任意文件引用。</summary>
public static partial class StyleSheetContract
{
    public const string Capability = "ui.styles.v1";
    public const int MaximumBytes = 512 * 1024;
    public static readonly string[] ControlTypes = ["ListeningBarChart", "VectorIcon", "Control", "Button", "ToggleButton", "ToggleSwitch", "CheckBox", "RadioButton", "TextBox", "NumericUpDown", "ComboBox", "ComboBoxItem", "ListBox", "ListBoxItem", "Slider", "ProgressBar", "Expander", "Border", "TextBlock", "ContentPresenter", "Panel", "Grid", "StackPanel", "ScrollViewer", "MenuItem", "ToolTip"];
    public static readonly string[] Properties = ["background", "foreground", "borderBrush", "borderThickness", "cornerRadius", "padding", "margin", "fontSize", "fontWeight", "opacity", "minWidth", "minHeight", "width", "height", "boxShadow", "fill", "stroke", "strokeThickness"];
    public static readonly string[] ResourceNames = ["AccentBrush", "AccentTextBrush", "AccentForegroundBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentSecondaryBrush", "AppBackgroundBrush", "SurfaceBrush", "SurfaceRaisedBrush", "SurfaceHoverBrush", "NavSelectedBrush", "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush", "DividerBrush", "TitleBarBackgroundBrush", "NavigationBackgroundBrush", "ContentBackgroundBrush", "PlayerBackgroundBrush", "TooltipBackgroundBrush", "ErrorBrush", "ControlCornerRadius"];
    public static StyleSheet Read(Stream input)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int length;
        while ((length = input.Read(bytes)) > 0) { if (buffer.Length + length > MaximumBytes) throw new InvalidDataException("Style document exceeds 512 KiB."); buffer.Write(bytes, 0, length); }
        var sheet = JsonSerializer.Deserialize(buffer.ToArray(), StyleSheetJson.Default.StyleSheet) ?? throw new InvalidDataException("Empty style.");
        Validate(sheet); return sheet;
    }
    public static void Validate(StyleSheet sheet)
    {
        if (sheet is null || sheet.Id is null || sheet.Name is null || sheet.Author is null || sheet.Description is null || sheet.Resources is null || sheet.Light is null || sheet.Dark is null || sheet.Rules is null
            || sheet.SchemaVersion != 1 || !Identity().IsMatch(sheet.Id) || sheet.Name.Length is 0 or > 80 || sheet.Author.Length > 80 || sheet.Description.Length > 1000 || !Version.TryParse(sheet.Version, out _)) throw new InvalidDataException("Invalid style identity or schema.");
        if (sheet.Rules.Count > 192 || sheet.Resources.Count > 32 || sheet.Light.Count > 32 || sheet.Dark.Count > 32) throw new InvalidDataException("Too many style definitions.");
        foreach (var resources in new[] { sheet.Resources, sheet.Light, sheet.Dark })
            foreach (var (key, value) in resources) if (!ResourceNames.Contains(key) || value is null || value.Length > 100 || value.Any(char.IsControl)) throw new InvalidDataException("Invalid style resource.");
        foreach (var rule in sheet.Rules)
        {
            if (rule is null || rule.Classes is null || rule.Part is null || rule.Scope is null || rule.Scope.Length > 0 && !ClassName().IsMatch(rule.Scope) || rule.States is null || rule.Setters is null || !ControlTypes.Contains(rule.Type) || rule.Classes.Count > 8 || rule.Classes.Any(c => c is null || !ClassName().IsMatch(c)) || rule.Part.Length > 80 || rule.Part.Any(char.IsControl)
                || rule.States.Any(s => s is not ("pointerover" or "pressed" or "checked" or "disabled" or "focus")) || rule.States.Count > 5 || rule.Setters.Count > Properties.Length
                || rule.DurationMs is < 0 or > 2000 || rule.Curve is not ("parabolic" or "linear" or "cubic-out")) throw new InvalidDataException("Invalid style selector or transition.");
            foreach (var (name, value) in rule.Setters)
                if (!Properties.Contains(name) || value is null || value is JsonObject or JsonArray || value.ToJsonString().Length > 180) throw new InvalidDataException("Invalid style property.");
            if (rule.Motion is { } motion && (!Finite(motion.PressScale, .8, 1.1) || !Finite(motion.HoverScale, .8, 1.1) || !Finite(motion.EnterOffsetY, -80, 80) || motion.EnterDurationMs is < 0 or > 1500)) throw new InvalidDataException("Invalid control motion.");
        }
        if (sheet.Morph is { } morph && (morph.DurationMs is < 0 or > 1500 || !Finite(morph.CornerRadius, 0, 96) || !Finite(morph.Arc, -240, 240))) throw new InvalidDataException("Invalid morph animation.");
        if (sheet.BackgroundBlur is { } blur && !Finite(blur, 0, 40)) throw new InvalidDataException("Invalid background blur.");
        if (sheet.Layout is not null) InterfaceCompositionContract.Validate(sheet.Layout);
    }
    private static bool Finite(double? number, double minimum, double maximum) => number is null || double.IsFinite(number.Value) && number >= minimum && number <= maximum;
    [GeneratedRegex("^[a-z][a-z0-9._-]{2,95}$", RegexOptions.CultureInvariant)] private static partial Regex Identity();
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)] private static partial Regex ClassName();
}

public sealed class StyleSheet
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "Author";
    public string Description { get; set; } = "";
    public Dictionary<string, string> Resources { get; set; } = [];
    public Dictionary<string, string> Light { get; set; } = [];
    public Dictionary<string, string> Dark { get; set; } = [];
    public List<StyleRule> Rules { get; set; } = [];
    public StyleMorph? Morph { get; set; }
    public double? BackgroundBlur { get; set; }
    public InterfaceComposition? Layout { get; set; }
}
public sealed class StyleRule
{
    public string Type { get; set; } = "Control";
    public List<string> Classes { get; set; } = [];
    public string Part { get; set; } = "";
    public string Scope { get; set; } = "";
    public List<string> States { get; set; } = [];
    public Dictionary<string, JsonNode?> Setters { get; set; } = [];
    public int DurationMs { get; set; } = 160;
    public string Curve { get; set; } = "parabolic";
    public StyleMotion? Motion { get; set; }
}
public sealed class StyleMotion
{
    public double? PressScale { get; set; }
    public double? HoverScale { get; set; }
    public double? EnterOffsetY { get; set; }
    public int? EnterDurationMs { get; set; }
}
public sealed class StyleMorph
{
    public bool? Enabled { get; set; }
    /// <summary>仅控制窗口最大化/还原，省略时沿用既有变形行为。</summary>
    public bool? WindowEnabled { get; set; }
    public int? DurationMs { get; set; }
    public double? CornerRadius { get; set; }
    public double? Arc { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(StyleSheet))]
[JsonSerializable(typeof(List<StyleSheet>))]
public partial class StyleSheetJson : JsonSerializerContext;
