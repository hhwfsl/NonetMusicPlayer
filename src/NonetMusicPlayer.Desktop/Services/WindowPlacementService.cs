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

    /// <summary>记忆尺寸随当前工作区约束，避免换屏后窗口落到屏幕之外。</summary>
    public static Size FitSize(Size desired, Size workArea)
    {
        var minimum = MinimumSize(workArea);
        return new(Math.Clamp(desired.Width, minimum.Width, Math.Max(minimum.Width, workArea.Width - 8)),
            Math.Clamp(desired.Height, minimum.Height, Math.Max(minimum.Height, workArea.Height - 8)));
    }
    public static Size StartupSize(Size workArea, double? savedWidth, double? savedHeight)
    {
        var desired = savedWidth is { } width && savedHeight is { } height
            && double.IsFinite(width) && double.IsFinite(height) && width >= 320 && height >= 320
            ? new Size(width, height) : InitialSize(workArea);
        return FitSize(desired, workArea);
    }
    public static PixelPoint CenterPosition(PixelRect workArea, Size logicalSize, double scaling) => new(
        workArea.X + (workArea.Width - (int)Math.Round(logicalSize.Width * scaling)) / 2,
        workArea.Y + (workArea.Height - (int)Math.Round(logicalSize.Height * scaling)) / 2);
}
