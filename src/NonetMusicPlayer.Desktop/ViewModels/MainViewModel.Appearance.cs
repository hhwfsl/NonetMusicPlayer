using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.ViewModels;

/// <summary>背景取色与界面、终端和插件共用同一路径；过期结果不能覆盖新背景或新设置。</summary>
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _backgroundAccentCancellation;
    private string? _backgroundAccentKey;
    private AppSettings? _backgroundAccentSettings;
    public Task BackgroundAccentUpdate { get; private set; } = Task.CompletedTask;
    public Task<string?> ExtractBackgroundAccentAsync(CancellationToken token = default)
    {
        var path = Settings.BackgroundImagePath;
        return string.IsNullOrWhiteSpace(path) ? Task.FromResult<string?>(null) : Task.Run(() => AppBackgroundService.ExtractAccent(path, token), token);
    }
    private void ScheduleBackgroundAccent()
    {
        var path = Settings.BackgroundImagePath;
        var key = Settings.AccentFromBackground && !string.IsNullOrWhiteSpace(path) ? path : null;
        if (_backgroundAccentKey == key && (ReferenceEquals(_backgroundAccentSettings, Settings) || BackgroundAccentUpdate.IsCompleted)) return;
        _backgroundAccentKey = key; _backgroundAccentSettings = Settings;
        _backgroundAccentCancellation?.Cancel(); _backgroundAccentCancellation?.Dispose(); _backgroundAccentCancellation = null;
        if (key is null) { BackgroundAccentUpdate = Task.CompletedTask; return; }
        var tokenSource = _backgroundAccentCancellation = new();
        var settings = Settings;
        BackgroundAccentUpdate = UpdateAsync();
        async Task UpdateAsync()
        {
            try
            {
                var color = await ExtractBackgroundAccentAsync(tokenSource.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_disposed || tokenSource.IsCancellationRequested || !ReferenceEquals(Settings, settings) || !settings.AccentFromBackground || settings.BackgroundImagePath != key) return;
                    if (color is null) return;
                    settings.Accent = color; Save(); SettingsChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("settings.background-accent");
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed && !tokenSource.IsCancellationRequested) ReportError(L10n.T("Background.AccentFailed"), error); });
            }
        }
    }
}
