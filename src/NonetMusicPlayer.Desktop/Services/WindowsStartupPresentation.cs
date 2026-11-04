using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>首个窗口合成批次完成前使用 DWM 隐藏呈现，不改变窗口透明度、鼠标或生命周期。</summary>
public sealed class WindowsStartupPresentation : IDisposable
{
    private readonly Window _window;
    private readonly nint _handle;
    private readonly Action _prepareFrame;
    private readonly CancellationTokenSource _closed = new();
    private bool _started, _disposed;
    private WindowsStartupPresentation(Window window, nint handle, Action prepareFrame)
    {
        _window = window; _handle = handle; _prepareFrame = prepareFrame;
        window.Opened += Opened; window.Closed += Closed;
    }
    public static WindowsStartupPresentation? Attach(Window window, Action prepareFrame)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0) return null;
        var cloak = 1;
        // 原生窗口仍接收绘制并保留系统标题栏身份，DWM 暂不把尚未绘制的表面展示出来。
        return DwmSetWindowAttribute(handle, 13, ref cloak, sizeof(int)) == 0 ? new(window, handle, prepareFrame) : null;
    }
    private async void Opened(object? sender, EventArgs e)
    {
        if (_started || _disposed) return;
        _started = true;
        var closed = _closed.Token;
        try
        {
            // Loaded 阶段先完成首轮样式与布局，再等实际窗口的合成批次，而非离屏图片或固定延迟。
            await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed) { _window.UpdateLayout(); _prepareFrame(); } }, DispatcherPriority.Loaded);
            if (_disposed) return;
            if (ElementComposition.GetElementVisual(_window)?.Compositor is { } compositor)
            {
                var batch = compositor.RequestCompositionBatchCommitAsync();
                await batch.Rendered.WaitAsync(closed);
            }
            await Dispatcher.UIThread.InvokeAsync(Reveal, DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error)
        {
            AppLog.Error("UI", "Initial window presentation failed", error);
            await Dispatcher.UIThread.InvokeAsync(Reveal);
        }
    }
    private void Reveal()
    {
        if (_disposed) return;
        var cloak = 0;
        DwmSetWindowAttribute(_handle, 13, ref cloak, sizeof(int));
        // 系统可能跳过被遮蔽窗口的首次激活；只在应用本来要求激活且仍可见时恢复。
        if (_window.ShowActivated && _window.IsVisible && _window.WindowState != WindowState.Minimized) _window.Activate();
    }
    private void Closed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _closed.Cancel();
        _window.Opened -= Opened; _window.Closed -= Closed;
        _closed.Dispose();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
