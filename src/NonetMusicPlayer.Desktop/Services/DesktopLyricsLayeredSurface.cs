using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>向 Win32 覆盖层提供显式预乘 BGRA 像素。</summary>
internal sealed class DesktopLyricsLayeredSurface : IDisposable
{
    private nint _dc, _bitmap, _previous, _pixels;
    private PixelSize _size;
    public bool Present(Window window, nint handle)
    {
        if (window.Content is not Control content || window.Bounds.Width <= 0 || window.Bounds.Height <= 0) return false;
        var scale = window.RenderScaling;
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scale)));
        if (size != _size || _dc == 0)
        {
            Dispose();
            _dc = CreateCompatibleDC(0);
            var info = new BitmapInfo { Size = 40, Width = size.Width, Height = -size.Height, Planes = 1, BitCount = 32 };
            _bitmap = CreateDIBSection(_dc, ref info, 0, out _pixels, 0, 0);
            if (_dc == 0 || _bitmap == 0 || _pixels == 0) { Dispose(); return false; }
            _previous = SelectObject(_dc, _bitmap); _size = size;
        }
        using var frame = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        frame.Render(content);
        // 原始指针重载不会转换格式；Win32 必须明确转换为 BGRA 预乘 Alpha，其他渲染器亦同。
        using var buffer = new NativeFramebuffer(_pixels, size, scale);
        frame.CopyPixels(buffer);
        var destination = new NativePoint { X = window.Position.X, Y = window.Position.Y };
        var dimensions = new NativeSize { Width = size.Width, Height = size.Height };
        var source = new NativePoint();
        var blend = new BlendFunction { Alpha = 255, AlphaFormat = 1 };
        return UpdateLayeredWindow(handle, 0, ref destination, ref dimensions, _dc, ref source, 0, ref blend, 2);
    }
    private sealed class NativeFramebuffer(nint address, PixelSize size, double scale) : ILockedFramebuffer
    {
        public nint Address => address;
        public PixelSize Size => size;
        public int RowBytes => checked(size.Width * 4);
        public Vector Dpi => new(96 * scale, 96 * scale);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Premul;
        public void Dispose() { } // DIB 内存由画面对象统一持有和释放。
    }
    public void Dispose()
    {
        if (_dc != 0 && _previous != 0) SelectObject(_dc, _previous);
        if (_bitmap != 0) DeleteObject(_bitmap);
        if (_dc != 0) DeleteDC(_dc);
        _dc = _bitmap = _previous = _pixels = 0; _size = default;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { public byte Operation, Flags, Alpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPixels, YPixels; public uint Used, Important; }
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint hwnd, nint destinationDc, ref NativePoint destination, ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
}
