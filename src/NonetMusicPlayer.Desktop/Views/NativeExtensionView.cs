using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Plugins;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>原生视图只面向已确认信任的托管插件；退出区域时释放视图，不销毁后台会话。</summary>
public sealed class NativeExtensionView : UserControl, IDisposable, NonetMusicPlayer.Desktop.Controls.IPluginKeyboardScope
{
    private readonly ExtensionSession _session;
    private readonly PluginManager _manager;
    private readonly string _slot;
    private INonetNativeView? _view;
    private bool _disposed, _starting;
    public event Action? Failed;
    public event Action? UnavailableView;
    public NativeExtensionView(PluginManager manager, PluginManifest manifest, string slot) : this(manager, manifest, slot, null, null) { }
    public NativeExtensionView(PluginManager manager, PluginManifest manifest, string slot, string? target, string? placement)
    {
        _manager = manager; _slot = slot; _session = manager.Extension(manifest);
        _session.Changed += Refresh; manager.UiPluginUnavailable += Unavailable;
        AttachedToVisualTree += async (_, _) =>
        {
            if (_starting || _disposed) return; _starting = true;
            try
            {
                var view = await _session.CreateNativeViewAsync(_slot, target, placement);
                if (_disposed) { await view.DisposeAsync(); return; }
                _view = view;
                if (view.View is not Control control || control.Parent is not null) throw new InvalidDataException("Native view must be a new Avalonia Control.");
                Content = control; view.Update(_session.Frame);
            }
            catch (Exception error) { if (_disposed) return; Dispose(); manager.ReportPluginFault(manifest, error); Failed?.Invoke(); }
        };
    }
    private void Refresh(ExtensionFrame frame)
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Refresh(frame)); return; }
        try { _view?.Update(frame); }
        catch (Exception error) { Dispose(); _manager.ReportPluginFault(_session.Manifest, error); Failed?.Invoke(); }
    }
    private void Unavailable(string id) { if (id == _session.Manifest.Id) { Dispose(); UnavailableView?.Invoke(); } }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // 完整界面方案会在同一 UI 事务内重新挂接父容器；仅真正离开页面时释放原生视图。
        Dispatcher.UIThread.Post(() => { if (!this.IsAttachedToVisualTree()) Dispose(); }, DispatcherPriority.Background);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _session.Changed -= Refresh; _manager.UiPluginUnavailable -= Unavailable;
        if (_view is not null) _ = ReleaseAsync(_view);
        _view = null; Content = null;
    }
    private async Task ReleaseAsync(INonetNativeView view)
    {
        try { await view.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception error)
        {
            if (!_session.IsDisposed) _manager.ReportPluginFault(_session.Manifest, error);
            else NonetMusicPlayer.Core.Diagnostics.AppLog.Warning("Plugin " + _session.Manifest.Name + " (" + _session.Manifest.Id + ")", "Native view cleanup failed after session closure.", error);
        }
    }
}
