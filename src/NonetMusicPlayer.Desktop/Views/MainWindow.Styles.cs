using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>风格服务生命周期绑定宿主；停用、卸载或故障时立即恢复资源与原控件属性。</summary>
public sealed partial class MainWindow
{
    private StyleLayerService? _styleLayers;
    private readonly StyleMorphMotion _styleMorph = new();
    private Action? _restoreExpandedStyleControl;
    private int _windowMorphRequest;
    public StyleLayerService StyleLayers => _styleLayers ??= CreateStyleLayers();
    private StyleLayerService CreateStyleLayers()
    {
        var service = new StyleLayerService(this);
        service.Faulted += (id, error) => { if (_vm?.Plugins.Installed.FirstOrDefault(p => p.Id == id) is { } plugin) _vm.Plugins.ReportPluginFault(plugin, error); };
        return service;
    }
    public async Task MorphContainerAsync(Control control, CancellationToken token = default)
    {
        if (StyleLayers.Morph is not { Enabled: true } profile || Content is not Grid root) return;
        _restoreExpandedStyleControl?.Invoke(); _styleMorph.Cancel();
        if (control.Parent is not Panel parent || control is MainWindow || control.Name == "WindowSurface") throw new InvalidDataException("Container expansion requires a Panel child.");
        var position = control.TranslatePoint(default, root) ?? default;
        var from = new Avalonia.Rect(position, control.Bounds.Size);
        var bitmap = LyricsPageMotion.Capture(control, new(control.Bounds.Size));
        var index = parent.Children.IndexOf(control);
        var placeholder = new Border { Width = control.Bounds.Width, Height = control.Bounds.Height, Margin = control.Margin };
        Grid.SetRow(placeholder, Grid.GetRow(control)); Grid.SetColumn(placeholder, Grid.GetColumn(control));
        Grid.SetRowSpan(placeholder, Grid.GetRowSpan(control)); Grid.SetColumnSpan(placeholder, Grid.GetColumnSpan(control));
        parent.Children.Remove(control); parent.Children.Insert(index, placeholder);
        var full = new Border { Name = "NonetMorphContainer", Background = Ui.Brush("SurfaceBrush"), CornerRadius = new(profile.CornerRadius), ClipToBounds = true, Margin = new(12, 52, 12, 12), ZIndex = 500 };
        var body = new Grid(); var restored = false; IDisposable[] leases = [];
        var close = Ui.IconButton(Controls.IconKind.Close, "Common.Close", () => _ = Collapse()); close.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; close.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top; close.Margin = new(8); close.ZIndex = 10;
        leases = [control.Bind(Control.WidthProperty, new StyleConstant(double.NaN), BindingPriority.Animation), control.Bind(Control.HeightProperty, new StyleConstant(double.NaN), BindingPriority.Animation), control.Bind(Control.MarginProperty, new StyleConstant(new Thickness(24, 54, 24, 24)), BindingPriority.Animation)];
        body.Children.Add(control); body.Children.Add(close); full.Child = body; root.Children.Add(full); close.Focus();
        void Escape(object? sender, KeyEventArgs args) { if (args.Key == Key.Escape) { args.Handled = true; _ = Collapse(); } }
        AddHandler(KeyDownEvent, Escape, RoutingStrategies.Tunnel, true);
        void Restore()
        {
            if (restored) return; restored = true; _styleMorph.Cancel(); RemoveHandler(KeyDownEvent, Escape);
            body.Children.Remove(control); root.Children.Remove(full); foreach (var lease in leases) lease.Dispose();
            parent.Children.Remove(placeholder); if (control.Parent is null) parent.Children.Insert(Math.Min(index, parent.Children.Count), control);
            _restoreExpandedStyleControl = null;
        }
        _restoreExpandedStyleControl = Restore;
        async Task Collapse()
        {
            if (restored) return;
            var image = LyricsPageMotion.Capture(full, new(full.Bounds.Size));
            if (image is not null) await _styleMorph.RunAsync(root, full, image, new(full.TranslatePoint(default, root) ?? default, full.Bounds.Size), from, profile, profile.CornerRadius, profile.CornerRadius);
            Restore();
        }
        UpdateLayout();
        if (bitmap is not null) await _styleMorph.RunAsync(root, full, bitmap, from, new(full.TranslatePoint(default, root) ?? default, full.Bounds.Size), profile, profile.CornerRadius, profile.CornerRadius, token);
        if (token.IsCancellationRequested) Restore();
        else if (!restored) close.Focus();
    }
    private async Task MaximizeWithStyleAsync()
    {
        var request = ++_windowMorphRequest;
        if (StyleLayers.Morph is not { Enabled: true } profile || !StyleLayers.WindowMorphEnabled || !InterfaceMotion.IsEnabled || Content is not Grid root)
        { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; return; }
        try
        {
            _styleMorph.Cancel();
            _restoreExpandedStyleControl?.Invoke();
            var oldPosition = Position; var oldSize = ClientSize; var oldScaling = RenderScaling; var oldRadius = WindowSurface.CornerRadius.TopLeft;
            var snapshot = LyricsPageMotion.Capture(WindowSurface, new(WindowSurface.Bounds.Size));
            var wasMaximized = WindowState == WindowState.Maximized;
            WindowState = wasMaximized ? WindowState.Normal : WindowState.Maximized;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            UpdateLayout();
            if (snapshot is null) return;
            if (request != _windowMorphRequest || WindowState == WindowState.Minimized) { snapshot.Dispose(); return; }
            var from = new Avalonia.Rect((oldPosition.X - Position.X) / RenderScaling, (oldPosition.Y - Position.Y) / RenderScaling,
                oldSize.Width * oldScaling / RenderScaling, oldSize.Height * oldScaling / RenderScaling);
            await _styleMorph.RunAsync(root, WindowSurface, snapshot, from, new(root.Bounds.Size), profile, oldRadius, WindowSurface.CornerRadius.TopLeft);
        }
        catch (Exception error) { AppLog.Warning("Styles", "Window morph skipped", error); }
    }
    private sealed class StyleConstant(object value) : IObservable<object>
    { public IDisposable Subscribe(IObserver<object> observer) { observer.OnNext(value); return new ElementLayoutService.ActionLease(() => { }); } }
}
