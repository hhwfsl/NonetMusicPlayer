using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>按视口回收独立等宽卡片，不将整行卡片作为可选项。</summary>
public sealed class VirtualizedCardGrid : ScrollViewer, IDisposable
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<int, Control> _realized = [];
    private readonly Queue<Control> _spares = new();
    private readonly int _count;
    private readonly Func<int, Control> _factory;
    private readonly Action<Control, int>? _bind;
    private int _columns;
    private double _cellWidth;
    private bool _disposed, _refreshing, _queued;
    private int _first = -1, _last = -1;
    private const double CellHeight = 242;
    public int RealizedCount => _realized.Count;
    public VirtualizedCardGrid(int count, Func<int, Control> factory) : this(count, factory, null) { }
    public VirtualizedCardGrid(int count, Func<int, Control> factory, Action<Control, int>? bind)
    {
        _count = count; _factory = factory; _bind = bind; Content = _canvas;
        VerticalContentAlignment = VerticalAlignment.Top; HorizontalContentAlignment = HorizontalAlignment.Left;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        Background = Brushes.Transparent;
        ScrollChanged += OnScrollChanged; SizeChanged += OnSizeChanged;
    }
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => QueueRefresh();
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => QueueRefresh();
    private void QueueRefresh()
    {
        // 同一帧多次拖动滚动条只处理最后的视口，不为经过的每一行创建控件和封面任务。
        if (_disposed || _queued) return;
        _queued = true;
        void Update() { _queued = false; Refresh(); }
        if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(_ => Update());
        else Dispatcher.UIThread.Post(Update, DispatcherPriority.Render);
    }
    public void Refresh()
    {
        // 布局重排可能再次通知尺寸变化；已退出页面的排队事件不能重新创建卡片。
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            var width = Viewport.Width > 0 ? Viewport.Width : Bounds.Width;
            if (!double.IsFinite(width) || width <= 0) return;
            var columns = Math.Max(1, (int)(width / 180)); var cellWidth = width / columns;
            var rows = (int)Math.Ceiling(_count / (double)columns);
            _canvas.Width = width; _canvas.Height = rows * CellHeight;
            var first = Math.Max(0, (int)(Offset.Y / CellHeight) - 1) * columns;
            var last = Math.Min(_count, ((int)Math.Ceiling((Offset.Y + Math.Max(Viewport.Height, Bounds.Height)) / CellHeight) + 1) * columns);
            if (_columns == columns && _cellWidth == cellWidth && _first == first && _last == last) return;
            _columns = columns; _first = first; _last = last;
            _cellWidth = cellWidth;
            var recycled = new Queue<Control>();
            foreach (var index in _realized.Keys.Where(i => i < first || i >= last).ToArray())
            {
                if (_bind is null) Remove(index);
                else { recycled.Enqueue(_realized[index]); _realized.Remove(index); }
            }
            for (var index = first; index < last; index++)
            {
                if (!_realized.TryGetValue(index, out var card))
                {
                    // 可重新绑定的卡片留在视觉树里复用，避免重复套用方案、注册事件和进入动画。
                    if (recycled.TryDequeue(out card)) _bind!(card, index);
                    else if (_spares.TryDequeue(out card)) { _bind!(card, index); _canvas.Children.Add(card); }
                    else { card = _factory(index); _canvas.Children.Add(card); }
                    _realized.Add(index, card);
                }
                card.Width = Math.Max(0, _cellWidth - 12); card.Height = CellHeight - 12;
                Canvas.SetLeft(card, index % columns * _cellWidth); Canvas.SetTop(card, index / columns * CellHeight);
            }
            while (recycled.TryDequeue(out var unused))
            {
                // 视口首尾行数量会交替变化，留两行无像素的备用控件，避免反复少一行又重建一行。
                foreach (var image in unused.GetVisualDescendants().OfType<ThumbnailImage>()) image.ReleaseDecodedPixels();
                _canvas.Children.Remove(unused); _spares.Enqueue(unused);
            }
            while (_spares.Count > columns * 2) Release(_spares.Dequeue());
        }
        finally { _refreshing = false; }
    }
    private void Remove(int index)
    {
        var card = _realized[index];
        Release(card); _realized.Remove(index);
    }
    private void Release(Control card)
    {
        foreach (var image in card.GetVisualDescendants().OfType<Image>())
        {
            if (image is ThumbnailImage thumbnail) thumbnail.Dispose();
            else image.Source = null;
        }
        _canvas.Children.Remove(card);
    }
    private void ClearCards()
    {
        foreach (var index in _realized.Keys.ToArray()) Remove(index);
        while (_spares.TryDequeue(out var spare)) Release(spare);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        ScrollChanged -= OnScrollChanged; SizeChanged -= OnSizeChanged;
        ClearCards(); Content = null;
    }
}
