using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>仅持有可见卡片的缩略图；磁盘访问和解码均在后台，回收或换绑时取消过期请求。</summary>
public sealed class ThumbnailImage : Image, IDisposable
{
    protected override Type StyleKeyOverride => typeof(Image);
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    private string[] _paths = [];
    private CancellationTokenSource? _request;
    private Bitmap? _bitmap;
    private bool _suspended, _disposed;

    public ThumbnailImage()
    {
        Stretch = Stretch.UniformToFill;
        AttachedToVisualTree += (_, _) => Load();
        DetachedFromVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // 样式方案同一轮重新挂接不丢弃像素；真正离开页面则释放并取消排队解码。
            if (!this.IsAttachedToVisualTree()) Release();
        }, DispatcherPriority.Background);
    }
    public void SetPaths(IEnumerable<string?> paths)
    {
        var next = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct(StringComparer.Ordinal).ToArray();
        if (_paths.SequenceEqual(next)) return;
        Release(); _paths = next; Load();
    }
    internal void SuspendDecodedPixels() { _suspended = true; Release(); }
    internal void ResumeDecodedPixels() { _suspended = false; Load(); }
    internal void ReleaseDecodedPixels() => Release();
    private void Load()
    {
        if (_disposed || _suspended || _bitmap is not null || _request is not null || _paths.Length == 0 || !this.IsAttachedToVisualTree()) return;
        var request = _request = new CancellationTokenSource();
        _ = LoadAsync(_paths, request);
    }
    private async Task LoadAsync(string[] paths, CancellationTokenSource request)
    {
        Bitmap? decoded = null;
        var token = request.Token;
        try
        {
            // 限制并发，取消后尚未获得解码槽的旧卡片不会再读取磁盘。
            decoded = await Task.Run(async () =>
            {
                await DecodeSlots.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    foreach (var path in paths)
                    {
                        token.ThrowIfCancellationRequested();
                        try { if (ArtworkCache.DecodeThumbnail(path) is { } bitmap) return bitmap; }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException) { }
                    }
                    return null;
                }
                finally { DecodeSlots.Release(); }
            }, token);
            if (token.IsCancellationRequested || !ReferenceEquals(_request, request) || !this.IsAttachedToVisualTree()) return;
            _bitmap = decoded; Source = decoded; decoded = null;
        }
        catch (OperationCanceledException) { }
        finally
        {
            decoded?.Dispose();
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }
    private void Release()
    {
        _request?.Cancel(); _request = null;
        Source = null; _bitmap?.Dispose(); _bitmap = null;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Release(); _paths = []; }
}
