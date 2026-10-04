using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>全宽播放进度条，起点和终点均确保滑块完整留在窗口边缘内。</summary>
public sealed class PlayerProgressSlider : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);
    private bool _dragging;
    private double _previewSeconds;
    private readonly TextBlock _previewText = new() { FontSize = 14 };
    private readonly Popup _preview;
    public bool IsSeekingWithPointer => _dragging;
    public string PreviewTime => FormatTime(_previewSeconds);
    /// <summary>预览使用独立浮层，不受播放栏和进度条的裁剪范围限制。</summary>
    public bool IsPreviewVisible => _preview.IsOpen && TopLevel.GetTopLevel(_previewText) is { IsVisible: true };
    public double ThumbRadius { get; set; } = 6;
    public Point ThumbCenter => new(ThumbRadius + Math.Max(0, Bounds.Width - ThumbRadius * 2) * Math.Clamp((Value - Minimum) / Math.Max(.001, Maximum - Minimum), 0, 1), Bounds.Height / 2);
    public PlayerProgressSlider()
    {
        _preview = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Top, VerticalOffset = -8,
            IsHitTestVisible = false, IsLightDismissEnabled = false, WindowManagerAddShadowHint = false,
            Child = new Border { Padding = new Thickness(10, 5), CornerRadius = new CornerRadius(6), Child = _previewText }
        };
        // 浮层必须加入逻辑树才能继承应用主题并实例化 PopupRoot 的呈现模板。
        LogicalChildren.Add(_preview);
        Template = new FuncControlTemplate<Slider>((_, _) => new Border { Background = Brushes.Transparent });
        PropertyChanged += (_, e) => { if (e.Property == ValueProperty || e.Property == MaximumProperty || e.Property == MinimumProperty) InvalidateVisual(); };
        PointerPressed += (_, e) => { if (!IsEnabled || e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; Focus(); _dragging = true; e.Pointer.Capture(this); Move(e.GetPosition(this).X); e.Handled = true; };
        PointerEntered += (_, e) => { if (IsEnabled && e.Pointer.Type == PointerType.Mouse) ShowPreview(e.GetPosition(this).X); };
        PointerMoved += (_, e) => { if (_dragging) { Move(e.GetPosition(this).X); e.Handled = true; } else if (IsEnabled && e.Pointer.Type == PointerType.Mouse) ShowPreview(e.GetPosition(this).X); };
        PointerExited += (_, _) => { if (!_dragging) _preview.IsOpen = false; };
        PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            var x = e.GetPosition(this).X; Move(x); _dragging = false; e.Pointer.Capture(null);
            if (e.Pointer.Type == PointerType.Mouse && IsPointerOver) ShowPreview(x); else _preview.IsOpen = false;
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => { if (_dragging) EndPreview(); };
        DetachedFromVisualTree += (_, _) => EndPreview();
    }
    private void Move(double x)
    {
        SetCurrentValue(ValueProperty, PositionToSeconds(x));
        ShowPreview(x);
    }
    private double PositionToSeconds(double x) => Minimum + Math.Clamp((x - ThumbRadius) / Math.Max(1, Bounds.Width - ThumbRadius * 2), 0, 1) * (Maximum - Minimum);
    private void ShowPreview(double x)
    {
        // 预览以指针位置为唯一来源，悬停不改变播放进度，绑定刷新也不覆盖预览。
        _previewSeconds = PositionToSeconds(x);
        _previewText.Text = PreviewTime;
        _previewText.FontFamily = (TopLevel.GetTopLevel(this) as Window)?.FontFamily ?? FontFamily.Default;
        _previewText.Foreground = Ui.Brush("TextPrimaryBrush");
        ((Border)_preview.Child!).Background = Ui.Brush("SurfaceRaisedBrush");
        // 两端仍完整显示时间，不将浮层中心移到屏幕外。
        _preview.HorizontalOffset = Math.Clamp(x, Math.Min(40, Bounds.Width / 2), Math.Max(40, Bounds.Width - 40)) - Bounds.Width / 2;
        _preview.IsOpen = true;
    }
    private void EndPreview() { _dragging = false; _preview.IsOpen = false; }
    public static string FormatTime(double seconds)
    {
        var time = (long)Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, TimeSpan.MaxValue.TotalSeconds);
        return $"{time / 60}:{time % 60:00}";
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var value = e.Key switch { Key.Left or Key.Down => Value - 1, Key.Right or Key.Up => Value + 1, Key.PageDown => Value - 10, Key.PageUp => Value + 10, Key.Home => Minimum, Key.End => Maximum, _ => double.NaN };
        if (double.IsNaN(value)) { base.OnKeyDown(e); return; }
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum)); e.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        var accent = Application.Current!.Resources["AccentBrush"] as IBrush ?? Brushes.MediumPurple;
        var rail = Application.Current.Resources["DividerBrush"] as IBrush ?? Brushes.Gray;
        var point = ThumbCenter;
        context.DrawLine(new Pen(rail, 2), new Point(0, point.Y), new Point(Bounds.Width, point.Y));
        context.DrawLine(new Pen(accent, 2), new Point(0, point.Y), point);
        context.DrawEllipse(accent, null, point, ThumbRadius, ThumbRadius);
    }
}
