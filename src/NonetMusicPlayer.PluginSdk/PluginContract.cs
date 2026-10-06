using System.Text.Json;
namespace NonetMusicPlayer.Core.Plugins;

public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public int ContractVersion { get; set; } = 1;
    public string Type { get; set; } = "provider";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    /// <summary>开发者在源码清单中声明的 GitHub 账户及仓库名；旧包允许省略。</summary>
    public string RepositoryOwner { get; set; } = "";
    public string RepositoryName { get; set; } = "";
    /// <summary>仅宿主记录实际远程导入来源，安装包不能预授予或伪造此来源。</summary>
    public string OriginRepository { get; set; } = "";
    /// <summary>可选发行平台；空值兼容旧包或平台无关包，不改变 Contract。</summary>
    public string Platform { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public Dictionary<string, string> EntryPoints { get; set; } = [];
    public Dictionary<string, string> Tokens { get; set; } = [];
    public List<WidgetDefinition> Widgets { get; set; } = [];
    public string NavigationLabel { get; set; } = "";
    public string PageEntry { get; set; } = "";
    public List<PluginMenuContribution> MenuContributions { get; set; } = [];
    public bool Enabled { get; set; }
    public string Configuration { get; set; } = "{}";
    public List<string> LifecycleMethods { get; set; } = [];
    /// <summary>仅由宿主在用户确认修改源文件后保存；安装包中的该值始终被忽略。</summary>
    public bool AudioTagWriteConsent { get; set; }
    public void Validate()
    {
        Permissions ??= []; EntryPoints ??= []; Tokens ??= []; Widgets ??= []; MenuContributions ??= []; Configuration ??= "{}";
        LifecycleMethods ??= [];
        RepositoryOwner ??= ""; RepositoryName ??= ""; OriginRepository ??= ""; Platform ??= "";
        if (Platform.Length > 0 && !PluginPlatformPolicy.IsRid(Platform)) throw new InvalidDataException("Invalid plugin platform.");
        if (RepositoryOwner.Length != 0 || RepositoryName.Length != 0) PluginRepository.Validate(RepositoryOwner, RepositoryName);
        if (OriginRepository.Length != 0) PluginRepository.Parse(OriginRepository);
        if (LifecycleMethods.Count > 3 || LifecycleMethods.Any(m => m is not ("lifecycle.disable" or "lifecycle.uninstall" or "lifecycle.shutdown")) || LifecycleMethods.Count > 0 && Type is not ("provider" or "lyrics" or "agent")) throw new InvalidDataException("只有进程插件可声明受支持的进程生命周期方法。");
        if (!System.Text.RegularExpressions.Regex.IsMatch(Id ?? "", "^[a-z][a-z0-9.-]{2,80}$") || string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
            throw new InvalidDataException("插件标识或名称不合法。");
        if ((Description?.Length ?? 0) > 2000 || (Author?.Length ?? 0) > 100 || Permissions.Count > 16 || Permissions.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 32)) throw new InvalidDataException("插件描述或权限清单过长。");
        if (ContractVersion != 1 || Type is not ("provider" or "theme" or "widget" or "ui" or "lyrics" or "agent")) throw new InvalidDataException("不支持此插件类型或 Contract 版本。");
        if (!System.Version.TryParse(Version, out _)) throw new InvalidDataException("插件版本需要形如 1.0.0。");
        if (Type == "provider" && (!Permissions.Contains("network") || !Permissions.Contains("process"))) throw new InvalidDataException("音源插件必须声明 network 与 process 权限。");
        if (Type == "lyrics" && (!Permissions.Contains("network") || !Permissions.Contains("process") || !Permissions.Contains("lyrics-search")
            || Permissions.Any(p => p is not ("network" or "process" or "lyrics-search" or "navigation" or "audio-tags")) || EntryPoints.Count == 0 || Tokens.Count != 0 || Widgets.Count != 0))
            throw new InvalidDataException("歌词插件需声明 network、process、lyrics-search；仅可附加 navigation 与 audio-tags 权限。");
        // Agent 为可选进程能力，保持原有 v1 清单及旧类型的校验行为。
        if (Type == "agent" && (!Permissions.Contains("network") || !Permissions.Contains("process") || !Permissions.Contains("agent-control")
            || Permissions.Any(p => p is not ("network" or "process" or "agent-control" or "navigation")) || EntryPoints.Count == 0 || Tokens.Count != 0 || Widgets.Count != 0))
            throw new InvalidDataException("Agent 插件需声明 network、process、agent-control，只允许附加 navigation。");
        if (Type is "lyrics" or "agent")
        {
            NavigationLabel = string.IsNullOrWhiteSpace(NavigationLabel) ? Name : NavigationLabel.Trim();
            if (NavigationLabel.Length > 60 || NavigationLabel.Any(char.IsControl)) throw new InvalidDataException("插件侧栏标签不合法。");
            PluginPathPolicy.ValidateRelativePath(PageEntry);
            if (!PageEntry.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || PageEntry == "manifest.json") throw new InvalidDataException("歌词插件需提供独立的 JSON 页面。");
        }
        if (Type == "ui")
        {
            if (Permissions.Any(p => p is not ("player-control" or "navigation" or "statistics" or "desktop-widget" or "lyrics-editor")) || EntryPoints.Count != 0 || Tokens.Count != 0 || Widgets.Count != 0)
                throw new InvalidDataException("声明式 UI 插件仅支持受限的播放器、导航、统计、桌面部件和歌词编辑权限，不允许进程、网络或可执行入口。");
            NavigationLabel = string.IsNullOrWhiteSpace(NavigationLabel) ? Name : NavigationLabel.Trim();
            if (NavigationLabel.Length > 60 || NavigationLabel.Any(char.IsControl)) throw new InvalidDataException("插件侧栏标签最多 60 个字符，不能包含控制字符。");
            PluginPathPolicy.ValidateRelativePath(PageEntry);
            if (PageEntry.Length > 240 || PageEntry.Split('/').Length > 8) throw new InvalidDataException("UI 页面路径过长或目录层级过深。");
            if (!PageEntry.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("UI 插件页面入口必须是包内 JSON 文件。");
            if (PageEntry.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("UI 页面与清单必须分别声明。");
        }
        if (MenuContributions.Count > 8 || MenuContributions.Count > 0 && (Type is not ("ui" or "lyrics") || !Permissions.Contains("navigation")))
            throw new InvalidDataException("菜单贡献仅允许具有 navigation 权限的 UI 插件，每个插件最多 8 项。");
        foreach (var contribution in MenuContributions)
            if (contribution is null || contribution.Location != "lyrics.more" || contribution.Action != "open-page" && !(Type == "lyrics" && contribution.Action == "match-lyrics")
                || string.IsNullOrWhiteSpace(contribution.Label) || contribution.Label.Length > 60 || contribution.Label.Any(char.IsControl))
                throw new InvalidDataException("菜单贡献需要指定 lyrics.more、open-page 与合法标签。");
        foreach (var path in EntryPoints.Values) PluginPathPolicy.ValidateRelativePath(path);
        foreach (var pair in Tokens)
            if (pair.Key is not ("Accent" or "Background" or "Surface" or "Text") || pair.Value is null || !System.Text.RegularExpressions.Regex.IsMatch(pair.Value, "^#[0-9a-fA-F]{6}$")) throw new InvalidDataException("主题插件只允许四个合法颜色 Token。");
        if (Widgets.Count > 20 || Widgets.Any(w => w is null || w.Title is null || w.Text is null || w.Title.Length > 100 || w.Text.Length > 4000)) throw new InvalidDataException("插件卡片内容过长。");
    }
}
/// <summary>白名单菜单贡献点；只能打开本插件页面，不能任意注入控件或执行代码。</summary>
public sealed record PluginMenuContribution(string Location, string Label, string Action = "open-page");
public sealed record PluginPageDefinition(string Title, string Description, IReadOnlyList<PluginPageWidget> Widgets, IReadOnlyList<PluginFlow>? Flows = null);
public sealed record PluginPageWidget(string Type, string Title, string Text, SnakeGameOptions? Snake,
    IReadOnlyList<PluginActionButton>? Actions = null, PluginPetOptions? Pet = null);
public sealed record PluginHostAction(string Kind, string Value = "");
public sealed record PluginActionButton(string Label, PluginHostAction Action);
public sealed record PluginFlow(string Event, PluginHostAction Action, int IntervalSeconds = 60);
public sealed record PluginPetOptions(string Name = "音乐猫", string Body = "#B8DCF2", string Accent = "#F19BAC", bool Floating = true);
public sealed record SnakeGameOptions(int Columns = 24, int Rows = 18, int TickMilliseconds = 150, bool WrapWalls = false,
    string Background = "#111827", string Snake = "#9CCF92", string Food = "#F0526C");
public sealed class WidgetDefinition { public string Title { get; set; } = ""; public string Text { get; set; } = ""; }
public sealed class ProviderCatalog { public List<ProviderTrack> Tracks { get; set; } = []; public string? NextCursor { get; set; } }
public sealed class ProviderTrack
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = PluginMessages.Get("Library.UnknownArtist");
    public string Album { get; set; } = PluginMessages.Get("Library.UnknownAlbum");
    public double DurationSeconds { get; set; }
    public string Format { get; set; } = "audio";
    public string? CoverBase64 { get; set; }
    public string? Lyrics { get; set; }
}
public sealed class PlaybackSource { public string Kind { get; set; } = "loopback-http"; public string Url { get; set; } = ""; }
public sealed class RpcRequest
{
    public string Jsonrpc { get; set; } = "2.0";
    public long Id { get; set; }
    public string Method { get; set; } = "";
    public Dictionary<string, string> Params { get; set; } = [];
}
