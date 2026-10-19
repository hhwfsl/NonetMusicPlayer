using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Models;

public sealed partial class TrackItem : ObservableObject
{
    public TrackItem() { }
    public TrackItem(string id, string title, string artist, string album, string filePath, string extension, long fileSize)
    { Id = id; Title = title; Artist = artist; Album = album; FilePath = filePath; Extension = extension; FileSize = fileSize; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = L10n.T("Library.UnknownArtist");
    public string Album { get; set; } = L10n.T("Library.UnknownAlbum");
    public string FilePath { get; set; } = "";
    public string Extension { get; set; } = "";
    public long FileSize { get; set; }
    public double DurationSeconds { get; set; }
    public string? CoverPath { get; set; }
    /// <summary>搜索缩略图尚未建立持久引用；加入歌单或播放时转存到正式封面目录。</summary>
    [JsonIgnore] public bool HasSearchCover { get; set; }
    public string? LyricsSourcePath { get; set; }
    /// <summary>用户主动取消关联后同时屏蔽外部和内嵌歌词，重新手动关联时清除。</summary>
    public bool LyricsDisabled { get; set; }
    public string? ProviderId { get; set; }
    public string? ProviderTrackId { get; set; }
    private string _sourceDeviceKind = "unknown";
    public string SourceDeviceKind { get => _sourceDeviceKind; set { if (SetProperty(ref _sourceDeviceKind, value)) OnPropertyChanged(nameof(SourceText)); } }
    [JsonIgnore] public string SourceText => L10n.T(ProviderId is null ? "Common.Local" : ProviderId.StartsWith("lan:", StringComparison.Ordinal) ? SourceDeviceKind == "desktop" ? "Lan.SourceDesktop" : SourceDeviceKind == "mobile" ? "Lan.SourceMobile" : "Lan.SourceDevice" : "MusicSearch.Cloud");
    [JsonIgnore] public bool IsCloud => ProviderId is not null;
    private bool _isFavorite;
    // 显式持久化属性同时供 JSON 与 MVVM 生成器使用；生成器无法读取彼此生成的成员。
    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (SetProperty(ref _isFavorite, value)) OnPropertyChanged(nameof(FavoriteText)); }
    }
    [ObservableProperty]
    [property: JsonIgnore]
    private int _displayIndex;
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isPlayingHere;
    [JsonIgnore] public string FavoriteText => IsFavorite ? "♥" : "♡";
    [JsonIgnore] public string DurationText => DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "—";
    [JsonIgnore] public string FormatText => Extension.TrimStart('.').ToUpperInvariant();
    [JsonIgnore] public string FileSizeText => $"{FileSize / 1048576d:0.0} MB";
    [JsonIgnore] public IImage? Artwork
    {
        get
        {
            if (_artworkPath != CoverPath || _artwork is null)
            {
                _artworkPath = CoverPath; _artwork = ArtworkCache.Shared.GetImage(CoverPath);
            }
            return _artwork;
        }
    }
    [JsonIgnore] public bool HasArtwork => Artwork is not null;
    private string? _artworkPath;
    private IImage? _artwork;
    public void ReleaseArtwork() { ArtworkCache.Shared.Invalidate(_artworkPath); _artwork = null; _artworkPath = null; }
    /// <summary>同一路径从缩略图更新为高清封面后重新建立绘制代理，避免沿用旧尺寸裁切。</summary>
    public void RefreshArtwork() { ReleaseArtwork(); OnPropertyChanged(nameof(Artwork)); OnPropertyChanged(nameof(HasArtwork)); }
}
