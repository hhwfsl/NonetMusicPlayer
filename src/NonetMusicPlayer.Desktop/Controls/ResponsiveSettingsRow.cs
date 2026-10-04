using Avalonia;
using Avalonia.Controls;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>窄视口下将标签和编辑器改为上下排列，避免固定宽度导致溢出。</summary>
public sealed class ResponsiveSettingsRow : Grid
{
    public string WideColumns { get; init; } = "*,240";
    private bool? _stacked;
    protected override Size MeasureOverride(Size availableSize)
    {
        var stacked = availableSize.Width < 560;
        if (_stacked != stacked && Children.Count >= 2)
        {
            _stacked = stacked;
            ColumnDefinitions = new(stacked ? "*" : WideColumns); RowDefinitions = new(stacked ? "Auto,Auto" : "Auto");
            var labels = Children[0]; var editor = Children[1];
            Grid.SetColumn(editor, stacked ? 0 : 1); Grid.SetRow(editor, stacked ? 1 : 0);
            labels.Margin = stacked ? new(0, 0, 0, 9) : new(0, 0, 25, 0);
            editor.HorizontalAlignment = stacked ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        return base.MeasureOverride(availableSize);
    }
}
