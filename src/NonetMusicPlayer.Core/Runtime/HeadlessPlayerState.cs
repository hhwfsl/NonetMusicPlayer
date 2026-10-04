using NonetMusicPlayer.Core.Library;
using NonetMusicPlayer.Core.Playback;

namespace NonetMusicPlayer.Core.Runtime;

/// <summary>CLI 的独立持久化状态。没有图形对象，也不读取桌面版的数据目录。</summary>
public sealed class HeadlessPlayerState
{
    public string Host { get; set; } = "cli";
    public int SchemaVersion { get; set; } = 1;
    public List<StoredMusicTrack> Tracks { get; set; } = [];
    public List<StoredMusicTrack> RecentTemporaryTracks { get; set; } = [];
    public List<StoredPlaylist> Playlists { get; set; } = [new() { Id = "liked", Name = "Playlists.LikedSongs" }];
    public List<string> History { get; set; } = [];
    public List<StoredListeningEntry> ListeningEntries { get; set; } = [];
    public HeadlessPlayerSettings Settings { get; set; } = new();
    public string? LastTrackId { get; set; }
    public string? LastPlaylistId { get; set; }
    public double LastPosition { get; set; }
}
public sealed class StoredMusicTrack
{
    public MusicMetadata Metadata { get; set; } = new("", "", "", "", "", "", 0, 0, null);
    public string? ProviderId { get; set; }
    public string? ProviderTrackId { get; set; }
    public string Id => Metadata.Id;
}
public sealed class StoredPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? CoverPath { get; set; }
    public List<string> TrackIds { get; set; } = [];
}
public sealed class StoredListeningEntry
{
    public string Date { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public double Seconds { get; set; }
    public int PlayCount { get; set; }
}
public sealed class HeadlessPlayerSettings
{
    public double Volume { get; set; } = 80;
    public string DeviceName { get; set; } = "Playback.SystemDefault";
    public PlaybackMode PlayMode { get; set; } = PlaybackMode.RepeatAll;
    public string Language { get; set; } = "zh-CN";
    public int HistoryLimit { get; set; } = 1000;
    public int TerminalScrollbackLines { get; set; } = 1000;
    public string TerminalMinimumLogLevel { get; set; } = "INFO";
    public string LyricsFolder { get; set; } = "";
    public string BackupFolder { get; set; } = "";
    public void Validate()
    {
        if (!double.IsFinite(Volume) || Volume is < 0 or > 100) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.VolumeRange"));
        if (HistoryLimit is < 0 or > 100000) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.HistoryRange"));
        Commands.SettingsContract.Validate("terminalScrollbackLines", System.Text.Json.Nodes.JsonValue.Create(TerminalScrollbackLines));
        Commands.SettingsContract.Validate("terminalMinimumLogLevel", System.Text.Json.Nodes.JsonValue.Create(TerminalMinimumLogLevel));
        if (Language is not ("zh-CN" or "en-US" or "ja-JP")) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnsupportedLanguage"));
        if (!Enum.IsDefined(PlayMode)) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnsupportedMode"));
        if (string.IsNullOrWhiteSpace(DeviceName)) DeviceName = "Playback.SystemDefault";
    }
}
