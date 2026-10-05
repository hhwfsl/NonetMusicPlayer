using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using Avalonia.Controls.Templates;

namespace NonetMusicPlayer.Desktop.Views;

internal static class Ui
{
    public static TextBlock Text(string text, double size = 13, bool muted = false)
    {
        var result = new TextBlock { Text = L10n.T(text), FontSize = size, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        if (muted) result.Foreground = Brush("TextSecondaryBrush");
        return result;
    }
    public static TextBlock RawText(string text, double size = 13, bool muted = false) { var result = Text("", size, muted); result.Text = text; return result; }
    public static IBrush Brush(string key) => (IBrush)Application.Current!.Resources[key]!;
    public static Button PathLink(string label, string path)
    {
        var button = Button("", () => MainWindow.OpenPath(path)); button.Classes.Add("quiet");
        var text = Text("", 12, true); text.Text = L10n.T(label) + ": " + path; text.TextWrapping = TextWrapping.Wrap;
        button.Content = text; button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        ToolTip.SetTip(button, L10n.T("Common.OpenLocation")); return button;
    }
    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = L10n.T(text) }; if (primary) button.Classes.Add("primary"); button.Click += (_, _) => { try { action(); } catch (Exception e) { Report(button, e); } }; return button;
    }
    public static Button AsyncButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = L10n.T(text) }; if (primary) button.Classes.Add("primary");
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception e) { Report(button, e); } finally { button.IsEnabled = true; } }; return button;
    }
    /// <summary>无文字按钮同时提供完整提示与无障碍名称，保留统一的触摸目标尺寸。</summary>
    public static Button IconButton(IconKind icon, string label, Action action)
    {
        var button = Button("", action); button.Width = 44; button.Height = 44; button.Classes.Add("transport");
        button.Content = new VectorIcon { Kind = icon };
        ToolTip.SetTip(button, L10n.T(label)); Avalonia.Automation.AutomationProperties.SetName(button, L10n.T(label));
        return button;
    }
    private static void Report(Control source, Exception e) { if (TopLevel.GetTopLevel(source)?.DataContext is ViewModels.MainViewModel vm) vm.ReportError(L10n.T("Common.ActionFailed"), e); }
    public static StackPanel Stack(params Control[] controls) { var panel = new StackPanel { Spacing = 12 }; foreach (var control in controls) panel.Children.Add(control); return panel; }
    public static StackPanel Actions(params Control[] controls) { var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 }; foreach (var control in controls) panel.Children.Add(control); return panel; }
    public static Border Card(string title, params Control[] controls)
    {
        var stack = Stack(); var heading = Text(title, 17); heading.FontWeight = FontWeight.SemiBold; stack.Children.Add(heading);
        foreach (var control in controls) stack.Children.Add(control);
        return new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("DividerBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Child = stack };
    }
    public static Grid Row(string label, string description, Control editor, string wideColumns = "*,240")
    {
        var grid = new ResponsiveSettingsRow { WideColumns = wideColumns, ColumnDefinitions = new ColumnDefinitions(wideColumns), Margin = new Thickness(0, 6), Tag = label + " " + description };
        var labels = Stack(Text(label), Text(description, 12, true)); labels.Spacing = 5; labels.Margin = new Thickness(0, 0, 25, 0); grid.Children.Add(labels);
        editor.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; Avalonia.Automation.AutomationProperties.SetName(editor, L10n.T(label)); ToolTip.SetTip(editor, L10n.T(description)); Grid.SetColumn(editor, 1); grid.Children.Add(editor); return grid;
    }
    public static ComboBox Choice(IEnumerable<string> values, string selected, Action<string> changed)
    {
        var combo = new ComboBox { ItemsSource = values.ToArray(), SelectedItem = selected, ItemTemplate = new FuncDataTemplate<string>((value, _) => { var text = Text(value ?? ""); text.TextWrapping = TextWrapping.NoWrap; return text; }), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, MinHeight = 38 };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string value) changed(value); }; return combo;
    }
    public static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        ToolTip.SetTip(toggle, L10n.T(value ? L10n.T("Common.Enabled") : L10n.T("Common.Disabled")));
        var applied = value; var reverting = false;
        toggle.IsCheckedChanged += (_, _) =>
        {
            if (reverting) return;
            try { changed(toggle.IsChecked == true); applied = toggle.IsChecked == true; ToolTip.SetTip(toggle, L10n.T(applied ? L10n.T("Common.Enabled") : L10n.T("Common.Disabled"))); }
            catch (Exception error)
            {
                reverting = true;
                try { toggle.IsChecked = applied; } finally { reverting = false; }
                Report(toggle, error);
            }
        };
        return toggle;
    }
    public static Control Range(double min, double max, double value, Action<double> changed, string suffix = "")
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        var label = Text(value.ToString("0") + suffix, 12, true); label.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) { label.Text = slider.Value.ToString("0") + suffix; changed(slider.Value); } };
        return Stack(slider, label);
    }
    public static ScrollViewer Scroll(Control content) => new() { Content = content, Background = Brushes.Transparent, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
}
public sealed class PlayerDialog : Window
{
    private PlayerDialog(string title, string description, Control? content, Action<PlayerDialog> accept, string acceptText)
    {
        InputCommitService.Install(this);
        WindowDecorations = WindowDecorations.None; CanResize = false; Width = 560; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; MaxHeight = 650;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Nonet/Assets/icon.ico")));
        var header = Ui.Text(title, 20); header.FontWeight = FontWeight.SemiBold;
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var panel = Ui.Stack(header, Ui.Text(description, 13, true)); panel.Spacing = 18;
        if (content is not null) panel.Children.Add(content);
        var buttons = Ui.Actions(Ui.Button(L10n.T("Common.Cancel"), () => Close()), Ui.Button(acceptText, () => accept(this), true)); buttons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right; panel.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(28), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("DividerBrush"), Background = Ui.Brush("SurfaceBrush"), Child = Ui.Scroll(panel) };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    public static Task<bool> Confirm(Window owner, string title, string description, string accept = "Common.Confirm")
        => new PlayerDialog(title, description, null, w => w.Close(true), accept) { MaxHeight = Math.Clamp(owner.Bounds.Height - 28, 300, 650), Width = Math.Clamp(owner.Bounds.Width - 40, 350, 560) }.ShowDialog<bool>(owner);
    public static Task<bool?> Uninstall(Window owner, string title)
    {
        var delete = false; var toggle = Ui.Toggle(false, value => delete = value);
        // 独立横向行避免通用设置行在窄窗口中把开关折到下一行。
        var row = new Grid { Name = "PluginDeleteFilesRow", ColumnDefinitions = new("*,Auto"), ColumnSpacing = 16 };
        var label = Ui.Text(L10n.T("Plugins.DeleteFiles")); label.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        toggle.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; toggle.Name = "PluginDeleteFilesToggle";
        row.Children.Add(label); Grid.SetColumn(toggle, 1); row.Children.Add(toggle);
        return new PlayerDialog(title, L10n.T("Plugins.UninstallChoice"), row, w => w.Close((bool?)delete), L10n.T("Common.Uninstall")) { Width = Math.Clamp(owner.Bounds.Width - 40, 350, 560) }.ShowDialog<bool?>(owner);
    }
    public static Task<string?> Choose(Window owner, string title, string description, IReadOnlyList<string> values)
    {
        var selector = new ComboBox { ItemsSource = values, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, MinHeight = 44 };
        return new PlayerDialog(title, description, selector, window => window.Close(selector.SelectedItem as string), L10n.T("Common.Confirm")) { DataContext = owner.DataContext, MaxHeight = Math.Clamp(owner.Bounds.Height - 28, 300, 650), Width = Math.Clamp(owner.Bounds.Width - 40, 350, 560) }.ShowDialog<string?>(owner);
    }
    public static Task<string?> Prompt(Window owner, string title, string description, string initial = "", bool multiline = false, string acceptText = "Common.Save")
    {
        var input = new TextBox { Text = initial, PlaceholderText = L10n.T("Common.EnterText"), AcceptsReturn = multiline, MinHeight = multiline ? 180 : 44, MaxHeight = 300, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        var dialog = new PlayerDialog(title, description, input, w => w.Close(input.Text ?? ""), acceptText) { MaxHeight = Math.Clamp(owner.Bounds.Height - 28, 300, 650), Width = Math.Clamp(owner.Bounds.Width - 40, 350, 560) };
        dialog.Opened += (_, _) => { input.Focus(); if (!multiline) input.SelectAll(); };
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter && !multiline) { dialog.Close(input.Text ?? ""); e.Handled = true; } };
        return dialog.ShowDialog<string?>(owner);
    }
}
