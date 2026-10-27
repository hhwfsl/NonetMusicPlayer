using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>只缓存一张有界背景位图，视图持有不拥有像素资源的绘制代理。</summary>
public static class AppBackgroundService
{
    public const int MaximumDimension = 1600;
    public static readonly string[] MediaPatterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"];
    // 仅用于识别旧配置与清理应用管理副本，不提供这些媒体的播放能力。
    private static readonly string[] RetiredExtensions = [".gif", ".mp4", ".m4v", ".mov", ".webm", ".mkv", ".avi"];
    private static readonly object Gate = new();
    private static Bitmap? _bitmap;
    private static string? _path;
    private static long _length;
    private static DateTime _modified;
    public static long DecodedBytes { get { lock (Gate) return _bitmap is null ? 0 : 4L * _bitmap.PixelSize.Width * _bitmap.PixelSize.Height; } }

    public static string Import(AppStorage storage, string source)
    {
        if (storage.IsMigrating) throw new InvalidOperationException(L10n.T("Settings.AppDataIsBeingCopiedChangeTheBackgroundAfter"));
        var targetFolder = Path.Combine(storage.ArtworkFolder, "Backgrounds"); DataDirectoryService.RejectLinkedAncestors(targetFolder); Directory.CreateDirectory(targetFolder);
        using var input = OpenValidated(source, out var width, out var height);
        using var bitmap = width >= height ? Bitmap.DecodeToWidth(input, Math.Min(width, MaximumDimension), BitmapInterpolationMode.HighQuality) : Bitmap.DecodeToHeight(input, Math.Min(height, MaximumDimension), BitmapInterpolationMode.HighQuality);
        using var hashInput = File.OpenRead(source); var hash = Convert.ToHexString(SHA256.HashData(hashInput))[..24];
        var target = Path.Combine(targetFolder, hash + ".png");
        DataDirectoryService.RejectLinkedAncestors(targetFolder);
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException(L10n.T("Settings.TheBackgroundDestinationCannotBeAFileLink"));
        if (!File.Exists(target))
        {
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) bitmap.Save(output, PngBitmapEncoderOptions.Default);
                DataDirectoryService.RejectLinkedAncestors(targetFolder); File.Move(temporary, target, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return target;
    }
    /// <summary>静态背景入口统一校验，避免动态 WebP/APNG 经裁剪弹窗绕过限制。</summary>
    public static void ValidateSource(string path) { using var input = OpenValidated(path, out _, out _); }
    /// <summary>最多分析 96×96 像素，不缓存原图；过滤透明像素，弱化大片纯白/纯黑对代表色的干扰。</summary>
    public static string? ExtractAccent(string path, CancellationToken token = default)
    {
        using var input = OpenValidated(path, out var width, out var height);
        using var thumbnail = width >= height ? Bitmap.DecodeToWidth(input, Math.Min(width, 96)) : Bitmap.DecodeToHeight(input, Math.Min(height, 96));
        using var readable = new WriteableBitmap(thumbnail.PixelSize, new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using var buffer = readable.Lock(); thumbnail.CopyPixels(buffer);
        var bytes = new byte[checked(buffer.RowBytes * buffer.Size.Height)];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var weights = new double[4096]; var reds = new double[4096]; var greens = new double[4096]; var blues = new double[4096];
        for (var y = 0; y < buffer.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var at = y * buffer.RowBytes + x * 4; var r = bytes[at]; var g = bytes[at + 1]; var b = bytes[at + 2]; var alpha = bytes[at + 3];
                if (alpha < 128) continue;
                var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
                var weight = alpha / 255d * (.04 + Math.Sqrt((max - min) / 255d));
                if (min > 235) weight *= .08; else if (max < 25) weight *= .2;
                var bin = (r >> 4) * 256 + (g >> 4) * 16 + (b >> 4);
                weights[bin] += weight; reds[bin] += r * weight; greens[bin] += g * weight; blues[bin] += b * weight;
            }
        }
        var best = Array.IndexOf(weights, weights.Max());
        if (weights[best] == 0) return null;
        return $"#{(int)Math.Round(reds[best] / weights[best]):X2}{(int)Math.Round(greens[best] / weights[best]):X2}{(int)Math.Round(blues[best] / weights[best]):X2}";
    }
    public static bool IsRetiredMedia(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (RetiredExtensions.Contains(extension)) return true;
        if (extension is not (".webp" or ".png") || !File.Exists(path)) return false;
        try { using var stream = File.OpenRead(path); using var codec = SKCodec.Create(stream); return codec?.FrameCount > 1; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
    public static IImage? GetImage(string? path)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(path)) { Clear(); return null; }
            try
            {
                path = Path.GetFullPath(path); var file = new FileInfo(path);
                if (_path == path && _bitmap is not null && file.Exists && file.Length == _length && file.LastWriteTimeUtc == _modified) return new Proxy(path, _bitmap.Size);
                Clear(); using var input = OpenValidated(path, out var width, out var height);
                _bitmap = width >= height ? Bitmap.DecodeToWidth(input, Math.Min(width, MaximumDimension), BitmapInterpolationMode.HighQuality) : Bitmap.DecodeToHeight(input, Math.Min(height, MaximumDimension), BitmapInterpolationMode.HighQuality);
                _path = path; _length = file.Length; _modified = file.LastWriteTimeUtc; return new Proxy(path, _bitmap.Size);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or InvalidOperationException) { Clear(); AppLog.Warning("Background", L10n.T("Settings.UnableToReadBackgroundImage"), error); return null; }
        }
    }
    /// <summary>仅清理受管理背景目录内未使用的副本，不删除原始图片。</summary>
    public static int CleanupUnused(AppStorage storage, string? current, int previousToKeep = 2)
    {
        if (storage.IsMigrating) return 0;
        previousToKeep = Math.Clamp(previousToKeep, 0, 2);
        var removed = 0;
        var roots = new[] { storage.Root, storage.PendingRoot }.OfType<string>().Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var root in roots)
        {
            var folder = Path.GetFullPath(Path.Combine(root, "Artwork", "Backgrounds"));
            try
            {
                if (!Directory.Exists(folder)) continue;
                DataDirectoryService.RejectLinkedAncestors(folder);
                var active = string.IsNullOrWhiteSpace(current) ? null : Path.GetFullPath(current);
                if (active is not null && root != storage.Root) active = DataDirectoryService.Remap(active, storage.Root, root);
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var images = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                    .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" || RetiredExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    .Select(file => new FileInfo(file))
                    .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0 && !string.Equals(file.FullName, active, comparison))
                    .OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToArray();
                foreach (var file in images.Skip(previousToKeep))
                {
                    try
                    {
                        DataDirectoryService.RejectLinkedAncestors(folder);
                        if (!string.Equals(Path.GetDirectoryName(file.FullName), folder, comparison) || (File.GetAttributes(file.FullName) & FileAttributes.ReparsePoint) != 0) continue;
                        File.Delete(file.FullName); removed++;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { AppLog.Warning("Background", "未能清理旧背景副本", error); }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidDataException) { AppLog.Warning("Background", "跳过无法安全清理的背景目录", error); }
        }
        if (removed > 0) AppLog.Info("Background", $"Cleaned {removed} unused managed background copies");
        return removed;
    }
    private static FileStream OpenValidated(string path, out int width, out int height)
    {
        if (!MediaPatterns.Contains("*" + Path.GetExtension(path).ToLowerInvariant())) throw new InvalidDataException(L10n.T("Background.StaticOnly"));
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > 20 * 1024 * 1024) throw new InvalidDataException(L10n.T("Settings.BackgroundImagesMustBeSmallerThanMB"));
        using var probe = File.OpenRead(path); using var codec = SKCodec.Create(probe);
        if (codec?.FrameCount > 1) throw new InvalidDataException(L10n.T("Background.StaticOnly"));
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 64 * 1024 * 1024) throw new InvalidDataException(L10n.T("Settings.UnsupportedBackgroundImageOrExcessiveDimensions"));
        width = codec.Info.Width; height = codec.Info.Height; return File.OpenRead(path);
    }
    public static void Clear() { lock (Gate) { _bitmap?.Dispose(); _bitmap = null; _path = null; _length = 0; } }
    /// <summary>保留绘制代理与文件身份，仅归还可重建的像素；下次绘制按需恢复，不重建页面。</summary>
    public static void TrimMemory() { lock (Gate) { _bitmap?.Dispose(); _bitmap = null; } }
    private sealed class Proxy(string path, Size size) : IImage
    {
        public Size Size => size;
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
            lock (Gate)
            {
                if (_path != path) return;
                if (_bitmap is null) GetImage(path);
                if (_bitmap is not null && _path == path) context.DrawImage(_bitmap, sourceRect, destRect);
            }
        }
    }
}
