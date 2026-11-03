using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>菜单、音量和日历共用的可反向浮层动画；保留系统轻触关闭及原控件绑定。</summary>
public sealed class PopupSurfaceMotion
{
    private static readonly ConditionalWeakTable<AvaloniaObject, PopupSurfaceMotion> Installed = new();
    private static readonly ConditionalWeakTable<Control, Popup> Popups = new();
    private static readonly HashSet<PopupSurfaceMotion> OpenSurfaces = [];
    private static bool _installed;
    private readonly RetargetableMotion _motion = new();
    private readonly Action _hide;
    private Control? _surface;
    private Control? _anchor;
    private IDisposable? _opacityLease, _transformLease, _radiusLease;
    private readonly Channel _opacity = new(1d), _transform = new(new MatrixTransform()), _radius = new(new CornerRadius());
    private ITransform? _baseTransform;
    private double _baseOpacity;
    private CornerRadius _baseRadius;
    private bool _closing, _allowClose, _above;
    private int _request;

    private PopupSurfaceMotion(Action hide) { _hide = hide; _motion.Changed += Draw; }

    public static void Install()
    {
        if (_installed) return; _installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<Window>((window, e) =>
        {
            foreach (var state in OpenSurfaces.ToArray())
            {
                if (state._anchor is not { } anchor || !ReferenceEquals(TopLevel.GetTopLevel(anchor), window)) continue;
                // 弹层事件也会经过所属窗口；内部选项及嵌套弹层不能被当作外部点击提前关闭。
                if (state.ContainsSource(e.Source)) continue;
                if (new Rect(anchor.Bounds.Size).Contains(e.GetPosition(anchor))) continue;
                // 外部按下必须立即撤掉原生弹层，让这次按下和释放都交给目标按钮；不能等收起动画吞掉释放。
                state._allowClose = true;
                try { state._hide(); }
                finally { state._allowClose = false; }
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 包括后续由插件创建的标准菜单与按钮，无需每个页面复制一套浮层逻辑。
        Control.ContextMenuProperty.Changed.AddClassHandler<Control>((_, e) => { if (e.NewValue is ContextMenu menu) Attach(menu); });
        Button.FlyoutProperty.Changed.AddClassHandler<Button>((_, e) => { if (e.NewValue is PopupFlyoutBase flyout) Attach(flyout); });
        // ContextMenu 不一定是 Popup 的逻辑子节点，直接跟踪公开 Child 属性，避免使用框架私有字段。
        Popup.ChildProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            if (e.OldValue is Control old) Popups.Remove(old);
            if (e.NewValue is Control child) { Popups.Remove(child); Popups.Add(child, popup); }
        });
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ComboBox>((combo, e) =>
        {
            if (e.NameScope.Find<Popup>("PART_Popup") is not { } popup || Installed.TryGetValue(popup, out _)) return;
            // 选择器保留框架原有关闭与选中逻辑，仅为展开的真实列表添加入场反馈。
            popup.SetCurrentValue(Popup.OverlayDismissEventPassThroughProperty, true);
            popup.SetCurrentValue(Popup.OverlayInputPassThroughElementProperty, combo);
            // 通过选择器自身关闭，保留模板 Popup.IsOpen 与 IsDropDownOpen 的绑定及键盘选择状态。
            var state = new PopupSurfaceMotion(() => combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false)); Installed.Add(popup, state);
            popup.Opened += (_, _) => { if (popup.Child is Control child) { state.Open(child, false); state.SelectAnchor(combo, true); } };
            popup.Closed += (_, _) => state.Reset();
        });
    }

    public static void Attach(PopupFlyoutBase flyout)
    {
        if (Installed.TryGetValue(flyout, out _)) return;
        var state = new PopupSurfaceMotion(flyout.Hide); Installed.Add(flyout, state);
        flyout.OverlayDismissEventPassThrough = true;
        flyout.Opened += (_, _) =>
        {
            flyout.OverlayInputPassThroughElement = flyout.Target;
            if (flyout.Popup.Child is Control surface) { state.Open(surface, flyout.Placement.ToString().Contains("Top", StringComparison.Ordinal)); state.SelectAnchor(flyout.Target, true); }
        };
        flyout.Closing += (_, e) => state.Closing(e);
        flyout.Closed += (_, _) => state.Reset();
    }
    public static void Attach(ContextMenu menu)
    {
        if (Installed.TryGetValue(menu, out _)) return;
        var state = new PopupSurfaceMotion(() =>
        {
            if (Popups.TryGetValue(menu, out var popup))
            {
                // 轻触关闭被取消后，Popup 的属性可能已为 false，而原生呈现器仍在；同帧重新提交开关以完成关闭。
                if (!popup.IsOpen) popup.SetCurrentValue(Popup.IsOpenProperty, true);
                popup.SetCurrentValue(Popup.IsOpenProperty, false);
            }
            else menu.Close();
        }); Installed.Add(menu, state);
        menu.Opened += (_, _) =>
        {
            var popup = Popups.TryGetValue(menu, out var host) ? host : menu.GetLogicalAncestors().OfType<Popup>().FirstOrDefault();
            if (popup is not null) popup.OverlayDismissEventPassThrough = true;
            state.Open(menu, menu.Placement.ToString().Contains("Top", StringComparison.Ordinal));
            state.SelectAnchor(popup?.PlacementTarget ?? menu.PlacementTarget, true);
        };
        menu.Closing += (_, e) => state.Closing(e);
        menu.Closed += (_, _) => state.Reset();
    }
    public static void AllowAnchorInput(ContextMenu menu, Control anchor)
    {
        if (Installed.TryGetValue(menu, out var state)) state.SelectAnchor(anchor, true);
        if (Popups.TryGetValue(menu, out var popup))
        { popup.OverlayDismissEventPassThrough = true; popup.OverlayInputPassThroughElement = anchor; }
    }
    public static void Toggle(PopupFlyoutBase flyout, Control anchor)
    {
        Attach(flyout);
        if (flyout.IsOpen) flyout.Hide(); else flyout.ShowAt(anchor);
    }
    private bool ContainsSource(object? source)
    {
        // 沿弹层真实可视树及 PlacementTarget 回溯，兼容日历里的下拉框和多级菜单。
        var seen = new HashSet<Visual>();
        for (var visual = source as Visual; visual is not null && seen.Add(visual);)
        {
            Popup? owner = null;
            for (var current = visual; current is not null; current = current.GetVisualParent())
            {
                if (ReferenceEquals(current, _surface)) return true;
                if (current is Control control && Popups.TryGetValue(control, out var popup)) owner ??= popup;
            }
            visual = owner?.PlacementTarget;
        }
        return false;
    }
    private void Open(Control surface, bool above)
    {
        Reset(); _surface = surface; _above = above;
        OpenSurfaces.Add(this);
        _baseTransform = surface.RenderTransform; _baseOpacity = surface.Opacity;
        _baseRadius = surface is TemplatedControl templated ? templated.CornerRadius : default;
        _opacityLease = surface.Bind(Visual.OpacityProperty, _opacity, BindingPriority.Animation);
        _transformLease = surface.Bind(Visual.RenderTransformProperty, _transform, BindingPriority.Animation);
        if (surface is TemplatedControl) _radiusLease = surface.Bind(TemplatedControl.CornerRadiusProperty, _radius, BindingPriority.Animation);
        Draw(0); _ = _motion.MoveAsync(1, 220);
    }
    private void Closing(CancelEventArgs args)
    {
        SelectAnchor(_anchor, false);
        if (_allowClose || _surface is null || !InterfaceMotion.IsEnabled) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true; var request = ++_request;
        _ = CloseAsync(request);
    }
    private async Task CloseAsync(int request)
    {
        await _motion.MoveAsync(0, 160);
        // 独立宿主/测试入口可能没有同步上下文，收起后的原生 Popup 操作必须回到 UI 线程。
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_closing || request != _request) return;
            _allowClose = true;
            try { _hide(); }
            finally { _allowClose = false; }
        });
    }
    private void Draw(double value)
    {
        if (_surface is null) return;
        var scale = .94 + .06 * value;
        var anchor = new Point(_surface.Bounds.Width / 2, _above ? _surface.Bounds.Height : 0);
        // 在矩阵中指定锚点，避免覆盖方案原有 RenderTransformOrigin。
        var matrix = Matrix.CreateTranslation(-anchor.X, -anchor.Y) * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(anchor.X, anchor.Y + (_above ? 1 : -1) * 12 * (1 - value));
        _transform.Set(new MatrixTransform((_baseTransform?.Value ?? Matrix.Identity) * matrix));
        _opacity.Set(_baseOpacity * value);
        _radius.Set(new CornerRadius(_baseRadius.TopLeft / scale, _baseRadius.TopRight / scale, _baseRadius.BottomRight / scale, _baseRadius.BottomLeft / scale));
    }
    private void Reset()
    {
        OpenSurfaces.Remove(this);
        SelectAnchor(_anchor, false); _anchor = null;
        ++_request; _closing = false; _motion.Cancel(); _ = _motion.MoveAsync(0, 0);
        _opacityLease?.Dispose(); _transformLease?.Dispose(); _radiusLease?.Dispose();
        _opacityLease = _transformLease = _radiusLease = null; _surface = null;
    }
    private void SelectAnchor(Control? anchor, bool open)
    {
        if (_anchor is not null && !ReferenceEquals(anchor, _anchor)) _anchor.Classes.Remove("popup-open");
        _anchor = anchor; anchor?.Classes.Set("popup-open", open);
    }
    private sealed class Channel(object initial) : IObservable<object>
    {
        private IObserver<object>? _observer;
        public IDisposable Subscribe(IObserver<object> observer)
        { _observer = observer; observer.OnNext(initial); return new ElementLayoutService.ActionLease(() => _observer = null); }
        public void Set(object value) { initial = value; _observer?.OnNext(value); }
    }
}
