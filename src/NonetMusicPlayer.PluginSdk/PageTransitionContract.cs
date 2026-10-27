using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>普通页面导航的可选动画协议；仅描述表现，不执行脚本或改变页面业务。</summary>
public static class PageTransitionContract
{
    public const string Capability = "ui.page-transitions.v1";
    public static readonly string[] Effects = ["none", "fade", "slide", "fade-slide"];
    public static readonly string[] Easings = ["linear", "cubic-out", "parabolic"];
    public static ResolvedPageTransition Default { get; } = new(true, "fade-slide", 180, 0, 12, .9, "cubic-out");
    public static void Validate(PageTransitions value)
    {
        if (value.Defaults is null || value.Rules is null || value.Rules.Count > 32) throw new InvalidDataException("Invalid page transitions.");
        Profile(value.Defaults);
        foreach (var rule in value.Rules)
        {
            if (rule is null || !Selector(rule.From) || !Selector(rule.To) || rule.Profile is null) throw new InvalidDataException("Invalid page transition rule.");
            Profile(rule.Profile);
        }
    }
    private static bool Selector(string? text) => !string.IsNullOrEmpty(text) && text.Length <= 200 && !text.Any(char.IsControl)
        && (text.IndexOf('*') < 0 || text.IndexOf('*') == text.Length - 1);
    private static void Profile(PageTransitionProfile value)
    {
        if (value.Effect is not null && !Effects.Contains(value.Effect) || value.Easing is not null && !Easings.Contains(value.Easing)
            || value.DurationMs is < 0 or > 1000 || !Finite(value.OffsetX, -96, 96) || !Finite(value.OffsetY, -96, 96) || !Finite(value.StartOpacity, 0, 1))
            throw new InvalidDataException("Invalid page transition profile.");
    }
    private static bool Finite(double? value, double min, double max) => value is null || double.IsFinite(value.Value) && value >= min && value <= max;
    /// <summary>后层及后匹配规则逐字段覆盖，未声明字段保留前层值；兼容既有页面时长设置。</summary>
    public static ResolvedPageTransition Resolve(IEnumerable<StyleSheet> layers, string from, string to)
    {
        var result = Default;
        foreach (var layer in layers)
        {
            if (layer.Layout?.Motion?.PageDurationMs is { } duration) result = result with { DurationMs = duration };
            if (layer.PageTransitions is not { } settings) continue;
            result = Apply(result, settings.Defaults);
            foreach (var rule in settings.Rules) if (Matches(rule.From, from) && Matches(rule.To, to)) result = Apply(result, rule.Profile);
        }
        return result;
    }
    private static bool Matches(string selector, string page) => selector.EndsWith('*') ? page.StartsWith(selector[..^1], StringComparison.Ordinal) : page == selector;
    private static ResolvedPageTransition Apply(ResolvedPageTransition value, PageTransitionProfile fields) => new(
        fields.Enabled ?? value.Enabled, fields.Effect ?? value.Effect, fields.DurationMs ?? value.DurationMs,
        fields.OffsetX ?? value.OffsetX, fields.OffsetY ?? value.OffsetY, fields.StartOpacity ?? value.StartOpacity, fields.Easing ?? value.Easing);
    public static JsonObject Catalog() => new()
    {
        ["capability"] = Capability, ["scope"] = "ordinary page content; lyrics use layout.motion.lyricsDurationMs",
        ["effects"] = new JsonArray(Effects.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
        ["easings"] = new JsonArray(Easings.Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
        ["selectors"] = "exact page ID or one trailing *, e.g. playlist:*, plugin:*",
        ["maximumRules"] = 32, ["maximumDurationMs"] = 1000, ["maximumOffset"] = 96,
        ["defaults"] = new JsonObject { ["enabled"] = Default.Enabled, ["effect"] = Default.Effect, ["durationMs"] = Default.DurationMs,
            ["offsetX"] = Default.OffsetX, ["offsetY"] = Default.OffsetY, ["startOpacity"] = Default.StartOpacity, ["easing"] = Default.Easing }
    };
}
public sealed class PageTransitions
{
    public PageTransitionProfile Defaults { get; set; } = new();
    public List<PageTransitionRule> Rules { get; set; } = [];
}
public sealed class PageTransitionRule
{
    public string From { get; set; } = "*";
    public string To { get; set; } = "*";
    public PageTransitionProfile Profile { get; set; } = new();
}
public sealed class PageTransitionProfile
{
    public bool? Enabled { get; set; }
    public string? Effect { get; set; }
    public int? DurationMs { get; set; }
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
    public double? StartOpacity { get; set; }
    public string? Easing { get; set; }
}
public sealed record ResolvedPageTransition(bool Enabled, string Effect, int DurationMs, double OffsetX, double OffsetY, double StartOpacity, string Easing);
