using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Plugins;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>使用低频动画的原生矢量桌宠，资源有界且不加载外部内容。</summary>
public sealed class PluginPetControl : Control, IDisposable
{
    private static readonly Geometry BodyGeometry = Geometry.Parse("M50 62L46 18L81 40Q100 32 119 40L154 18L150 62Q176 114 137 128H63Q24 114 50 62Z");
    private static readonly Geometry EarGeometry = Geometry.Parse("M53 28L57 50L72 44Z M147 28L143 50L128 44Z");
    private static readonly Geometry MouthGeometry = Geometry.Parse("M96 90L100 94L104 90 M100 94V99Q92 109 86 100 M100 99Q108 109 114 100");
    private static readonly Pen Outline = new(Brushes.DarkSlateGray, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
    private readonly IBrush _body, _accent;
    private readonly DispatcherTimer _timer;
    private int _frame;
    public bool IsDisposed { get; private set; }
    public bool IsTimerRunning => _timer.IsEnabled;
    public PluginPetControl(PluginPetOptions options)
    {
        Height = 142; MinWidth = 160;
        _body = new SolidColorBrush(Color.Parse(options.Body)); _accent = new SolidColorBrush(Color.Parse(options.Accent));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += Tick; _timer.Start();
        Avalonia.Automation.AutomationProperties.SetName(this, options.Name);
    }
    private void Tick(object? sender, EventArgs e) { _frame = (_frame + 1) % 12; InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / 200, Bounds.Height / 140); if (scale <= 0) return;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - 200 * scale) / 2, (Bounds.Height - 140 * scale) / 2)))
        {
            context.DrawEllipse(Shadow, null, new Point(100, 130), 62, 7);
            context.DrawGeometry(_body, Outline, BodyGeometry);
            context.DrawGeometry(_accent, null, EarGeometry);
            context.DrawEllipse(_accent, null, new Point(65, 93), 10, 5); context.DrawEllipse(_accent, null, new Point(135, 93), 10, 5);
            if (_frame == 0) { context.DrawLine(Outline, new(73, 76), new(85, 76)); context.DrawLine(Outline, new(115, 76), new(127, 76)); }
            else { context.DrawEllipse(Brushes.DarkSlateGray, null, new Point(79, 76), 3, 6); context.DrawEllipse(Brushes.DarkSlateGray, null, new Point(121, 76), 3, 6); }
            context.DrawGeometry(null, Outline, MouthGeometry);
        }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose() { if (IsDisposed) return; IsDisposed = true; _timer.Stop(); _timer.Tick -= Tick; }
}
