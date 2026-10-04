using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>
/// 全进程有界 LRU 封面缓存；视图持有轻量绘制代理，不拥有像素，淘汰位图可安全释放。
/// </summary>
public sealed class ArtworkCache
{
    public const int MaxEntries = 48;
    public const long MaxBytes = 16 * 1024 * 1024;
    public const int MaxDimension = 320;
    public static ArtworkCache Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private long _bytes, _decodes, _evictions;
    public ArtworkCacheSnapshot Snapshot { get { lock (_gate) return new(_entries.Count, _bytes, _decodes, _evictions); } }

    public IImage? GetImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { lock (_gate) return GetEntry(Path.GetFullPath(path))?.Image; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException) { return null; }
    }
    public void Invalidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { lock (_gate) Remove(Path.GetFullPath(path)); } catch (ArgumentException) { }
    }
    public void Clear()
    {
        lock (_gate) { foreach (var entry in _entries.Values) entry.Bitmap?.Dispose(); _entries.Clear(); _lru.Clear(); _bytes = 0; }
    }
    private Entry? GetEntry(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) { Remove(path); return null; }
        if (_entries.TryGetValue(path, out var cached))
        {
            if (cached.Length == file.Length && cached.Modified == file.LastWriteTimeUtc)
            {
                _lru.Remove(cached.Node); _lru.AddFirst(cached.Node); return cached;
            }
            Remove(path);
        }
        Bitmap? bitmap = null;
        try
        {
            // 分配像素前拒绝过大编码数据及解压炸弹。
            if (file.Length <= 0 || file.Length > 20 * 1024 * 1024) return null;
            int width, height;
            using (var probe = File.OpenRead(path))
            using (var codec = SKCodec.Create(probe))
            {
                if (codec is null) return null;
                width = codec.Info.Width; height = codec.Info.Height;
            }
            if (width <= 0 || height <= 0 || (long)width * height > 64 * 1024 * 1024) return null;
            var scaledWidth = Math.Min(MaxDimension, width);
            var scaledHeight = Math.Min(MaxDimension, height);
            var expectedBytes = 4L * (width >= height ? scaledWidth * Math.Max(1, (int)((long)height * scaledWidth / width)) : scaledHeight * Math.Max(1, (int)((long)width * scaledHeight / height)));
            Trim(expectedBytes);
            using var input = File.OpenRead(path);
            bitmap = width >= height ? Bitmap.DecodeToWidth(input, scaledWidth, BitmapInterpolationMode.HighQuality) : Bitmap.DecodeToHeight(input, scaledHeight, BitmapInterpolationMode.HighQuality);
            var bytes = 4L * bitmap.PixelSize.Width * bitmap.PixelSize.Height;
            if (bitmap.PixelSize.Width > MaxDimension || bitmap.PixelSize.Height > MaxDimension || bytes > MaxBytes) { bitmap.Dispose(); return null; }
            Trim(bytes);
            var node = _lru.AddFirst(path);
            var entry = new Entry(bitmap, new CachedImage(this, path, bitmap.Size), node, bytes, file.Length, file.LastWriteTimeUtc);
            _entries.Add(path, entry); _bytes += bytes; _decodes++; return entry;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            bitmap?.Dispose(); return null;
        }
    }
    private void Trim(long incoming)
    {
        while (_lru.Last is { } last && (_entries.Count >= MaxEntries || _bytes + incoming > MaxBytes)) Remove(last.Value);
    }
    private void Remove(string path)
    {
        if (!_entries.Remove(path, out var entry)) return;
        _lru.Remove(entry.Node); _bytes -= entry.Bytes; entry.Bitmap.Dispose(); _evictions++;
    }
    private void Draw(string path, DrawingContext context, Rect source, Rect destination)
    {
        // 解锁前绘制上下文已取得原生图像引用；LRU 淘汰后，视图不会继续使用已释放的 Bitmap 包装。
        try { lock (_gate) { if (GetEntry(path) is { } entry) context.DrawImage(entry.Bitmap, source, destination); } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException) { }
    }
    private sealed record Entry(Bitmap Bitmap, CachedImage Image, LinkedListNode<string> Node, long Bytes, long Length, DateTime Modified);
    private sealed class CachedImage(ArtworkCache cache, string path, Size size) : IImage
    {
        public Size Size { get; } = size;
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) => cache.Draw(path, context, sourceRect, destRect);
    }
}

public readonly record struct ArtworkCacheSnapshot(int Count, long EstimatedBytes, long DecodeCount, long Evictions);
