using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace NonetMusicPlayer.Desktop.Services;

public sealed class CropImage(Bitmap preview, PixelSize original) : IDisposable
{
    public Bitmap Preview { get; } = preview;
    public PixelSize Original { get; } = original;
    public void Dispose() => Preview.Dispose();
}
public static class CoverCropService
{
    public const long MaximumFileBytes = 20L * 1024 * 1024;
    public const long MaximumPixels = 64L * 1024 * 1024;
    public static CropImage Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumFileBytes) throw new InvalidDataException(L10n.T("Common.ImagesMustNotExceedMB"));
        int width, height;
        using (var probe = File.OpenRead(path))
        using (var codec = SKCodec.Create(probe))
        {
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > MaximumPixels) throw new InvalidDataException(L10n.T("Common.InvalidImageFormatOrExcessivePixelDimensions"));
            width = codec.Info.Width; height = codec.Info.Height;
        }
        using var stream = File.OpenRead(path);
        var preview = width >= height ? Bitmap.DecodeToWidth(stream, Math.Min(1600, width), BitmapInterpolationMode.HighQuality) : Bitmap.DecodeToHeight(stream, Math.Min(1600, height), BitmapInterpolationMode.HighQuality);
        return new(preview, new(width, height));
    }
    public static string Export(AppStorage storage, CropImage image, Rect crop, bool background = false)
    {
        if (crop.Width < 1 || crop.Height < 1 || crop.X < 0 || crop.Y < 0 || crop.Right > image.Original.Width + .01 || crop.Bottom > image.Original.Height + .01) throw new InvalidDataException(L10n.T("Common.TheCropIsOutsideTheImage"));
        var folder = Path.Combine(storage.ArtworkFolder, background ? "Backgrounds" : "Crops"); DataDirectoryService.RejectLinkedAncestors(folder); Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "crop-" + Guid.NewGuid().ToString("N") + ".png");
        var width = background ? Math.Min(1600, Math.Max(1, (int)Math.Round(image.Preview.PixelSize.Width * crop.Width / image.Original.Width))) : 320;
        var height = background ? Math.Max(1, (int)Math.Round(width * crop.Height / crop.Width)) : 320;
        if (height > 1600) { width = Math.Max(1, (int)Math.Round(width * 1600d / height)); height = 1600; }
        using var result = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        var size = image.Preview.Size;
        var source = new Rect(crop.X / image.Original.Width * size.Width, crop.Y / image.Original.Height * size.Height, crop.Width / image.Original.Width * size.Width, crop.Height / image.Original.Height * size.Height);
        using (var drawing = result.CreateDrawingContext()) drawing.DrawImage(image.Preview, source, new Rect(0, 0, width, height));
        result.Save(target, PngBitmapEncoderOptions.Default); return target;
    }
}
