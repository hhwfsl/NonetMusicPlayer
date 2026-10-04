using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;
using System.Diagnostics;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>保留文本塑形的歌词呈现器：逐字变色，页内换行，桌面固定视口横向滚动。</summary>
public sealed class KaraokeLine : Control
{
    private string _text = "";
    private const double Inset = 4;
    private FormattedText? _pending;
    private Geometry? _glyphs, _completed;
    private readonly Dictionary<int, Rect> _elementBounds = [];
    private int[] _elements = [];
    private int _completedLength = -1;
    private FontFamily? _family;
    private double _layoutWidth = -1;
    private Color _textColor = Colors.White;
    public bool Wrap { get; set; } = true;
    private Color? _pendingColor;
    public Color? PendingColor { get => _pendingColor; set { if (_pendingColor == value) return; _pendingColor = value; VisualRevision++; InvalidateVisual(); } }
    private double _characters, _marquee;
    private long _textStarted = Stopwatch.GetTimestamp();
    public long VisualRevision { get; private set; }
    public Color TextColor
    {
        get => _textColor;
        set { if (_textColor == value) return; _textColor = value; VisualRevision++; InvalidateVisual(); }
    }
    private double _textSize = 28;
    public double TextSize { get => _textSize; set { if (Math.Abs(_textSize - value) < .01) return; _textSize = value; ResetLayout(); VisualRevision++; InvalidateMeasure(); InvalidateVisual(); } }
    private void ResetLayout()
    {
        _pending = null; _glyphs = _completed = null; _completedLength = -1; _elementBounds.Clear();
    }
    public Size NaturalSize(double size)
    {
        var text = Format(size, double.PositiveInfinity, Brushes.White); return new(text.Width, text.Height);
    }
    private FormattedText Format(double size, double width, IBrush brush)
    {
        var family = (TopLevel.GetTopLevel(this) as Window)?.FontFamily ?? FontFamily.Default;
        var text = new FormattedText(_text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyle.Normal, FontWeight.Bold), size, brush) { TextAlignment = TextAlignment.Center };
        if (Wrap && double.IsFinite(width)) text.MaxTextWidth = Math.Max(1, width - Inset * 2);
        return text;
    }
    public Size WrappedSize(double width)
    {
        if (string.IsNullOrWhiteSpace(_text)) return default;
        var layout = GetTextLayout(width); return new(layout.Text.Width + Inset * 2, layout.Text.Height + Inset * 2);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = WrappedSize(availableSize.Width);
        return new(double.IsFinite(availableSize.Width) ? Math.Min(size.Width, availableSize.Width) : size.Width, size.Height);
    }
    public double Progress { get; private set; }
    public string Text => _text;
    // 原生输入与裁剪区域使用当前文本布局，不能依赖可能已被旧区域裁掉的上一帧。
    public Rect TextBounds => GetTextLayout(Bounds.Width).Region;
    public void Update(string text, double progress)
    {
        if (_text == text && Progress == Math.Clamp(progress, 0, 1)) return;
        if (_text != text) { _text = text; _elements = StringInfo.ParseCombiningCharacters(text); _textStarted = Stopwatch.GetTimestamp(); ResetLayout(); InvalidateMeasure(); }
        Progress = Math.Clamp(progress, 0, 1); _characters = _text.Length; VisualRevision++; InvalidateVisual();
    }
    public void UpdateTimed(string text, IReadOnlyList<LyricWord> words, double time, bool active = true, double? characterProgress = null)
    {
        Update(text, 0);
        var characters = !active ? 0 : characterProgress ?? (words.Count > 0 ? LyricsService.CharacterProgress(words, time) : text.Length);
        characters = Math.Clamp(characters, 0, text.Length);
        if (Math.Abs(_characters - characters) > .001) { _characters = characters; VisualRevision++; InvalidateVisual(); }
        if (!Wrap)
        {
            var excess = Math.Max(0, GetTextLayout(Bounds.Width).Text.Width + Inset * 2 - Bounds.Width);
            var travel = excess / 36;
            var elapsed = Stopwatch.GetElapsedTime(_textStarted).TotalSeconds;
            var cycle = travel > 0 ? elapsed % (travel * 2 + 3) : 0;
            var offset = cycle < 1.5 ? 0 : cycle < 1.5 + travel ? (cycle - 1.5) * 36 : cycle < 3 + travel ? excess : Math.Max(0, excess - (cycle - 3 - travel) * 36);
            if (Math.Abs(offset - _marquee) > .1) { _marquee = offset; VisualRevision++; InvalidateVisual(); }
        }
    }
    private (FormattedText Text, Point Origin, Rect Region) GetTextLayout(double width)
    {
        var family = (TopLevel.GetTopLevel(this) as Window)?.FontFamily ?? FontFamily.Default;
        if (_layoutWidth != width || !Equals(family, _family)) { _layoutWidth = width; _family = family; ResetLayout(); }
        _pending ??= Format(TextSize, width, new SolidColorBrush(TextColor));
        var x = Wrap ? Inset : Math.Max(Inset, (Bounds.Width - _pending.Width) / 2) - _marquee;
        var y = Math.Max(Inset, (Bounds.Height - _pending.Height) / 2);
        // MaxTextWidth 对齐换行文本；输入区域只覆盖实际字形，非悬停时不能截获整行空白。
        var left = Wrap ? Math.Max(0, (Bounds.Width - _pending.Width) / 2 - 2) : x - 2;
        var region = string.IsNullOrWhiteSpace(_text) || Bounds.Width <= 0 || Bounds.Height <= 0 ? default :
            new Rect(left, y - 2, _pending.Width + 4, _pending.Height + 4).Intersect(new Rect(Bounds.Size));
        return (_pending, new Point(x, y), region);
    }
    /// <summary>已唱部分的空间裁剪区；当前文本元素始终从左边界向右填充，不做整字透明度渐变。</summary>
    public Geometry ActiveClip
    {
        get
        {
            var (text, _, _) = GetTextLayout(Bounds.Width);
            var completed = 0; var partial = -1;
            foreach (var start in _elements)
            {
                if (start >= _characters) break;
                var next = Array.BinarySearch(_elements, start) + 1;
                var end = next < _elements.Length ? _elements[next] : _text.Length;
                if (_characters >= end) completed = end; else { partial = start; break; }
            }
            if (_completedLength != completed)
            {
                _completedLength = completed;
                _completed = completed > 0 ? text.BuildHighlightGeometry(default, 0, completed) : null;
            }
            var clip = new GeometryGroup { FillRule = FillRule.NonZero };
            if (_completed is not null) clip.Children.Add(_completed);
            if (partial >= 0)
            {
                var index = Array.BinarySearch(_elements, partial);
                var end = index + 1 < _elements.Length ? _elements[index + 1] : _text.Length;
                if (!_elementBounds.TryGetValue(partial, out var region))
                    _elementBounds[partial] = region = text.BuildHighlightGeometry(default, partial, end - partial)?.Bounds ?? default;
                var fraction = Math.Clamp((_characters - partial) / (end - partial), 0, 1);
                if (region.Width > 0 && region.Height > 0) clip.Children.Add(new RectangleGeometry(new Rect(region.X, region.Y, region.Width * fraction, region.Height)));
            }
            return clip;
        }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var (text, origin, _) = GetTextLayout(Bounds.Width);
        if (string.IsNullOrWhiteSpace(_text)) return;
        var pending = PendingColor ?? Color.FromArgb(220, (byte)((TextColor.R + 130) / 2), (byte)((TextColor.G + 130) / 2), (byte)((TextColor.B + 130) / 2));
        // LCD 抗锯齿假设背景不透明；透明覆盖层使用灰度 Alpha。
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        {
            // 缓存完整塑形字形，只移动裁剪边界；透明窗口不叠画偏移描边，避免阴影和重影。
            _glyphs ??= text.BuildGeometry(default);
            if (_glyphs is null) return;
            using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
            {
                context.DrawGeometry(new SolidColorBrush(pending), null, _glyphs);
                if (_characters > 0) using (context.PushGeometryClip(ActiveClip)) context.DrawGeometry(new SolidColorBrush(TextColor), null, _glyphs);
            }
        }
    }
}
