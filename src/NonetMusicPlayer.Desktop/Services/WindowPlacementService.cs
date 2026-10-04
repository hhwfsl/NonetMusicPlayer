using Avalonia;

namespace NonetMusicPlayer.Desktop.Services;

public static class WindowPlacementService
{
    // 屏幕工作区排除任务栏，布局尺寸使用逻辑像素，以适应高 DPI、竖屏与低分辨率设备。
    public static Size InitialSize(Size workArea) => new(
        Math.Min(1360, Math.Max(320, workArea.Width * .90)),
        Math.Min(860, Math.Max(320, workArea.Height * .90)));
    public static Size MinimumSize(Size workArea) => new(
        Math.Min(640, Math.Max(320, workArea.Width - 32)),
        Math.Min(480, Math.Max(320, workArea.Height - 32)));
}
