using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private TrayIcon? _tray;
    private NativeMenuItem? _trayOpen, _trayExit;
    private bool _memorySuspended;
    private bool _memoryCachesReleased;
    private CancellationTokenSource? _memoryOptimization;
    private Task _memoryOptimizationTask = Task.CompletedTask;
    public bool TrayAvailable => _tray is not null;
    private void InitializeTrayLifecycle()
    {
        Opened += (_, _) =>
        {
            if (_tray is not null) return;
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime) return;
            try
            {
                _trayOpen = new NativeMenuItem(L10n.T("Common.OpenApplication")); _trayExit = new NativeMenuItem(L10n.T("Common.ExitApplication"));
                _trayOpen.Click += (_, _) => RestoreFromTray(); _trayExit.Click += (_, _) => ExitApplication();
                var menu = new NativeMenu(); menu.Items.Add(_trayOpen); menu.Items.Add(_trayExit);
                _tray = new TrayIcon { Icon = Icon ?? new WindowIcon(AssetLoader.Open(new Uri("avares://Nonet/Assets/icon.ico"))), ToolTipText = "Nonet", Menu = menu, IsVisible = true };
                _tray.Clicked += (_, _) => RestoreFromTray(); BuildExtensionTray();
                if (_tray.NativeMenuExporter is null) { _tray.Dispose(); _tray = null; }
            }
            catch (Exception error) { _tray?.Dispose(); _tray = null; AppLog.Warning("Tray", L10n.T("Common.SystemTrayUnavailable"), error); }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty && e.Property != IsVisibleProperty) return;
            if (!IsVisible || WindowState == WindowState.Minimized) SuspendVisualMemory();
            else RestoreVisualMemory();
        };
        L10n.LanguageChanged += TrayLanguageChanged;
        Closed += (_, _) => { _memoryOptimization?.Cancel(); L10n.LanguageChanged -= TrayLanguageChanged; _tray?.Dispose(); _tray = null; };
    }
    private void TrayLanguageChanged(object? sender, EventArgs e)
    {
        if (_trayOpen is not null) _trayOpen.Header = L10n.T("Common.OpenApplication");
        if (_trayExit is not null) _trayExit.Header = L10n.T("Common.ExitApplication");
    }
    public void RestoreFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        RestoreVisualMemory(); Activate();
    }
    public void ExitApplication()
    {
        if (_vm?.IsMigratingData == true) { _vm.ReportWarning(L10n.T("Storage.AppDataIsBeingCopiedWaitForCompletionBefore")); return; }
        _allowClose = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime) lifetime.Shutdown();
        else Close();
    }
    private void SuspendVisualMemory()
    {
        if (_memorySuspended || _vm?.Settings.OptimizeMemoryWhenMinimized != true) return;
        // 保留页面、歌词布局及封面引用。可见性变化不是导航，不能以重建界面换取内存下降。
        _memorySuspended = true;
        _memoryOptimization?.Cancel();
        var cancellation = _memoryOptimization = new CancellationTokenSource();
        _memoryOptimizationTask = OptimizeHiddenMemoryAsync(cancellation);
    }
    private async Task OptimizeHiddenMemoryAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            // 快速切换窗口不回收缓存；实际持续最小化或隐藏后才执行一次。
            await Task.Delay(800, token);
            if (!_memorySuspended || _vm?.Settings.OptimizeMemoryWhenMinimized != true || IsVisible && WindowState != WindowState.Minimized) return;
            _pageMotion.Cancel(); _lyricsMotion.Cancel();
            foreach (var image in this.GetVisualDescendants().OfType<OriginalArtworkImage>()) image.SuspendDecodedPixels();
            AppBackgroundService.TrimMemory(); ArtworkCache.Shared.Clear();
            _memoryCachesReleased = true;
            await Task.Run(() => HiddenWindowMemory.Collect(token), token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Warning("Memory", "Unable to release hidden-window cache", error); }
        finally
        {
            if (ReferenceEquals(_memoryOptimization, cancellation)) _memoryOptimization = null;
            cancellation.Dispose();
        }
    }
    private void RestoreVisualMemory()
    {
        _memoryOptimization?.Cancel();
        if (!_memorySuspended || _vm is null) return;
        _memorySuspended = false;
        if (_memoryCachesReleased)
        {
            _memoryCachesReleased = false;
            // 原有绘制场景可能没有重新调用代理；恢复像素并标记重绘，不触发外观或布局重建。
            AppBackgroundService.GetImage(InterfaceSettings.BackgroundImagePath);
            this.FindControl<Image>("AppBackgroundImage")?.InvalidateVisual();
            foreach (var image in this.GetVisualDescendants().OfType<OriginalArtworkImage>()) image.ResumeDecodedPixels();
        }
        _vm.RefreshPlaybackState();
    }
}
