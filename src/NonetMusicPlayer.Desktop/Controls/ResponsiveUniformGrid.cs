using Avalonia;
using Avalonia.Controls.Primitives;

namespace NonetMusicPlayer.Desktop.Controls;

public sealed class ResponsiveUniformGrid : UniformGrid
{
    public double MinimumCellWidth { get; set; } = 330;
    public int MaximumColumns { get; set; } = 2;
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = double.IsFinite(availableSize.Width) ? Math.Clamp((int)(availableSize.Width / MinimumCellWidth), 1, MaximumColumns) : MaximumColumns;
        if (Columns != columns) Columns = columns;
        return base.MeasureOverride(availableSize);
    }
}
