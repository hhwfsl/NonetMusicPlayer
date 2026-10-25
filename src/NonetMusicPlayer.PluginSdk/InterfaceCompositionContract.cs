using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>完整界面方案只描述表现树与运行期视觉设置，不包含脚本、数据写入或业务实现。</summary>
public static class InterfaceCompositionContract
{
    public const string Capability = "ui.composition.v1";
    public const string PlaylistCardsCapability = "ui.playlist-cards.v1";
    public static readonly string[] NavigationPages = ["library", "songs", "playlists", "history", "albums", "artists", "statistics", "devices", "settings", "plugins", "terminal", "favorites", "lyrics", "back"];
    public static readonly string[] Actions = ["playlist.create", "player.play-pause", "player.previous", "player.next", "lyrics.toggle"];
    public static readonly string[] Settings = ["navigationRight", "playerTop", "sidebarWidth", "playerHeight", "myMusicExpanded", "pluginsExpanded", "playlistsExpanded", "controlCornerRadius", "fontSize", "theme", "accent", "accentFromBackground", "uiOpacity", "titleBarOpacity", "navigationOpacity", "contentOpacity", "playerOpacity", "backgroundImageOpacity", "backgroundImageStretch", "titleButtonsOnLeft"];
    public static void Validate(InterfaceComposition composition)
    {
        if (composition.Settings is null || composition.Regions is null || composition.Pages is null || composition.Regions.Count > 24 || composition.Pages.Count > 16 || composition.Settings.Count > Settings.Length) throw new InvalidDataException("Invalid interface composition.");
        foreach (var (key, value) in composition.Settings)
        {
            if (!Settings.Contains(key) || value is null) throw new InvalidDataException("Only presentation settings can be temporarily owned.");
            if (key is "navigationRight" or "playerTop" or "myMusicExpanded" or "pluginsExpanded" or "playlistsExpanded" or "accentFromBackground" or "titleButtonsOnLeft") { if (!value.AsValue().TryGetValue<bool>(out _)) throw new InvalidDataException("Expected boolean setting."); }
            else if (key is "theme" or "accent" or "backgroundImageStretch")
            {
                var text = value.GetValue<string>();
                if (key == "theme" && text is not ("Light" or "Dark" or "System") || key == "backgroundImageStretch" && text is not ("Uniform" or "UniformToFill") || key == "accent" && (text.Length != 7 || text[0] != '#' || text.Skip(1).Any(c => !Uri.IsHexDigit(c)))) throw new InvalidDataException("Invalid visual choice.");
            }
            else
            {
                if (!double.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new InvalidDataException("Invalid visual number.");
                var (min, max) = key switch { "sidebarWidth" => (170d, 320d), "playerHeight" => (96d, 180d), "controlCornerRadius" => (0d, 22d), "fontSize" => (11d, 18d), "uiOpacity" => (.35, 1d), _ => (0d, 1d) };
                if (number < min || number > max) throw new InvalidDataException("Visual number is out of range.");
            }
        }
        var count = 0;
        void Visit(InterfaceNode node, int depth)
        {
            if (node is null || ++count > 512 || depth > 20 || node.Children is null || node.Properties is null || node.Classes is null || node.Children.Count > 64 || node.Classes.Count > 8 || node.Properties.Count > 40
                || node.Kind is not ("grid" or "stack" or "border" or "scroll" or "canvas" or "ref" or "navigate" or "action" or "playlists" or "artwork" or "text" or "icon" or "spacer")
                || node.Target is null || node.Target.Length > 120 || node.Page is null || node.Page.Length > 100 || node.Label is null || node.Label.Length > 300 || node.Icon is null || node.Icon.Length > 60
                || node.WhenPage is null || node.WhenPage.Length > 100 || node.WhenPage.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('!' or ':' or '-' or '_' or '*'))
                || node.Classes.Any(c => c is null || c.Length is 0 or > 64 || c.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_')))) throw new InvalidDataException("Invalid interface node.");
            if (node.Kind == "ref" && (node.Target.Length == 0 || node.Children.Count > 0) || node.Kind is "border" or "scroll" && node.Children.Count > 1) throw new InvalidDataException("Invalid container or reference.");
            if (node.Presentation is not ("list" or "cards") || node.Presentation == "cards" && node.Kind != "playlists") throw new InvalidDataException("Invalid presentation mode.");
            if (node.Target is "TitleBarSurface" or "FullLyricsHost" && node.WhenPage.Length > 0) throw new InvalidDataException("Core hosts cannot be conditional.");
            if (node.Kind == "navigate" && !NavigationPages.Contains(node.Page) && !node.Page.StartsWith("view:", StringComparison.Ordinal) || node.Kind == "action" && !Actions.Contains(node.Target)) throw new InvalidDataException("A preset may only invoke existing presentation actions.");
            if (node.NarrowProperties is null || node.NarrowProperties.Count > 40) throw new InvalidDataException("Invalid responsive properties.");
            foreach (var value in node.Properties.Concat(node.NarrowProperties).Select(p => p.Value)) if (value is JsonArray or JsonObject || value?.ToJsonString().Length > 180) throw new InvalidDataException("Only bounded layout values are allowed.");
            foreach (var child in node.Children) Visit(child, depth + 1);
        }
        foreach (var (target, node) in composition.Regions) { if (target.Length is 0 or > 120 || target.StartsWith("element:", StringComparison.Ordinal)) throw new InvalidDataException("A layout region requires a stable control name."); Visit(node, 0); }
        if (composition.Regions.TryGetValue("ShellLayout", out var shell))
        {
            IEnumerable<string> Refs(InterfaceNode n) => (n.Kind == "ref" ? new[] { n.Target } : []).Concat(n.Children.SelectMany(Refs));
            var refs = Refs(shell).ToArray();
            if (refs.Count(r => r == "TitleBarSurface") != 1 || refs.Count(r => r == "FullLyricsHost") != 1) throw new InvalidDataException("A full shell must preserve the native caption and lyric host.");
        }
        bool Borrows(InterfaceNode node) => node.Kind == "ref" || node.Children.Any(Borrows);
        foreach (var (key, node) in composition.Pages) { if (key.Length is 0 or > 60 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw new InvalidDataException("Invalid presentation page key."); Visit(node, 0); if (Borrows(node)) throw new InvalidDataException("Presentation pages cannot borrow shell controls."); }
        if (composition.Motion is { } motion && (motion.LyricsDurationMs is < 0 or > 1000 || motion.PageDurationMs is < 0 or > 1000 || motion.LyricsCornerRadius is { } radius && (!double.IsFinite(radius) || radius is < 0 or > 64))) throw new InvalidDataException("Invalid interface motion.");
        if (composition.Lyrics is { } lyrics)
            foreach (var color in new[] { lyrics.ActiveColor, lyrics.PendingColor, lyrics.TranslationColor })
                if (color is null || color.Length > 100 || color.Length > 0 && !(color.StartsWith('@') && StyleSheetContract.ResourceNames.Contains(color[1..])) && !(color.Length is 7 or 9 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit))) throw new InvalidDataException("Invalid lyric presentation color.");
    }
}

public sealed class InterfaceComposition
{
    public JsonObject Settings { get; set; } = new();
    public Dictionary<string, InterfaceNode> Regions { get; set; } = [];
    public Dictionary<string, InterfaceNode> Pages { get; set; } = [];
    public InterfaceMotionProfile? Motion { get; set; }
    public InterfaceLyricsProfile? Lyrics { get; set; }
}
public sealed class InterfaceNode
{
    public string Kind { get; set; } = "grid";
    /// <summary>歌单仅改变表现为列表或虚拟化卡片，复用原业务入口。</summary>
    public string Presentation { get; set; } = "list";
    public string Target { get; set; } = "";
    public string Page { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string WhenPage { get; set; } = "";
    public bool Optional { get; set; }
    public List<string> Classes { get; set; } = [];
    public JsonObject Properties { get; set; } = new();
    public JsonObject NarrowProperties { get; set; } = new();
    public List<InterfaceNode> Children { get; set; } = [];
}
public sealed class InterfaceMotionProfile
{
    public int? LyricsDurationMs { get; set; }
    public double? LyricsCornerRadius { get; set; }
    public int? PageDurationMs { get; set; }
}
public sealed class InterfaceLyricsProfile
{
    /// <summary>歌词页延伸到窗口内边缘；不改变系统窗口按钮和输入行为。</summary>
    public bool? EdgeToEdge { get; set; }
    public string PendingColor { get; set; } = "";
    public string ActiveColor { get; set; } = "";
    public string TranslationColor { get; set; } = "";
}
