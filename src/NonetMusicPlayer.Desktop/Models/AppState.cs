using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Models;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public string? ManagedDataRoot { get; set; }
    public List<TrackItem> Tracks { get; set; } = [];
    public List<string> MusicFolders { get; set; } = [];
    public List<Playlist> Playlists { get; set; } = [];
    public List<string> History { get; set; } = [];
    // 仅用于最近播放的临时音频元数据，不属于音乐列表或听歌统计。
    public List<TrackItem> RecentTemporaryTracks { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
    public string? LastTrackId { get; set; }
    public string? LastTemporaryFile { get; set; }
    public bool LastTemporaryCounted { get; set; }
    public double LastPosition { get; set; }
    public string LastSourcePage { get; set; } = "library";
    public string? LastPlaylistId { get; set; }
    public List<ListeningEntry> ListeningEntries { get; set; } = [];
    public Dictionary<string, string> GroupCovers { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> SoftwareDefaultGroupCovers { get; set; } = new(StringComparer.Ordinal);
}
public sealed partial class Playlist : ObservableObject
{
    public const string LikedId = "liked";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = L10n.T("Playlists.NewPlaylist");
    private string _description = "";
    private string? _coverPath;
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string? CoverPath
    {
        get => _coverPath;
        set { var old = _coverPath; if (SetProperty(ref _coverPath, value)) { ArtworkCache.Shared.Invalidate(old); OnPropertyChanged(nameof(Artwork)); OnPropertyChanged(nameof(HasArtwork)); } }
    }
    public List<string> TrackIds { get; set; } = [];
    [JsonIgnore] public bool IsSystem => Id == LikedId;
    [JsonIgnore] public IImage? Artwork => ArtworkCache.Shared.GetImage(CoverPath);
    [JsonIgnore] public bool HasArtwork => Artwork is not null;
    public void ReleaseArtwork() => ArtworkCache.Shared.Invalidate(CoverPath);
    public override string ToString() => Name;
}
// 保持已持久化枚举值兼容；旧值 0 在加载时迁移为列表循环。
public enum PlayMode { RepeatAll = 1, RepeatOne = 2, Shuffle = 3 }
public sealed class AppSettings
{
    public int UiRevision { get; set; }
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "System";
    public string Accent { get; set; } = "#A895FF";
    public string? BackgroundImagePath { get; set; }
    public double BackgroundImageOpacity { get; set; } = .5;
    public double TitleBarOpacity { get; set; } = .15;
    public double NavigationOpacity { get; set; } = .15;
    public double ContentOpacity { get; set; } = .15;
    public double PlayerOpacity { get; set; } = .15;
    public double TerminalOpacity { get; set; } = .85;
    public int TerminalScrollbackLines { get; set; } = 1000;
    public double TerminalFontSize { get; set; } = 14;
    public string TerminalMinimumLogLevel { get; set; } = "INFO";
    public double UiOpacity { get; set; } = 1;
    public string BackgroundImageStretch { get; set; } = "UniformToFill";
    public double ControlCornerRadius { get; set; } = 9;
    [JsonIgnore] public bool TouchMode { get; set; }
    public bool MyMusicExpanded { get; set; } = true;
    public bool PluginsExpanded { get; set; } = true;
    public bool PlaylistsExpanded { get; set; } = true;
    public bool DesktopLyricsVisible { get; set; }
    public int? DesktopLyricsX { get; set; }
    public int? DesktopLyricsY { get; set; }
    public double DesktopLyricsWidth { get; set; } = 840;
    public double DesktopLyricsHeight { get; set; } = 144;
    public double DesktopLyricsFontSize { get; set; } = 28;
    public bool DesktopLyricsLocked { get; set; }
    public bool OptimizeMemoryWhenMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public double FontSize { get; set; } = 13;
    public string FontFamily { get; set; } = "";
    public string? FontFilePath { get; set; }
    public int HistoryLimit { get; set; } = 1000;
    public double SidebarWidth { get; set; } = 208;
    public double PlayerHeight { get; set; } = 96;
    public bool NavigationRight { get; set; }
    public bool PlayerTop { get; set; }
    public bool ConfirmClose { get; set; }
    public bool TitleButtonsOnLeft { get; set; } = OperatingSystem.IsMacOS();
    public Dictionary<string, string> KeyBindings { get; set; } = [];
    public string BackupFolder { get; set; } = "";
    [JsonIgnore] public string? ValidationWarning { get; private set; }
    public double Volume { get; set; } = 80;
    public string DeviceName { get; set; } = "Playback.SystemDefault";
    public PlayMode PlayMode { get; set; } = PlayMode.RepeatAll;
    public string LyricsFolder { get; set; } = "";
    public double LyricOffset { get; set; }
    public Dictionary<string, LayoutRect> Layout { get; set; } = [];
    public void Validate()
    {
        if (UiRevision == 0 && PlayerHeight == 114) PlayerHeight = 96;
        UiRevision = 1;
        ValidationWarning = null;
        if (DeviceName == "系统默认") DeviceName = "Playback.SystemDefault"; Layout ??= []; KeyBindings ??= []; BackupFolder ??= ""; if (string.IsNullOrWhiteSpace(DeviceName)) DeviceName = "Playback.SystemDefault";
        try { ShortcutService.Validate(KeyBindings); }
        catch (InvalidDataException) { KeyBindings = []; ValidationWarning = L10n.T("Settings.InvalidOrConflictingShortcutsWereResetToDefaults"); }
        FontSize = Math.Clamp(FontSize, 11, 18); SidebarWidth = Math.Clamp(SidebarWidth, 170, 320);
        PlayerHeight = Math.Clamp(PlayerHeight, 96, 180); Volume = Math.Clamp(Volume, 0, 100);
        LyricOffset = Math.Clamp(LyricOffset, -60, 60);
        HistoryLimit = Math.Clamp(HistoryLimit, 0, 100000);
        DesktopLyricsWidth = double.IsFinite(DesktopLyricsWidth) ? Math.Clamp(DesktopLyricsWidth, 360, 2000) : 840;
        DesktopLyricsHeight = double.IsFinite(DesktopLyricsHeight) ? Math.Clamp(DesktopLyricsHeight, 120, 2000) : 144;
        DesktopLyricsFontSize = double.IsFinite(DesktopLyricsFontSize) ? Math.Clamp(DesktopLyricsFontSize, 14, 48) : 28;
        FontFamily = (FontFamily ?? "").Trim(); if (FontFamily.Length > 200 || FontFamily.Any(char.IsControl)) FontFamily = "";
        if (!Enum.IsDefined(PlayMode)) PlayMode = PlayMode.RepeatAll;
        if (!double.IsFinite(Volume)) Volume = 80;
        if (!double.IsFinite(FontSize)) FontSize = 13;
        if (!double.IsFinite(SidebarWidth)) SidebarWidth = 208;
        if (!double.IsFinite(PlayerHeight)) PlayerHeight = 96;
        UiOpacity = double.IsFinite(UiOpacity) ? Math.Clamp(UiOpacity, .35, 1) : 1;
        if (!double.IsFinite(LyricOffset)) LyricOffset = 0;
        if (Theme is not ("Dark" or "Light" or "System")) Theme = "System";
        if (Language is not ("zh-CN" or "ja-JP" or "en-US")) Language = "zh-CN";
        if (!double.IsFinite(BackgroundImageOpacity)) BackgroundImageOpacity = .5;
        BackgroundImageOpacity = Math.Clamp(BackgroundImageOpacity, 0, 1);
        static double RegionOpacity(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : .15;
        TitleBarOpacity = RegionOpacity(TitleBarOpacity); NavigationOpacity = RegionOpacity(NavigationOpacity);
        ContentOpacity = RegionOpacity(ContentOpacity); PlayerOpacity = RegionOpacity(PlayerOpacity);
        TerminalOpacity = double.IsFinite(TerminalOpacity) ? Math.Clamp(TerminalOpacity, 0, 1) : .85;
        TerminalScrollbackLines = Math.Clamp(TerminalScrollbackLines, 1, 10000);
        TerminalFontSize = double.IsFinite(TerminalFontSize) ? Math.Clamp(TerminalFontSize, 10, 32) : 14;
        if (TerminalMinimumLogLevel is not ("INFO" or "WARN" or "ERROR")) TerminalMinimumLogLevel = "INFO";
        if (!double.IsFinite(ControlCornerRadius)) ControlCornerRadius = 9;
        ControlCornerRadius = Math.Clamp(ControlCornerRadius, 0, 22);
        if (BackgroundImageStretch is not ("UniformToFill" or "Uniform")) BackgroundImageStretch = "UniformToFill";
        if (!System.Text.RegularExpressions.Regex.IsMatch(Accent ?? "", "^#[0-9a-fA-F]{6}$")) Accent = "#A895FF";
        foreach (var key in Layout.Keys.ToArray())
            if (key is not ("Navigation" or "Content" or "Player" or "Player.Info" or "Player.Transport" or "Player.Options") || Layout[key] is null || !Layout[key].IsValid) Layout.Remove(key);
    }
}
public sealed record LayoutRect(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X + Y + Width + Height) && X >= 0 && Y >= 0 && Width >= .12 && Height >= .1 && X + Width <= 1.001 && Y + Height <= 1.001;
}
