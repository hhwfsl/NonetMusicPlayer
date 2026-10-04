using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed class ColorPickerDialog : Window
{
    private ColorPickerDialog(Color initial)
    {
        InputCommitService.Install(this);
        Width = 500; SizeToContent = SizeToContent.Height; CanResize = false; WindowDecorations = WindowDecorations.None; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var wheel = new ColorWheel { Name = "AccentColorWheel", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }; wheel.SetColor(initial);
        var preview = new Border { Name = "ColorPreview", Width = 50, Height = 38, CornerRadius = new(7), Background = new SolidColorBrush(initial) };
        var hex = new TextBox { Name = "ColorHex", Text = Hex(initial), Width = 130 };
        var brightness = new Slider { Minimum = 0, Maximum = 100, Value = wheel.Brightness * 100 };
        var red = Channel(initial.R); var green = Channel(initial.G); var blue = Channel(initial.B);
        red.Name = "ColorRed"; green.Name = "ColorGreen"; blue.Name = "ColorBlue";
        foreach (var (control, label) in new (Control, string)[] { (red, L10n.T("Common.RedRTo")), (green, L10n.T("Common.GreenGTo")), (blue, L10n.T("Common.BlueBTo")), (hex, L10n.T("Common.HexColorRRGGBB")), (brightness, L10n.T("Common.BrightnessTo")), (wheel, L10n.T("Settings.ColorWheelDragToSelectHueAndSaturation")) })
        { Avalonia.Automation.AutomationProperties.SetName(control, L10n.T(label)); ToolTip.SetTip(control, L10n.T(label)); }
        var error = Ui.Text("", 12, true); bool updating = false;
        void Update(Color color, bool updateWheel)
        {
            updating = true;
            try { if (updateWheel) wheel.SetColor(color); red.Value = color.R; green.Value = color.G; blue.Value = color.B; hex.Text = Hex(color); brightness.Value = wheel.Brightness * 100; preview.Background = new SolidColorBrush(color); error.Text = ""; }
            finally { updating = false; }
        }
        void RgbChanged() { if (!updating) Update(Color.FromRgb((byte)(red.Value ?? 0), (byte)(green.Value ?? 0), (byte)(blue.Value ?? 0)), true); }
        InputCommitService.Bind(hex, value => { if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")) throw new InvalidDataException(L10n.T("Common.EnterRRGGBBSuchAsA895FF")); if (!updating) Update(Color.Parse(value), true); });
        InputCommitService.Bind(red, _ => RgbChanged()); InputCommitService.Bind(green, _ => RgbChanged()); InputCommitService.Bind(blue, _ => RgbChanged());
        wheel.ColorChanged += (_, color) => Update(color, false);
        brightness.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !updating) wheel.Brightness = brightness.Value / 100; };
        var applyHex = Ui.Button(L10n.T("Settings.UseHex"), () =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex.Text ?? "", "^#[0-9a-fA-F]{6}$")) { error.Text = L10n.T("Common.EnterRRGGBBSuchAsA895FF"); return; }
            Update(Color.Parse(hex.Text!), true);
        });
        var title = Ui.Text(L10n.T("Settings.ChooseThemeColor"), 20); title.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var rgb = Ui.Actions(Ui.Stack(Ui.Text("R", 12, true), red), Ui.Stack(Ui.Text("G", 12, true), green), Ui.Stack(Ui.Text("B", 12, true), blue)); rgb.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        var buttons = Ui.Actions(Ui.Button(L10n.T("Common.Cancel"), () => Close()), Ui.Button(L10n.T("Common.ApplyColor"), () =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex.Text ?? "", "^#[0-9a-fA-F]{6}$")) { error.Text = L10n.T("Settings.InvalidColorEnterRRGGBBOrChooseAColorAgain"); hex.Focus(); return; }
            Close(Hex(Color.Parse(hex.Text!)));
        }, true)); buttons.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        Content = new Border { Background = Ui.Brush("SurfaceBrush"), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new(1), Padding = new(26), Child = Ui.Scroll(Ui.Stack(title, Ui.Text(L10n.T("Settings.ChooseHueAndSaturationWithTheWheelAdjustBrightness"), 12, true), wheel, Ui.Text(L10n.T("Common.Brightness"), 12, true), brightness, rgb, Ui.Actions(preview, hex, applyHex), error, buttons)) };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => wheel.Dispose();
    }
    private static NumericUpDown Channel(byte value) => new() { Value = value, Minimum = 0, Maximum = 255, Increment = 1, Width = 120, FormatString = "0", ParsingNumberStyle = System.Globalization.NumberStyles.Integer };
    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    public static Task<string?> Show(Window owner, string initial) => new ColorPickerDialog(Color.Parse(initial)) { DataContext = owner.DataContext, MaxHeight = Math.Clamp(owner.Bounds.Height - 24, 360, 680) }.ShowDialog<string?>(owner);
}
