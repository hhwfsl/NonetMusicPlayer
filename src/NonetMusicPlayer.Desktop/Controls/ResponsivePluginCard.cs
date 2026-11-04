using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>操作区按实际按钮尺寸换行，窄卡片改为上下排列，给插件文字保留可阅读的宽度。</summary>
public sealed class ResponsivePluginCard : Grid
{
    protected override Type StyleKeyOverride => typeof(Grid);
    private bool? _stacked;
    private double _actionWidth = -1;
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 2 || Children[1] is not WrapPanel actions) return base.MeasureOverride(availableSize);
        foreach (var child in actions.Children) child.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var visible = actions.Children.Where(c => c.IsVisible).ToArray();
        if (visible.Length == 0) return base.MeasureOverride(availableSize);
        var cellWidth = visible.Max(c => c.DesiredSize.Width);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        var budget = width * .42;
        // 至少容纳两个操作；按钮被主题或触摸模式放大时，尽早把操作区移到文字下方。
        var stacked = width < 560 || budget < cellWidth * 2;
        var columns = Math.Clamp((int)((stacked ? width : budget) / cellWidth), 1, visible.Length);
        var rows = (int)Math.Ceiling((double)visible.Length / columns);
        // 均衡分组为三加三或二加二加二，避免最后一个卸载按钮独占一行。
        columns = (int)Math.Ceiling((double)visible.Length / rows);
        var actionWidth = Math.Min(width, columns * cellWidth);
        actions.ItemWidth = cellWidth; actions.MaxWidth = actionWidth;
        if (_stacked != stacked || Math.Abs(_actionWidth - actionWidth) > .1)
        {
            _stacked = stacked; _actionWidth = actionWidth;
            ColumnDefinitions = stacked ? new("*") : new() { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(actionWidth)) };
            RowDefinitions = new(stacked ? "Auto,Auto" : "Auto");
            Children[0].Margin = stacked ? new(0, 0, 0, 14) : new(0, 0, 20, 0);
            Grid.SetColumn(actions, stacked ? 0 : 1); Grid.SetRow(actions, stacked ? 1 : 0);
            actions.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        }
        return base.MeasureOverride(availableSize);
    }
}
