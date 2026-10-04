using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

public sealed record ListeningChartPoint(string Label, double Seconds, string? Detail = null);
public enum ListeningChartKind { Line, Bars }

/// <summary>本地统计的轻量矢量绘图控件，支持鼠标和触摸查看数据。</summary>
public sealed class ListeningChart : Control
{
    private readonly IReadOnlyList<ListeningChartPoint> _points;
    private readonly ListeningChartKind _kind;
    private int _hover = -1;
    public ListeningChart(IEnumerable<ListeningChartPoint> points, ListeningChartKind kind)
    {
        _points = points.Take(kind == ListeningChartKind.Line ? 31 : 8).ToArray(); _kind = kind;
        MinHeight = kind == ListeningChartKind.Line ? 230 : Math.Max(190, _points.Count * 44 + 24);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(this, string.Join("; ", _points.Select(point => point.Label + ": " + Views.StatisticsView.Time(point.Seconds))));
        PointerMoved += (_, e) => Inspect(e.GetPosition(this));
        PointerPressed += (_, e) => Inspect(e.GetPosition(this));
        PointerExited += (_, _) => { _hover = -1; InvalidateVisual(); };
    }
    private IBrush Brush(string key) => Application.Current!.Resources[key] as IBrush ?? Brushes.Gray;
    private void Inspect(Point point)
    {
        if (_points.Count == 0) return;
        var index = _kind == ListeningChartKind.Line
            ? (int)Math.Round((point.X - 42) / Math.Max(1, Bounds.Width - 58) * Math.Max(1, _points.Count - 1))
            : (int)((point.Y - 10) / Math.Max(1, (Bounds.Height - 20) / _points.Count));
        index = Math.Clamp(index, 0, _points.Count - 1);
        if (_hover != index) { _hover = index; ToolTip.SetTip(this, _points[index].Label + "\n" + Views.StatisticsView.Time(_points[index].Seconds) + (_points[index].Detail is { Length: > 0 } detail ? "\n" + detail : "")); InvalidateVisual(); }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width; var height = Bounds.Height;
        if (width < 100 || height < 50) return;
        if (_points.Count == 0 || _points.All(point => point.Seconds <= 0))
        {
            Text(context, L10n.T("Common.NoListeningDataYet"), new Point(16, height / 2 - 8), Brush("TextSecondaryBrush"), 13); return;
        }
        var maximum = Math.Max(1, _points.Max(point => double.IsFinite(point.Seconds) ? Math.Max(0, point.Seconds) : 0));
        if (_kind == ListeningChartKind.Bars) RenderBars(context, maximum); else RenderLine(context, maximum);
    }
    private void RenderLine(DrawingContext context, double maximum)
    {
        var plot = new Rect(42, 16, Math.Max(1, Bounds.Width - 58), Math.Max(1, Bounds.Height - 54));
        var textBrush = Brush("TextSecondaryBrush"); var divider = new Pen(Brush("DividerBrush"), 1); var accent = Brush("AccentTextBrush");
        var peak = Math.Max(1, Math.Ceiling(maximum / 60));
        for (var index = 0; index <= 4; index++)
        {
            var y = plot.Bottom - plot.Height * index / 4;
            context.DrawLine(divider, new(plot.X, y), new(plot.Right, y));
            Text(context, (peak * index / 4).ToString("0.#", CultureInfo.CurrentCulture), new(0, y - 7), textBrush, 11);
        }
        Text(context, L10n.T("Common.MinBD957B"), new(0, 0), textBrush, 10);
        var positions = _points.Select((point, index) => new Point(plot.X + plot.Width * index / Math.Max(1, _points.Count - 1), plot.Bottom - plot.Height * Math.Clamp(point.Seconds / (peak * 60), 0, 1))).ToArray();
        var fill = new StreamGeometry(); using (var geometry = fill.Open()) { geometry.BeginFigure(new(plot.Left, plot.Bottom), true); foreach (var point in positions) geometry.LineTo(point); geometry.LineTo(new(plot.Right, plot.Bottom)); geometry.EndFigure(true); }
        using (context.PushOpacity(.12)) context.DrawGeometry(accent, null, fill);
        var line = new StreamGeometry(); using (var geometry = line.Open()) { geometry.BeginFigure(positions[0], false); foreach (var point in positions.Skip(1)) geometry.LineTo(point); geometry.EndFigure(false); }
        context.DrawGeometry(null, new Pen(accent, 2.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
        for (var index = 0; index < positions.Length; index++)
        {
            if (_points.Count <= 8 || index == _hover) context.DrawEllipse(accent, null, positions[index], index == _hover ? 5 : 3, index == _hover ? 5 : 3);
            if (_points.Count <= 8 || index == 0 || index == _points.Count - 1 || index % Math.Max(1, _points.Count / 6) == 0)
            {
                var label = _points[index].Label.Length >= 10 ? _points[index].Label[5..] : _points[index].Label;
                var text = Formatted(label, textBrush, 10); context.DrawText(text, new(Math.Clamp(positions[index].X - text.Width / 2, 0, Bounds.Width - text.Width), plot.Bottom + 12));
            }
        }
        if (_hover >= 0) context.DrawLine(new Pen(accent, 1), new(positions[_hover].X, plot.Y), new(positions[_hover].X, plot.Bottom));
    }
    private void RenderBars(DrawingContext context, double maximum)
    {
        var rowHeight = (Bounds.Height - 20) / _points.Count;
        var labelWidth = Math.Clamp(Bounds.Width * .30, 80, 200); var valueWidth = 78d;
        var start = labelWidth + 8; var fullWidth = Math.Max(1, Bounds.Width - start - valueWidth);
        for (var index = 0; index < _points.Count; index++)
        {
            var point = _points[index]; var y = 10 + rowHeight * index;
            using (context.PushClip(new Rect(0, y, labelWidth, rowHeight))) Text(context, point.Label, new(0, y + rowHeight / 2 - 8), Brush("TextPrimaryBrush"), 12);
            var bar = new Rect(start, y + rowHeight / 2 - 7, Math.Max(2, fullWidth * Math.Clamp(point.Seconds / maximum, 0, 1)), 14);
            using (context.PushOpacity(index == _hover ? 1 : .78)) context.DrawRectangle(Brush("AccentTextBrush"), null, bar, 7, 7);
            Text(context, Views.StatisticsView.Time(point.Seconds), new(start + fullWidth + 10, y + rowHeight / 2 - 7), Brush("TextSecondaryBrush"), 10);
        }
    }
    private static FormattedText Formatted(string text, IBrush brush, double size) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI, Noto Sans CJK SC, Inter"), size, brush);
    private static void Text(DrawingContext context, string text, Point point, IBrush brush, double size) => context.DrawText(Formatted(text, brush, size), point);
}
