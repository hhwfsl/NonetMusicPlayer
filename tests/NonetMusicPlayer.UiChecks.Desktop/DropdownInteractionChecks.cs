using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

/// <summary>用真实指针路径验证下拉选择、重复展开和嵌套浮窗，不用设置 SelectedIndex 代替用户操作。</summary>
internal static class DropdownInteractionChecks
{
    public static void Run()
    {
        var combo = CreateCombo();
        var nested = CreateCombo();
        var host = new Button { Content = "嵌套下拉框", Width = 220 };
        var flyout = new Flyout { Content = nested };
        host.Flyout = flyout;
        var outside = new Button { Content = "外部操作", Width = 160, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 30, 30, 0) };
        var outsideClicks = 0; outside.Click += (_, _) => outsideClicks++;
        // 外部按钮放在弹层右侧，避免测试指针落在覆盖按钮的真实弹层上。
        var panel = new StackPanel { Margin = new(30), Spacing = 160, HorizontalAlignment = HorizontalAlignment.Left, Children = { combo, host } };
        var window = new Window { Width = 600, Height = 500, Content = new Grid { Children = { panel, outside } } };
        window.Show(); Pump(window, 80);
        for (var cycle = 0; cycle < 4; cycle++)
        {
            Click(combo); Pump(window, 260);
            Require(combo.IsDropDownOpen && combo.Classes.Contains("popup-open"), "Dropdown opens and stays selected");
            var index = (cycle + 1) % 3;
            ClickOption(window, combo, index);
            Require(combo.SelectedIndex == index && !combo.IsDropDownOpen && !PopupFor(combo).IsOpen && !combo.Classes.Contains("popup-open"), "Pointer selection updates value and closes without breaking reopen binding");
        }
        Click(combo); Pump(window, 260); Click(outside); Pump(window, 240);
        Require(!combo.IsDropDownOpen && outsideClicks == 1, $"Outside click closes dropdown and reaches target button (open={combo.IsDropDownOpen}, clicks={outsideClicks})");
        Click(combo); Pump(window, 260); Click(combo); Pump(window, 220);
        Require(!combo.IsDropDownOpen, "Anchor toggles dropdown closed after repeated selections");
        Click(combo); Pump(window, 260); Click(host); Pump(window, 260);
        Require(!combo.IsDropDownOpen && flyout.IsOpen, "Switching dropdown to flyout preserves both control states");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Click(nested); Pump(window, 260);
            Require(flyout.IsOpen && nested.IsDropDownOpen, "Parent flyout stays open while nested dropdown opens");
            var index = (cycle + 1) % 3;
            ClickOption(window, nested, index);
            Require(flyout.IsOpen && nested.SelectedIndex == index && !nested.IsDropDownOpen, "Nested option selects and only closes its own popup");
        }
        Click(nested); Pump(window, 260); Click(outside); Pump(window, 240);
        Require(!flyout.IsOpen && !nested.IsDropDownOpen && outsideClicks == 2, $"Outside click closes nested dropdown and its owner (open={flyout.IsOpen}, nested={nested.IsDropDownOpen}, clicks={outsideClicks})");
        Click(host); Pump(window, 260); Click(nested); Pump(window, 260); ClickOption(window, nested, 1);
        Require(flyout.IsOpen && nested.SelectedIndex == 1 && !nested.IsDropDownOpen, "Nested dropdown reopens after its owner was dismissed");
        Click(outside); Pump(window, 240);
        var action = new MenuItem { Header = "菜单选项" }; var actionCount = 0; action.Click += (_, _) => actionCount++;
        var menu = new ContextMenu { ItemsSource = new[] { action } }; outside.ContextMenu = menu;
        menu.Open(outside); Pump(window, 260); Click(action); Pump(window, 240);
        Require(actionCount == 1 && !menu.IsOpen, "Inside menu pointer input reaches command before dismissing");
        window.Close();
        Console.WriteLine("PASS dropdown pointer selection, repeated reopen/toggle, outside target delivery and nested flyout selection");
    }
    private static ComboBox CreateCombo() => new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "选项一", "选项二", "选项三" }, SelectedIndex = 0 };
    private static Popup PopupFor(ComboBox combo) => combo.GetVisualDescendants().OfType<Popup>().Single(p => p.Name == "PART_Popup");
    private static void ClickOption(Window window, ComboBox combo, int index)
    {
        var item = combo.ContainerFromIndex(index) ?? throw new InvalidOperationException("Dropdown item not realized");
        Click(item); Pump(window, 240);
    }
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("Input target detached");
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left);
    }
    private static void Pump(Window window, int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        do { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); Thread.Sleep(5); } while (Environment.TickCount64 < until);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
