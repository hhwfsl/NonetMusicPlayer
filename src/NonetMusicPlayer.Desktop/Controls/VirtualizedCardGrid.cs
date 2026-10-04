using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>按视口回收独立等宽卡片，不将整行卡片作为可选项。</summary>
public sealed class VirtualizedCardGrid : ScrollViewer, IDisposable
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<int, Control> _realized = [];
    private readonly int _count;
    private readonly Func<int, Control> _factory;
    private int _columns;
    private double _cellWidth;
    private const double CellHeight = 242;
    public int RealizedCount => _realized.Count;
    public VirtualizedCardGrid(int count, Func<int, Control> factory)
    {
        _count = count; _factory = factory; Content = _canvas;
        VerticalContentAlignment = VerticalAlignment.Top; HorizontalContentAlignment = HorizontalAlignment.Left;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        Background = Brushes.Transparent;
        ScrollChanged += (_, _) => Refresh(); SizeChanged += (_, _) => Refresh();
    }
    public void Refresh()
    {
        var width = Viewport.Width > 0 ? Viewport.Width : Bounds.Width;
        if (width <= 0) return;
        var columns = Math.Max(1, (int)(width / 180)); var cellWidth = width / columns;
        var rows = (int)Math.Ceiling(_count / (double)columns);
        _canvas.Width = width; _canvas.Height = rows * CellHeight;
        var first = Math.Max(0, (int)(Offset.Y / CellHeight) - 1) * columns;
        var last = Math.Min(_count, ((int)Math.Ceiling((Offset.Y + Math.Max(Viewport.Height, Bounds.Height)) / CellHeight) + 1) * columns);
        if (_columns != columns) { ClearCards(); _columns = columns; }
        _cellWidth = cellWidth;
        foreach (var index in _realized.Keys.Where(i => i < first || i >= last).ToArray()) Remove(index);
        for (var index = first; index < last; index++)
        {
            if (!_realized.TryGetValue(index, out var card)) { card = _factory(index); _realized.Add(index, card); _canvas.Children.Add(card); }
            card.Width = _cellWidth - 12; card.Height = CellHeight - 12;
            Canvas.SetLeft(card, index % columns * _cellWidth); Canvas.SetTop(card, index / columns * CellHeight);
        }
    }
    private void Remove(int index)
    {
        var card = _realized[index];
        foreach (var image in card.GetVisualDescendants().OfType<Image>()) image.Source = null;
        _canvas.Children.Remove(card); _realized.Remove(index);
    }
    private void ClearCards() { foreach (var index in _realized.Keys.ToArray()) Remove(index); }
    public void Dispose() => ClearCards();
}
