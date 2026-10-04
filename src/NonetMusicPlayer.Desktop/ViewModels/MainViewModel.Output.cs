using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool _switchingOutput;
    public event EventHandler? AudioDevicesChanged;
    private void DevicesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { if (!_disposed) AudioDevicesChanged?.Invoke(this, EventArgs.Empty); });
    private void OutputChanged(object? sender, AudioOutputChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed || e.PlaybackGeneration != _audio.PlaybackGeneration) return;
        IsPlaying = false; _updatingPosition = true;
        try { PlaybackPosition = Math.Clamp(e.Position, 0, PlaybackDuration); } finally { _updatingPosition = false; }
        Settings.DeviceName = e.DeviceName; ResetListeningAnchor(); Save();
        ReportWarning(e.Available ? L10n.T("Playback.AudioOutputChangedPlaybackIsPausedAtTheCurrent") : L10n.T("Playback.NoAudioOutputIsAvailableYourPositionIsSaved"));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    });
    public async Task SwitchOutputAsync(string device)
    {
        if (_switchingOutput) return;
        _switchingOutput = true;
        try
        {
            UpdateListeningStatistics();
            if (_audioLoaded) { _updatingPosition = true; try { PlaybackPosition = _audio.Position.TotalSeconds; } finally { _updatingPosition = false; } _audio.Pause(); }
            IsPlaying = false; ResetListeningAnchor(); Save();
            await Task.Run(() => _audio.DeviceName = device);
            if (!_disposed) { Settings.DeviceName = _audio.DeviceName; StatusText = L10n.T("Playback.OutputDeviceChangedPlaybackPositionWasRetainedPressPlay"); Save(); SettingsChanged?.Invoke(this, EventArgs.Empty); }
        }
        catch (Exception error) { if (!_disposed) ReportError(L10n.T("Playback.AudioOutputCouldNotBeSwitchedYourPlaybackPosition"), error); }
        finally { _switchingOutput = false; }
    }
    private void TrimHistory()
    {
        if (State.History.Count > Settings.HistoryLimit) State.History.RemoveRange(Settings.HistoryLimit, State.History.Count - Settings.HistoryLimit);
        var retained = State.History.ToHashSet(StringComparer.Ordinal);
        var persistent = State.Tracks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        State.RecentTemporaryTracks.RemoveAll(t => !retained.Contains(t.Id) || persistent.Contains(t.Id));
    }
}
