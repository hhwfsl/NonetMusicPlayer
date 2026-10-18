using SkiaSharp;

// 原图不重绘、不裁掉内容；只为系统图标生成统一圆角、透明留白与必要尺寸。
var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "../../../../.."));
using var source = SKImage.FromEncodedData(Path.Combine(root, "assets/icons/app-icon-source.png")) ?? throw new InvalidDataException("Invalid icon source.");
var sizes = new[] { 16, 24, 32, 48, 64, 128, 256, 512, 1024 };
var images = new Dictionary<int, byte[]>();
foreach (var size in sizes)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas; canvas.Clear(SKColors.Transparent);
    var inset = size * .035f; var rect = new SKRect(inset, inset, size - inset, size - inset);
    canvas.ClipRoundRect(new SKRoundRect(rect, rect.Width * .20f), SKClipOperation.Intersect, true);
    canvas.DrawImage(source, rect, new SKSamplingOptions(SKCubicResampler.Mitchell));
    using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    images[size] = png.ToArray();
    if (size >= 256) File.WriteAllBytes(Path.Combine(root, $"assets/icons/native/app-icon-{size}.png"), images[size]);
}
// PNG 载荷 ICO 同时覆盖 EXE、窗口、托盘与关联图标，避免残留旧原生资源。
var iconSizes = sizes.Where(s => s <= 256).ToArray();
using var ico = new MemoryStream(); using var writer = new BinaryWriter(ico);
writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)iconSizes.Length);
var offset = 6 + iconSizes.Length * 16;
foreach (var size in iconSizes)
{
    writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
    writer.Write(images[size].Length); writer.Write(offset); offset += images[size].Length;
}
foreach (var size in iconSizes) writer.Write(images[size]);
File.WriteAllBytes(Path.Combine(root, "src/NonetMusicPlayer.Desktop/Assets/icon.ico"), ico.ToArray());
Console.WriteLine("PASS rounded source-preserving PNG/ICO: " + string.Join(", ", sizes));
