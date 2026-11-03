using Avalonia;
using Avalonia.Controls;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>先收缩搜索区域，再把操作按钮成组换行，避免最后一个图标独占一行。</summary>
public sealed class AdaptiveToolbarPanel : WrapPanel
{
    // 保留旧方案对 WrapPanel 的类型选择器及原生控件引用，内部仅替换测量和排列策略。
    protected override Type StyleKeyOverride => typeof(WrapPanel);
    private const double Gap = 6, RowGap = 8, MinimumFieldWidth = 120, PreferredFieldWidth = 300;
    private readonly List<List<Control>> _rows = [];
    private Control? _field;
    private double _fieldWidth, _fieldHeight;
    private bool _singleRow;

    protected override Size MeasureOverride(Size availableSize)
    {
        _rows.Clear();
        _field = Children.FirstOrDefault() is { IsVisible: true } field ? field : null;
        var actions = Children.Skip(1).Where(c => c.IsVisible).ToList();
        foreach (var control in actions) control.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var actionWidth = WidthOf(actions);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : PreferredFieldWidth + (actions.Count > 0 ? Gap : 0) + actionWidth;
        _singleRow = _field is null || width >= MinimumFieldWidth + (actions.Count > 0 ? Gap : 0) + actionWidth;
        _fieldWidth = _field is null ? 0 : Math.Min(PreferredFieldWidth, Math.Max(0, _singleRow ? width - actionWidth - (actions.Count > 0 ? Gap : 0) : width));
        _field?.Measure(new(_fieldWidth, double.PositiveInfinity));
        _fieldHeight = _field?.DesiredSize.Height ?? 0;
        if (_singleRow)
        {
            if (actions.Count > 0) _rows.Add(actions);
            return new(width, Math.Max(_fieldHeight, HeightOf(actions)));
        }
        foreach (var action in actions)
        {
            if (_rows.Count == 0 || WidthOf(_rows[^1]) + (_rows[^1].Count > 0 ? Gap : 0) + action.DesiredSize.Width > width) _rows.Add([]);
            _rows[^1].Add(action);
        }
        // 极窄区域使用均衡的按钮行，不保留“五个加一个”一类孤立图标行。
        for (var row = _rows.Count - 1; row > 0; row--)
            while (_rows[row - 1].Count > _rows[row].Count + 1 && WidthOf(_rows[row]) + Gap + _rows[row - 1][^1].DesiredSize.Width <= width)
            { var control = _rows[row - 1][^1]; _rows[row - 1].RemoveAt(_rows[row - 1].Count - 1); _rows[row].Insert(0, control); }
        return new(width, _fieldHeight + _rows.Sum(row => HeightOf(row) + RowGap));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_singleRow)
        {
            var height = Math.Max(_fieldHeight, _rows.Count > 0 ? HeightOf(_rows[0]) : 0);
            _field?.Arrange(new(0, (height - _fieldHeight) / 2, _fieldWidth, _fieldHeight));
            if (_rows.Count > 0) ArrangeRow(_rows[0], finalSize.Width, 0, height);
        }
        else
        {
            _field?.Arrange(new(0, 0, _fieldWidth, _fieldHeight));
            var y = _fieldHeight;
            foreach (var row in _rows) { y += RowGap; var height = HeightOf(row); ArrangeRow(row, finalSize.Width, y, height); y += height; }
        }
        return finalSize;
    }
    private static double WidthOf(List<Control> row) => row.Sum(c => c.DesiredSize.Width) + Math.Max(0, row.Count - 1) * Gap;
    private static double HeightOf(List<Control> row) => row.Count == 0 ? 0 : row.Max(c => c.DesiredSize.Height);
    private static void ArrangeRow(List<Control> row, double width, double y, double height)
    {
        var x = Math.Max(0, width - WidthOf(row));
        foreach (var control in row)
        { control.Arrange(new(x, y + (height - control.DesiredSize.Height) / 2, control.DesiredSize.Width, control.DesiredSize.Height)); x += control.DesiredSize.Width + Gap; }
    }
}
