using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

public sealed class CoverCropControl : Control
{
    private readonly CropImage _image;
    private Point? _pointer;
    private double _ratio;
    public Rect Crop { get; private set; }
    public event EventHandler? CropChanged;
    public CoverCropControl(CropImage image, double ratio = 1)
    {
        _image = image; Focusable = true;
        _ratio = Math.Clamp(ratio, .2, 5); var side = MaximumWidth;
        Crop = new Rect((image.Original.Width - side) / 2d, (image.Original.Height - side / _ratio) / 2d, side, side / _ratio);
        Cursor = new Cursor(StandardCursorType.Hand); Focusable = true;
        PointerPressed += (_, e) => { if (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; Focus(); _pointer = e.GetPosition(this); e.Pointer.Capture(this); e.Handled = true; };
        PointerMoved += (_, e) =>
        {
            if (_pointer is not { } previous) return;
            var current = e.GetPosition(this); var scale = Frame.Width / Crop.Width;
            SetCrop(Crop.X - (current.X - previous.X) / scale, Crop.Y - (current.Y - previous.Y) / scale, Crop.Width);
            _pointer = current; e.Handled = true;
        };
        PointerReleased += (_, e) => { _pointer = null; e.Pointer.Capture(null); e.Handled = true; };
        PointerCaptureLost += (_, _) => _pointer = null;
        PointerWheelChanged += (_, e) => { Zoom(ZoomValue * Math.Pow(1.15, e.Delta.Y)); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { SetCrop(Crop.X + (e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0), Crop.Y + (e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0), Crop.Width); e.Handled = true; }
        };
    }
    private double MaximumWidth => Math.Min(_image.Original.Width, _image.Original.Height * _ratio);
    private Rect Frame { get { var width = Math.Max(1, Math.Min(Bounds.Width - 32, (Bounds.Height - 32) * _ratio)); return new Rect((Bounds.Width - width) / 2, (Bounds.Height - width / _ratio) / 2, width, width / _ratio); } }
    public double ZoomValue => MaximumWidth / Crop.Width;
    public void SetAspectRatio(double ratio)
    {
        _ratio = Math.Clamp(ratio, .2, 5); var width = MaximumWidth;
        SetCrop((_image.Original.Width - width) / 2, (_image.Original.Height - width / _ratio) / 2, width);
    }
    public void Zoom(double value)
    {
        var side = MaximumWidth / Math.Clamp(value, 1, 8);
        SetCrop(Crop.X + (Crop.Width - side) / 2, Crop.Y + (Crop.Height - side / _ratio) / 2, side);
    }
    public void SetCrop(double x, double y, double side)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(side)) return;
        side = Math.Clamp(side, Math.Min(Math.Max(1, _ratio), MaximumWidth), MaximumWidth);
        Crop = new Rect(Math.Clamp(x, 0, _image.Original.Width - side), Math.Clamp(y, 0, _image.Original.Height - side / _ratio), side, side / _ratio);
        InvalidateVisual(); CropChanged?.Invoke(this, EventArgs.Empty);
    }
    public override void Render(DrawingContext drawing)
    {
        var frame = Frame; var scale = frame.Width / Crop.Width;
        drawing.FillRectangle(new SolidColorBrush(Color.Parse("#12151D")), new Rect(Bounds.Size));
        using (drawing.PushClip(new Rect(Bounds.Size)))
        {
            drawing.DrawImage(_image.Preview, new Rect(frame.X - Crop.X * scale, frame.Y - Crop.Y * scale, _image.Original.Width * scale, _image.Original.Height * scale));
            var shade = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
            drawing.FillRectangle(shade, new Rect(0, 0, Bounds.Width, frame.Y)); drawing.FillRectangle(shade, new Rect(0, frame.Bottom, Bounds.Width, Bounds.Height - frame.Bottom));
            drawing.FillRectangle(shade, new Rect(0, frame.Y, frame.X, frame.Height)); drawing.FillRectangle(shade, new Rect(frame.Right, frame.Y, Bounds.Width - frame.Right, frame.Height));
            drawing.DrawRectangle(null, new Pen(Brushes.White, 2), frame);
            for (var i = 1; i <= 2; i++) { drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255))), new Point(frame.X + frame.Width * i / 3, frame.Y), new Point(frame.X + frame.Width * i / 3, frame.Bottom)); drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255))), new Point(frame.X, frame.Y + frame.Height * i / 3), new Point(frame.Right, frame.Y + frame.Height * i / 3)); }
        }
    }
}
