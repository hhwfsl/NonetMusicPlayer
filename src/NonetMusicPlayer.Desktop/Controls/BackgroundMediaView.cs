using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>与静态背景共用 Image 的铺放和透明度；动态背景最多保留当前帧与一张绘制位图。</summary>
public sealed class BackgroundMediaView : Image, IDisposable
{
    private string? _path;
    private CancellationTokenSource? _decode;
    private WriteableBitmap? _bitmap;
    private int _generation;
    private bool _active, _disposed;
    public long FramesPresented { get; private set; }
    public long PixelBytes => _bitmap is null ? 0 : 4L * _bitmap.PixelSize.Width * _bitmap.PixelSize.Height;
    public event Action<Exception>? PlaybackFailed;
    public void SetMedia(string? path)
    {
        if (_path == path) return;
        Stop(); _path = path; Refresh();
    }
    public void SetPlaybackActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (!active && _decode is not null) Stop(); else Refresh();
    }
    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            if (!AppBackgroundService.IsDynamic(_path)) { Source = AppBackgroundService.GetImage(_path); return; }
            if (!_active || _decode is not null || _path is null) return;
            AppBackgroundService.Clear();
            _decode = new(); _ = DecodeAsync(_path, ++_generation, _decode.Token);
        }
        catch (Exception error) { PlaybackFailed?.Invoke(error); }
    }
    private async Task DecodeAsync(string path, int generation, CancellationToken token)
    {
        try
        {
            if (AnimatedRasterDecoder.Handles(path))
            {
                await Task.Run(async () =>
                {
                    using var reader = new AnimatedRasterDecoder(path); var clock = Stopwatch.StartNew();
                    while (!token.IsCancellationRequested) { using var frame = reader.ReadFrame(clock.ElapsedMilliseconds); await PresentAsync(frame, generation, token); await Task.Delay(42, token); }
                }, token); return;
            }
            using var process = Process.Start(BackgroundVideoDecoder.StartInfo(path, true, true)) ?? throw new IOException(L10n.T("Background.DecoderUnavailable"));
            using var kill = token.Register(() => BackgroundVideoDecoder.Kill(process));
            var errors = BackgroundVideoDecoder.DrainErrorsAsync(process.StandardError, token);
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var frame = await BackgroundVideoDecoder.ReadFrameAsync(process.StandardOutput.BaseStream, token);
                    if (frame is null) throw new IOException(L10n.T("Background.PlaybackFailed"));
                    // 等待 UI 消费后才读取下一帧，避免后台不断向忙碌的界面排队。
                    await PresentAsync(frame, generation, token);
                }
            }
            finally { BackgroundVideoDecoder.Kill(process); try { await errors; } catch (OperationCanceledException) { } }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Dispatcher.UIThread.Post(() => { if (generation == _generation && !_disposed) { Stop(); PlaybackFailed?.Invoke(error); } });
        }
    }
    private async Task PresentAsync(VideoFrame frame, int generation, CancellationToken token)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation != _generation || token.IsCancellationRequested || _disposed) return;
            if (_bitmap is null || _bitmap.PixelSize != new PixelSize(frame.Width, frame.Height))
            { Source = null; _bitmap?.Dispose(); _bitmap = new(new(frame.Width, frame.Height), new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul); Source = _bitmap; }
            using (var locked = _bitmap.Lock()) for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.Pixels, row * frame.Width * 4, locked.Address + row * locked.RowBytes, frame.Width * 4);
            FramesPresented++; InvalidateVisual();
        });
    }
    private void Stop()
    {
        ++_generation; var decoder = _decode; _decode = null; decoder?.Cancel(); decoder?.Dispose();
        Source = null; _bitmap?.Dispose(); _bitmap = null;
    }
    public void Dispose() { _disposed = true; Stop(); }
}
