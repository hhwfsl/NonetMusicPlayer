using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private void InitializeWindowPlacement()
    {
        if (Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
            var size = WindowPlacementService.InitialSize(area); var minimum = WindowPlacementService.MinimumSize(area);
            MinWidth = minimum.Width; MinHeight = minimum.Height; Width = size.Width; Height = size.Height;
        }
        Opened += (_, _) => FitWindowToScreen();
        ScalingChanged += (_, _) => Dispatcher.UIThread.Post(FitWindowToScreen, DispatcherPriority.Loaded);
        Screens.Changed += ScreensChanged;
        Closed += (_, _) => Screens.Changed -= ScreensChanged;
    }
    private void ScreensChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(FitWindowToScreen, DispatcherPriority.Loaded);
    private void FitWindowToScreen()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen || WindowState != WindowState.Normal) return;
        var area = screen.WorkingArea; var logical = area.Size.ToSize(screen.Scaling);
        var minimum = WindowPlacementService.MinimumSize(logical);
        MinWidth = minimum.Width; MinHeight = minimum.Height;
        Width = Math.Min(Width, logical.Width - 8); Height = Math.Min(Height, logical.Height - 8);
        Position = new PixelPoint(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(Width * screen.Scaling))),
            Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Height * screen.Scaling))));
    }
}
