using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _windowSizeSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _windowSizeReady, _applyingWindowPlacement, _windowSizeCaptureQueued, _placementClosed;
    private int _windowPlacementRequest;

    private void InitializeWindowPlacement()
    {
        if (Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
            var size = WindowPlacementService.InitialSize(area); var minimum = WindowPlacementService.MinimumSize(area);
            MinWidth = minimum.Width; MinHeight = minimum.Height; Width = size.Width; Height = size.Height;
        }
        // 框架在此事件之后才调整原生窗口、计算居中并显示；不要在 Opened 后再次移动。
        AddHandler(WindowOpenedEvent, (_, _) => PrepareWindowPlacement());
        Opened += (_, _) =>
        {
            // 托盘重新显示沿用运行期间的位置，只有本次进程第一次打开时居中。
            if (_windowSizeReady) return;
            _windowSizeReady = true;
        };
        Resized += (_, e) =>
        {
            if (!_windowSizeReady || _applyingWindowPlacement || e.Reason == WindowResizeReason.DpiChange || _windowSizeCaptureQueued) return;
            // 等原生状态和布局完成交接，避免最大化/最小化过程中把临时尺寸当成用户尺寸。
            _windowSizeCaptureQueued = true;
            Dispatcher.UIThread.Post(() => { _windowSizeCaptureQueued = false; RememberNormalWindowSize(); }, DispatcherPriority.Loaded);
        };
        _windowSizeSaveTimer.Tick += (_, _) => { _windowSizeSaveTimer.Stop(); _vm?.Save(playbackOnly: true); };
        Closing += (_, _) => RememberNormalWindowSize(scheduleSave: false);
        ScalingChanged += (_, _) => Dispatcher.UIThread.Post(FitWindowToScreen, DispatcherPriority.Loaded);
        Screens.Changed += ScreensChanged;
        Closed += (_, _) => { _placementClosed = true; _windowSizeSaveTimer.Stop(); Screens.Changed -= ScreensChanged; };
    }
    private void PrepareWindowPlacement()
    {
        if (_windowSizeReady || Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        var desired = double.IsFinite(Width) && double.IsFinite(Height) ? new Size(Width, Height) : WindowPlacementService.InitialSize(area);
        var size = WindowPlacementService.FitSize(desired, area); var minimum = WindowPlacementService.MinimumSize(area);
        _applyingWindowPlacement = true;
        try
        {
            MinWidth = minimum.Width; MinHeight = minimum.Height; Width = size.Width; Height = size.Height;
        }
        finally { _applyingWindowPlacement = false; }
    }
    private void CenterInitialWindowFrame()
    {
        // Windows 首帧尚由 DWM 隐藏；使用系统已确认的外框尺寸完成最后的像素取整。
        if (!_placementClosed && Screens.Primary is { } screen)
            Position = WindowPlacementService.CenterPosition(screen.WorkingArea, FrameSize ?? ClientSize, DesktopScaling);
    }
    private void RestoreRememberedWindowSize()
    {
        if (_vm is null || _windowSizeReady || Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        var size = WindowPlacementService.StartupSize(area, _vm.Settings.WindowWidth, _vm.Settings.WindowHeight);
        var minimum = WindowPlacementService.MinimumSize(area);
        MinWidth = minimum.Width; MinHeight = minimum.Height; Width = size.Width; Height = size.Height;
    }
    public void RestoreDefaultWindowSize()
    {
        if (_vm is null || (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;
        var size = WindowPlacementService.FitSize(WindowPlacementService.InitialSize(screen.WorkingArea.Size.ToSize(screen.Scaling)), screen.WorkingArea.Size.ToSize(screen.Scaling));
        WindowState = WindowState.Normal;
        ApplyWindowPlacement(size, screen.WorkingArea, screen.Scaling, center: true);
        // 明确恢复操作立即保存，旧的缩放防抖回调不能覆盖本次默认尺寸。
        _windowSizeSaveTimer.Stop(); SetWindowSizePreference(size); _vm.Save(playbackOnly: true);
    }
    private void ApplyWindowPlacement(Size size, PixelRect area, double scaling, bool center)
    {
        _applyingWindowPlacement = true; var request = ++_windowPlacementRequest;
        var minimum = WindowPlacementService.MinimumSize(area.Size.ToSize(scaling));
        MinWidth = minimum.Width; MinHeight = minimum.Height; Width = size.Width; Height = size.Height;
        if (center) Position = WindowPlacementService.CenterPosition(area, size, scaling);
        Dispatcher.UIThread.Post(() =>
        {
            if (_placementClosed || request != _windowPlacementRequest) return;
            // 使用实际外框大小补偿 DPI 取整，最大化恢复也在同一次原生交接后完成居中。
            Width = size.Width; Height = size.Height; UpdateLayout();
            if (center) Position = WindowPlacementService.CenterPosition(area, FrameSize ?? ClientSize, DesktopScaling);
            _applyingWindowPlacement = false;
        }, DispatcherPriority.Loaded);
    }
    private void SetWindowSizePreference(Size size)
    {
        if (_vm is null) return;
        _vm.Settings.WindowWidth = size.Width; _vm.Settings.WindowHeight = size.Height;
    }
    private void RememberNormalWindowSize(bool scheduleSave = true)
    {
        if (_placementClosed || !_windowSizeReady || _applyingWindowPlacement || _vm is null || !IsVisible || WindowState != WindowState.Normal
            || !double.IsFinite(ClientSize.Width) || !double.IsFinite(ClientSize.Height) || ClientSize.Width < 320 || ClientSize.Height < 320) return;
        if (_vm.Settings.WindowWidth == ClientSize.Width && _vm.Settings.WindowHeight == ClientSize.Height) return;
        SetWindowSizePreference(ClientSize);
        if (scheduleSave) { _windowSizeSaveTimer.Stop(); _windowSizeSaveTimer.Start(); }
    }
    private void ScreensChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(FitWindowToScreen, DispatcherPriority.Loaded);
    private void FitWindowToScreen()
    {
        if (_placementClosed || Screens.ScreenFromWindow(this) is not { } screen || WindowState != WindowState.Normal) return;
        var area = screen.WorkingArea; var logical = area.Size.ToSize(screen.Scaling);
        var minimum = WindowPlacementService.MinimumSize(logical);
        MinWidth = minimum.Width; MinHeight = minimum.Height;
        var size = WindowPlacementService.FitSize(new(Width, Height), logical);
        ApplyWindowPlacement(size, area, screen.Scaling, center: false);
        Position = new PixelPoint(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(Width * screen.Scaling))),
            Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Height * screen.Scaling))));
    }
}
