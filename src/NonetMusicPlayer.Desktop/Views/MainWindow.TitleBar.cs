using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly List<Border> _resizeBorders = [];
    private Win32Properties.CustomWindowStylesCallback? _snapWindowStyles;
    private TouchWindowMover? _touchTitleMover;
    private TouchWindowResizer? _touchWindowResizer;
    private void InitializeTitleInteraction()
    {
        _touchTitleMover = new TouchWindowMover(this);
        _touchWindowResizer = new TouchWindowResizer(this);
        _resizeBorders.AddRange(((Grid)Content!).Children.OfType<Border>().Where(b => b.Tag is string));
        if (OperatingSystem.IsWindows())
        {
            // 使用框架的扩展客户区保留系统标题/缩放身份，内容和按钮仍由应用绘制。
            // 不手动叠加 WS_THICKFRAME，避免原生边框计算令记忆尺寸在每次启动时变大。
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 0;
            WindowDecorations = WindowDecorations.BorderOnly;
            // 边框模式由框架正确换算客户区；补充系统菜单身份，不引入第二组标题按钮。
            _snapWindowStyles = (style, extended) => (style | 0x00080000u, extended);
            Win32Properties.AddWindowStylesCallback(this, _snapWindowStyles);
            // 标题栏直接报告 HTCAPTION，由 Windows 接管按下、拖动、双击与系统贴靠。
            // 不再从客户区 PointerPressed 调 BeginMoveDrag，也不补发或拦截鼠标消息。
            Win32Properties.SetNonClientHitTestResult(TitleBarSurface, Win32Properties.Win32HitTestValue.Caption);
            // 完整方案可将品牌移到侧栏；角色跟随原控件，不依赖固定的 XAML 父节点。
            Win32Properties.SetNonClientHitTestResult(TitleBrand, Win32Properties.Win32HitTestValue.Caption);
            // 窗口级样式也覆盖重挂接及后来插入的 New/插件按钮，内部图标继承 Client。
            Styles.Add(new Style(selector => selector.Is<Button>())
            {
                Setters = { new Setter(Win32Properties.NonClientHitTestResultProperty, Win32Properties.Win32HitTestValue.Client) }
            });
            Win32Properties.SetNonClientHitTestResult(MaximizeWindowButton, Win32Properties.Win32HitTestValue.MaxButton);
        }
        Closed += (_, _) =>
        {
            if (_snapWindowStyles is not null) Win32Properties.RemoveWindowStylesCallback(this, _snapWindowStyles);
            _touchTitleMover.Dispose();
            _touchWindowResizer.Dispose();
        };
    }
    private void ApplyTitleButtons()
    {
        var left = _vm is null ? OperatingSystem.IsMacOS() : InterfaceSettings.TitleButtonsOnLeft;
        TitleBarLayout.Margin = left ? new(16, 0, 16, 0) : new(20, 0, 12, 0);
        TitleBarLayout.ColumnDefinitions = new ColumnDefinitions(left ? "*,Auto,*" : "Auto,*,Auto");
        Grid.SetColumn(TitleButtons, left ? 0 : 2); Grid.SetColumn(TitleBrand, left ? 1 : 0);
        TitleBrand.HorizontalAlignment = left ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Left;
        TitleButtons.HorizontalAlignment = left ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Right;
        // 颜色始终对应同一功能；位置设置仅改变符合平台习惯的按钮顺序。
        Control[] buttons = left ? [CloseWindowButton, MinimizeWindowButton, MaximizeWindowButton]
            : [MinimizeWindowButton, MaximizeWindowButton, CloseWindowButton];
        TitleButtons.Children.Clear(); foreach (var button in buttons) TitleButtons.Children.Add(button);
    }
    // 缩放热区仅随窗口状态切换，不再等待一次后续鼠标移动才能恢复。
    private void RefreshResizeBorders() { foreach (var border in _resizeBorders) border.IsHitTestVisible = WindowState == WindowState.Normal; }
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Ancestors(e.Source).Any(c => c is Button)) return;
        if (e.Pointer.Type is PointerType.Touch or PointerType.Pen)
        {
            if (e.ClickCount == 2) { ToggleMaximize(); e.Handled = true; return; }
            _touchTitleMover!.TryBegin(e); return;
        }
        // Windows 鼠标拖窗完全走非客户区；触摸/笔和其他平台保留现有兼容入口。
        if (OperatingSystem.IsWindows()) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        BeginMoveDrag(e);
    }
}
