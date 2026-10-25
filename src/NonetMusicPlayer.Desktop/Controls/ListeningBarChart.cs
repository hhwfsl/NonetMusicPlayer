using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>固定 7/24 个数据点的轻量图表；不创建每根柱子的控件和计时器。</summary>
public sealed class ListeningBarChart : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ListeningBarChart, IBrush?>(nameof(Fill), Brushes.SteelBlue);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<ListeningBarChart, IBrush?>(nameof(Foreground), Brushes.Gray);
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IReadOnlyList<ListeningBucketPoint> Points { get; private set; } = [];
    public bool Duration { get; init; }
    static ListeningBarChart() => AffectsRender<ListeningBarChart>(FillProperty, ForegroundProperty);
    public void Update(IReadOnlyList<ListeningBucketPoint> points)
    {
        Points = points;
        Avalonia.Automation.AutomationProperties.SetName(this, string.Join("; ", points.Select(p => p.Detail + ": " + (Duration ? Views.StatisticsView.Time(p.Value) : L10n.Format("Statistics.Plays", p.Value)))));
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Points.Count == 0 || Bounds.Width < 90 || Bounds.Height < 64) return;
        var compact = Bounds.Height < 100;
        var font = new Typeface(GetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty));
        FormattedText Text(string value) => new(value, L10n.Culture, FlowDirection.LeftToRight, font, compact ? 10 : 12, Foreground);
        var maximum = Math.Max(0, Points.Max(p => p.Value));
        // 时长以分钟/小时的自然刻度取整，次数采用 1/2/5 的刻度，避免出现小数次。
        double ceiling;
        if (Duration) ceiling = new double[] { 60, 300, 600, 1800, 3600, 7200, 14400, 28800, 86400 }.FirstOrDefault(v => v >= maximum, Math.Ceiling(maximum / 86400) * 86400);
        else { var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, maximum)))); ceiling = new[] { 1d, 2, 5, 10 }.Select(n => n * magnitude).First(n => n >= Math.Max(1, maximum)); }
        var maxText = Text(Duration ? Views.StatisticsView.Time(ceiling) : ceiling.ToString("0", L10n.Culture));
        var left = Math.Max(compact ? 24 : 32, maxText.Width + 12); var bottom = Bounds.Height - (compact ? 22 : 30); var top = compact ? 5d : 12d;
        context.DrawText(maxText, new(0, top)); context.DrawText(Text("0"), new(0, bottom - (compact ? 10 : 12)));
        var slot = (Bounds.Width - left - 4) / Points.Count;
        for (var i = 0; i < Points.Count; i++)
        {
            var point = Points[i]; var width = Math.Max(2, slot * (Points.Count > 7 ? .64 : .6));
            var height = Math.Max(1.5, (bottom - top) * Math.Clamp(point.Value / ceiling, 0, 1));
            var x = left + slot * i + (slot - width) / 2;
            context.DrawRectangle(Fill, null, new Rect(x, bottom - height, width, height), Math.Min(4, height / 2), Math.Min(4, height / 2));
            // 小时图仅保留四至六个标签，窄窗口不挤成一团。
            var step = Points.Count <= 7 ? (slot < 39 ? 2 : 1) : (slot < 16 ? 6 : 4);
            if (i % step != 0 && i != Points.Count - 1) continue;
            var label = Text(point.Label); context.DrawText(label, new(left + slot * (i + .5) - label.Width / 2, bottom + (compact ? 6 : 10)));
        }
    }
}
