using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private TrayIcon? _tray;
    private NativeMenuItem? _trayOpen, _trayExit;
    private bool _memorySuspended;
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
                _tray.Clicked += (_, _) => RestoreFromTray();
                if (_tray.NativeMenuExporter is null) { _tray.Dispose(); _tray = null; }
            }
            catch (Exception error) { _tray?.Dispose(); _tray = null; AppLog.Warning("Tray", L10n.T("Common.SystemTrayUnavailable"), error); }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                if (WindowState == WindowState.Minimized) SuspendVisualMemory();
                else RestoreVisualMemory();
            }
            if (e.Property == IsVisibleProperty) { if (!IsVisible) SuspendVisualMemory(); else RestoreVisualMemory(); }
        };
        L10n.LanguageChanged += TrayLanguageChanged;
        Closed += (_, _) => { L10n.LanguageChanged -= TrayLanguageChanged; _tray?.Dispose(); _tray = null; };
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
        Dispatcher.UIThread.Post(() =>
        {
            if (!_memorySuspended) return;
            _ = Task.Run(() =>
            {
                if (!_memorySuspended) return;
                // 仅在窗口可见性变化时整理内存，不在播放循环中触发。
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
                AppLog.Info("Memory", "Collected unused objects; active UI and playback retained");
            });
        }, DispatcherPriority.Background);
    }
    private void RestoreVisualMemory()
    {
        if (!_memorySuspended || _vm is null) return;
        _memorySuspended = false; _vm.RefreshPlaybackState();
    }
}
