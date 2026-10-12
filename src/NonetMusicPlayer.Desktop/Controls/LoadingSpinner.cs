using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>只在可见加载期间运行的低频旋转指示器，使用当前主题色且不截获输入。</summary>
public sealed class LoadingSpinner : Control
{
    public static readonly StyledProperty<IBrush?> BrushProperty = AvaloniaProperty.Register<LoadingSpinner, IBrush?>(nameof(Brush), Brushes.White);
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double _angle;
    public LoadingSpinner()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => { _angle = (_angle + 18) % 360; InvalidateVisual(); };
        AttachedToVisualTree += (_, _) => { if (IsVisible) _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) { if (IsVisible && VisualRoot is not null) _timer.Start(); else _timer.Stop(); } };
    }
    public override void Render(DrawingContext context)
    {
        var radius = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 3); var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        Point At(double angle) => center + new Vector(Math.Cos(angle * Math.PI / 180) * radius, Math.Sin(angle * Math.PI / 180) * radius);
        var arc = new StreamGeometry();
        using (var path = arc.Open()) { path.BeginFigure(At(_angle), false); path.ArcTo(At(_angle + 270), new(radius, radius), 0, true, SweepDirection.Clockwise); path.EndFigure(false); }
        context.DrawGeometry(null, new Pen(Brush, 3, lineCap: PenLineCap.Round), arc);
    }
}
