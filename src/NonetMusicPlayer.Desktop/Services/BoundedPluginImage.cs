using Avalonia.Media.Imaging;
using SkiaSharp;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>动态插件图片共用限额；在像素分配前拒绝过大图像，不联网、不执行 SVG。</summary>
public static class BoundedPluginImage
{
    public static Bitmap Decode(string base64, int width)
    {
        if (base64.Length is 0 or > 2_000_000) throw new InvalidDataException("Image data exceeds limit.");
        var bytes = Convert.FromBase64String(base64);
        var dimension = Math.Clamp(width, 32, 1024); var tall = false;
        using (var probe = new MemoryStream(bytes))
        using (var codec = SKCodec.Create(probe))
        {
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg)
                || codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192 || (long)codec.Info.Width * codec.Info.Height > 16_000_000)
                throw new InvalidDataException("Unsupported or oversized image.");
            tall = codec.Info.Height > codec.Info.Width; dimension = Math.Min(dimension, tall ? codec.Info.Height : codec.Info.Width);
        }
        // SKCodec 拥有探测流，实际解码使用独立流，避免访问已经关闭的流。
        using var input = new MemoryStream(bytes);
        return tall ? Bitmap.DecodeToHeight(input, dimension) : Bitmap.DecodeToWidth(input, dimension);
    }
}
