using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private LyricsPlaybackLease? _lyricsPlaybackLease;
    public bool IsLyricsTimingActive => _lyricsPlaybackLease is not null;
    /// <summary>读取音频后端的时钟，避免 250 ms 界面定时器给标注引入额外误差。</summary>
    public double AudioClockSeconds => _audioLoaded ? _audio.Position.TotalSeconds : PlaybackPosition;

    public LyricsPlaybackLease ReserveLyricsPlayback(string pluginId, TrackItem track)
    {
        var plugin = Plugins.Installed.FirstOrDefault(p => p.Id == pluginId && p.Enabled);
        if (plugin?.Permissions.Contains("lyrics-editor") != true || !plugin.Permissions.Contains("player-control"))
            throw new InvalidOperationException(L10n.T("LyricsTiming.PermissionRequired"));
        if (_disposed || _loadingAudio || _switchingOutput || _lyricsPlaybackLease is not null)
            throw new InvalidOperationException(L10n.T("LyricsTiming.Busy"));
        var lease = new LyricsPlaybackLease(this, pluginId, track, _playingList.ToArray(), _explicitQueueIds?.ToArray(), Settings.PlayMode, PlayingSourcePage);
        _lyricsPlaybackLease = lease; _playingList = [track]; _explicitQueueIds = [track.Id];
        OnPropertyChanged(nameof(IsLyricsTimingActive)); return lease;
    }

    private bool RejectLyricsPlaybackChange()
    {
        if (_lyricsPlaybackLease is null) return false;
        ++ErrorRevision; ReportWarning(L10n.T("LyricsTiming.PlaybackRestricted")); return true;
    }

    /// <summary>播放限制是有生命周期的租约；页面卸载和插件禁用也必须释放，不能留下单曲锁。</summary>
    public sealed class LyricsPlaybackLease : IDisposable
    {
        private MainViewModel? _owner;
        private readonly TrackItem[] _queue;
        private readonly string[]? _scope;
        private readonly PlayMode _mode;
        private readonly string _source;
        public TrackItem Track { get; }
        public string PluginId { get; }
        internal bool Starting { get; private set; }
        internal PlayMode SavedMode => _mode;
        internal LyricsPlaybackLease(MainViewModel owner, string pluginId, TrackItem track, TrackItem[] queue, string[]? scope, PlayMode mode, string source)
        { _owner = owner; PluginId = pluginId; Track = track; _queue = queue; _scope = scope; _mode = mode; _source = source; }

        public async Task StartAsync()
        {
            var owner = Owner(); Starting = true;
            try
            {
                await owner.StartTrackAsync(Track, [Track], _source, 0);
                if (_owner is null || !owner.IsPlaying || owner.CurrentTrack?.Id != Track.Id)
                    throw new InvalidOperationException(L10n.T("Commands.PlaybackStartFailed"));
                owner.CompleteOperation("player.play");
            }
            finally { Starting = false; }
        }
        public void Save(string lrc)
        {
            var owner = Owner();
            if (owner.CurrentTrack?.Id != Track.Id) throw new InvalidOperationException(L10n.T("LyricsTiming.PlaybackRestricted"));
            owner.Lyrics.Save(Track.Id, lrc); owner.ReloadLyrics(); owner.CompleteOperation("lyrics.import");
        }
        private MainViewModel Owner() => _owner is { } owner && !owner._disposed && ReferenceEquals(owner._lyricsPlaybackLease, this)
            ? owner : throw new InvalidOperationException(L10n.T("LyricsTiming.SessionEnded"));
        public void Dispose()
        {
            var owner = _owner; _owner = null; if (owner is null || !ReferenceEquals(owner._lyricsPlaybackLease, this)) return;
            // 释放时让正在进行的载入失效；原音频与歌词文件在取消时保持不变。
            Interlocked.Increment(ref owner._playRequest); owner._lyricsPlaybackLease = null;
            if (!owner._disposed)
            {
                owner.UpdateListeningStatistics();
                if (!owner._loadingAudio) { try { owner._audio.Pause(); } catch (Exception error) { AppLog.Warning("LyricsTiming", "结束标注时暂停失败", error); } }
                owner.IsPlaying = false; owner.ResetListeningAnchor();
                owner._playingList = _queue.ToList(); owner._explicitQueueIds = _scope?.ToHashSet(StringComparer.Ordinal);
                owner.Settings.PlayMode = _mode; owner.PlayingSourcePage = _source;
                owner.OnPropertyChanged(nameof(IsLyricsTimingActive)); owner.OnPropertyChanged(nameof(ModeText)); owner.OnPropertyChanged(nameof(ModeIcon)); owner.OnPropertyChanged(nameof(ModeDescription));
                owner.Save();
            }
        }
    }
}
