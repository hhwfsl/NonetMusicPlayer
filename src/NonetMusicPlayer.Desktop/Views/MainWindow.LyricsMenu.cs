using Avalonia.Controls;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    /// <summary>歌词操作统一加入播放栏，保留既有 lyrics.more 扩展位置供插件贡献菜单。</summary>
    private void AppendLyricsMenu(ContextMenu menu)
    {
        if (_vm is null) return;
        var import = new MenuItem { Name = "PlayerImportLyrics", Header = L10n.T("Lyrics.ImportLyrics"), Icon = new VectorIcon { Kind = IconKind.FileAdd }, IsEnabled = _vm.CurrentTrack is not null };
        import.Click += async (_, _) =>
        {
            var paths = await OpenFilesAsync(L10n.T("Lyrics.SelectLyrics"), ["*.lrc", "*.txt"], false);
            if (paths.Length == 0) return;
            try
            {
                if (_vm.CurrentTrack is not null && _vm.Lyrics.Read(_vm.CurrentTrack.Id).Length > 0 && !await PlayerDialog.Confirm(this,
                    L10n.T("Lyrics.ReplaceCurrentLyrics"), L10n.T("Lyrics.ReplaceExistingLyricsWithTheSelectedFileTheMusic"), L10n.T("Common.Replace"))) return;
                _vm.ImportLyrics(paths[0]);
            }
            catch (Exception error) { _vm.ReportError(L10n.T("Lyrics.LyricsImportFailed"), error); }
        };
        var reveal = new MenuItem { Header = L10n.T("Lyrics.ShowLyricFileInFolder"), Icon = new VectorIcon { Kind = IconKind.Folder }, IsEnabled = _vm.CurrentTrack is { } track && _vm.Lyrics.ExistingPath(track.Id) is not null };
        reveal.Click += (_, _) => { try { if (_vm.CurrentTrack is { } current && _vm.Lyrics.ExistingPath(current.Id) is { } path) SongInfoDialog.RevealFile(path); } catch (Exception error) { _vm.ReportError(L10n.T("Lyrics.UnableToLocateTheLyricFile"), error); } };
        var unlink = new MenuItem { Header = L10n.T("Lyrics.Unlink"), Icon = new VectorIcon { Kind = IconKind.Close }, IsEnabled = _vm.CurrentTrack is not null };
        unlink.Click += (_, _) => _vm.CancelLyricsAssociation();
        MenuItem Shift(string key, double seconds)
        {
            var item = new MenuItem { Header = L10n.T(key), IsEnabled = _vm.CurrentTrack is not null };
            item.Click += (_, _) => { _vm.Settings.LyricOffset = Math.Clamp(_vm.Settings.LyricOffset + seconds, -30, 30); _vm.ApplySettings(); };
            return item;
        }
        var reset = new MenuItem { Header = L10n.T("Lyrics.ResetLyricsOffset"), IsEnabled = _vm.CurrentTrack is not null };
        reset.Click += (_, _) => { _vm.Settings.LyricOffset = 0; _vm.ApplySettings(); };
        foreach (var item in new object[] { import, reveal, unlink, new Separator(), Shift("Lyrics.LyricsSecondsEarlier", .5), Shift("Lyrics.LyricsSecondsLater", -.5), reset }) menu.Items.Add(item);
        foreach (var plugin in _vm.Plugins.Installed.Where(p => p.Enabled && p.Type is "ui" or "lyrics"))
            foreach (var contribution in plugin.MenuContributions.Where(c => c.Location == "lyrics.more"))
            {
                var item = new MenuItem { Header = contribution.Label, Icon = new VectorIcon { Kind = IconKind.Edit } };
                item.Click += async (_, _) => { if (contribution.Action == "match-lyrics") await _vm.MatchCurrentLyricsAsync(plugin); else _vm.Navigate("plugin:" + plugin.Id); };
                menu.Items.Add(item);
            }
        AddExtensionMenu(menu, "lyrics.more", new System.Text.Json.Nodes.JsonObject { ["trackId"] = _vm.CurrentTrack?.Id });
    }
}
