using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace NonetMusicPlayer.Desktop.Controls;

public sealed class ColorWheel : Control, IDisposable
{
    private readonly WriteableBitmap _wheel;
    private double _hue, _saturation, _value = 1;
    public event EventHandler<Color>? ColorChanged;
    public Color SelectedColor => Hsv(_hue, _saturation, _value);
    public ColorWheel()
    {
        Width = Height = 220; Focusable = true;
        const int size = 240;
        _wheel = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = _wheel.Lock(); var pixels = new byte[frame.RowBytes * size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var dx = (x - (size - 1) / 2d) / (size / 2d - 1); var dy = (y - (size - 1) / 2d) / (size / 2d - 1);
            var saturation = Math.Sqrt(dx * dx + dy * dy); if (saturation > 1) continue;
            var color = Hsv((Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360, saturation, 1); var index = y * frame.RowBytes + x * 4;
            pixels[index] = color.B; pixels[index + 1] = color.G; pixels[index + 2] = color.R; pixels[index + 3] = 255;
        }
        Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        PointerPressed += (_, e) => { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; e.Pointer.Capture(this); Select(e.GetPosition(this)); e.Handled = true; };
        PointerMoved += (_, e) => { if (e.Pointer.Captured == this && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Select(e.GetPosition(this)); };
        PointerReleased += (_, e) => { if (e.Pointer.Captured == this) e.Pointer.Capture(null); };
    }
    public void SetColor(Color color)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d; var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        _value = max; _saturation = max == 0 ? 0 : delta / max;
        _hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (_hue < 0) _hue += 360; InvalidateVisual();
    }
    public double Brightness { get => _value; set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); ColorChanged?.Invoke(this, SelectedColor); } }
    private void Select(Point point)
    {
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2; var dx = point.X - Bounds.Width / 2; var dy = point.Y - Bounds.Height / 2;
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360; _saturation = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / radius, 0, 1);
        InvalidateVisual(); ColorChanged?.Invoke(this, SelectedColor);
    }
    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size); context.DrawImage(_wheel, rect);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2; var center = rect.Center;
        if (_value < 1) context.DrawEllipse(new SolidColorBrush(Colors.Black, 1 - _value), null, center, radius - 1, radius - 1);
        var angle = _hue * Math.PI / 180; var marker = center + new Vector(Math.Cos(angle), Math.Sin(angle)) * (radius * _saturation * .97);
        context.DrawEllipse(null, new Pen(Brushes.Black, 4), marker, 5, 5); context.DrawEllipse(null, new Pen(Brushes.White, 2), marker, 5, 5);
    }
    public static Color Hsv(double hue, double saturation, double value)
    {
        var c = value * saturation; var x = c * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = value - c;
        var (r, g, b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    public void Dispose() => _wheel.Dispose();
}
