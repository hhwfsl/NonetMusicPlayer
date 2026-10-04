using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string LocalizedTitleForPage(string page)
    {
        if (page == "playlist:" + Playlist.LikedId || page == "favorites") return L10n.T("Playlists.LikedSongs");
        if (page == "library") return L10n.T("Home.Music");
        if (page == "songs") return L10n.T("Home.Songs");
        if (page == "terminal") return L10n.T("Terminal.Title");
        if (page.StartsWith("plugin:", StringComparison.Ordinal)) return Plugins.Installed.FirstOrDefault(p => p.Id == page[7..])?.NavigationLabel ?? L10n.T("Plugins.PluginCenter");
        return page is "history" or "albums" or "artists" or "lyrics" or "settings" or "plugins" or "statistics"
            ? L10n.T(TitleForPage(page)) : TitleForPage(page);
    }

    /// <summary>刷新界面翻译，不修改音乐元数据、用户内容或播放状态。</summary>
    public void RefreshLocalizedText()
    {
        if (!Page.StartsWith("plugin:", StringComparison.Ordinal)) PageTitle = LocalizedTitleForPage(Page);
        foreach (var name in new[] { nameof(TrackCountText), nameof(EmptyStateTitle), nameof(EmptyStateDescription),
            nameof(CurrentTitle), nameof(CurrentArtist), nameof(ModeText), nameof(ModeDescription) })
            OnPropertyChanged(name);
    }
}
