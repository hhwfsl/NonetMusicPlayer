using NonetMusicPlayer.Desktop.Models;
namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private readonly CancellationTokenSource _lyricsSearchLifetime = new();
    private readonly SemaphoreSlim _autoLyricsGate = new(1);
    public static LyricsQuery LyricsQueryFor(TrackItem track) => new(track.Title, track.Artist, track.Album, track.DurationSeconds);
    /// <summary>在歌单提交后后台补全；不阻塞导入、播放和页面导航，退出时取消待处理请求。</summary>
    private async Task CompleteImportedLyricsAsync(IReadOnlyList<TrackItem> tracks)
    {
        var token = _lyricsSearchLifetime.Token;
        try
        {
            await _autoLyricsGate.WaitAsync(token);
            try
            {
                foreach (var track in tracks)
                {
                    token.ThrowIfCancellationRequested(); if (_disposed || !State.Tracks.Contains(track)) continue;
                    if (await Task.Run(() => !string.IsNullOrWhiteSpace(Lyrics.ReadForTrack(track.Id, track.FilePath)), token)) continue;
                    foreach (var plugin in Plugins.AutomaticLyricsPlugins)
                    {
                        try
                        {
                            var result = await Plugins.MatchLyricsAsync(plugin, LyricsQueryFor(track), token);
                            if (result.Text.Length == 0) { if (result.Warning == "sources-unavailable") ReportWarning(L10n.T("LyricsSearch.SourcesUnavailable")); continue; }
                            // 请求期间可能手动导入歌词、删除歌曲或关闭插件；再次检查，避免过期结果覆盖用户操作。
                            if (_disposed || !plugin.Enabled || !State.Tracks.Contains(track) || !string.IsNullOrWhiteSpace(Lyrics.ReadForTrack(track.Id, track.FilePath))) break;
                            await Plugins.ApplyLyricsAsync(plugin, track, result, Lyrics, Plugins.EmbedsLyrics(plugin), token);
                            if (_disposed) return; if (CurrentTrack?.Id == track.Id) ReloadLyrics(); Save(); break;
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception error) { if (!_disposed && plugin.Enabled) ReportError(L10n.T("LyricsSearch.Failed") + " · " + track.Title, error); }
                    }
                }
            }
            finally { _autoLyricsGate.Release(); }
        }
        catch (OperationCanceledException) { /* 退出或停止插件时不弹错误。 */ }
        catch (Exception error) { if (!_disposed) ReportError(L10n.T("LyricsSearch.Failed"), error); }
    }
    public async Task MatchCurrentLyricsAsync(PluginManifest plugin)
    {
        var track = CurrentTrack; if (track is null) { ReportWarning(L10n.T("Commands.SelectTrack")); return; }
        try
        {
            StatusText = L10n.T("LyricsSearch.Searching");
            var result = await Plugins.MatchLyricsAsync(plugin, LyricsQueryFor(track), _lyricsSearchLifetime.Token);
            if (result.Text.Length == 0) { ReportWarning(L10n.T(result.Warning == "sources-unavailable" ? "LyricsSearch.SourcesUnavailable" : "LyricsSearch.NoMatch")); return; }
            if (_disposed || !State.Tracks.Contains(track) && CurrentTrack?.Id != track.Id) return;
            await Plugins.ApplyLyricsAsync(plugin, track, result, Lyrics, Plugins.EmbedsLyrics(plugin), _lyricsSearchLifetime.Token);
            if (CurrentTrack?.Id == track.Id) ReloadLyrics(); Save(); StatusText = L10n.T("LyricsSearch.Associated"); CompleteOperation("lyrics.import");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed) ReportError(L10n.T("LyricsSearch.Failed"), error); }
    }
}
