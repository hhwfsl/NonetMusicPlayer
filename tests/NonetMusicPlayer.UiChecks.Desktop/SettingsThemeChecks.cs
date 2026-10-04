using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class SettingsThemeChecks
{
    internal static void Run(MainWindow window, MainViewModel vm, string output)
    {
        var originalTheme = vm.Settings.Theme; var originalAccent = vm.Settings.Accent; var originalRadius = vm.Settings.ControlCornerRadius; var originalTouch = vm.Settings.TouchMode;
        var originalBackground = vm.Settings.BackgroundImagePath; var originalLanguage = vm.Settings.Language; var width = window.Width; var height = window.Height;
        try
        {
            Require(new NonetMusicPlayer.Desktop.Models.AppSettings().Theme == "System", "New settings follow system theme");
            foreach (var theme in new[] { "Light", "Dark" })
                foreach (var accent in new[] { "#FFFFFF", "#000000", "#FFFF00", "#00AA88", "#CC0033" })
                {
                    vm.Settings.Theme = theme; vm.Settings.Accent = accent; vm.ApplySettings(); Pump(window);
                    var color = Color.Parse(accent); var surface = ColorOf("SurfaceBrush");
                    Require(ColorOf("AccentBrush") == color, "Selected theme color is preserved");
                    Require(ThemeService.Contrast(ColorOf("AccentForegroundBrush"), color) >= 4.5, "Primary button contrast");
                    Require(ThemeService.Contrast(ColorOf("AccentTextBrush"), surface) >= 4.5, "Accent text contrast in " + theme);
                    Require(ColorOf("SurfaceHoverBrush") != surface && ColorOf("NavSelectedBrush") != surface, "Interaction states are theme-derived");
                }
            vm.Settings.ControlCornerRadius = 17; vm.Settings.TouchMode = true; vm.ApplySettings();
            Require(Application.Current!.Resources["ControlCornerRadius"] is CornerRadius radius && radius.TopLeft == 17, "Custom control radius");
            Require((double)Application.Current.Resources["ButtonMinHeight"]! >= 44 && (double)Application.Current.Resources["TrackRowMinHeight"]! >= 72, "Touch target resource sizing");
            vm.Settings.TouchMode = false; vm.Settings.Theme = "System"; vm.Settings.Accent = "#00AA88"; vm.ApplySettings();
            vm.Navigate("settings"); Pump(window);
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "SettingsSearch");
            var view = (SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!;
            Require(!view.GetVisualDescendants().OfType<Expander>().Any(), "Settings sections are directly visible, without expanders");
            search.Focus(); search.Text = "快捷键"; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
            Require(view.GetVisualDescendants().OfType<ShortcutCaptureButton>().Any(), "Settings search expands shortcuts");
            var pendingShortcut = view.GetVisualDescendants().OfType<ShortcutCaptureButton>().First(); pendingShortcut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pendingShortcut.Focus();
            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, null); window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, null); Pump(window);
            Require(pendingShortcut.Content?.ToString() == "Ctrl+K", "Unsaved shortcut captured");
            vm.Settings.Language = "en-US"; vm.ApplySettings(); L10n.SetLanguage("en-US"); Pump(window);
            view = (SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!; search = view.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "SettingsSearch"); search.Focus(); search.Text = "shortcut"; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
            Require(view.GetVisualDescendants().OfType<ShortcutCaptureButton>().First().Content?.ToString() == "Ctrl+K", "Unsaved shortcut survives live language change");
            Require(view.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Keyboard shortcuts"), "English settings immediately translated");
            vm.Settings.Language = "ja-JP"; vm.ApplySettings(); L10n.SetLanguage("ja-JP"); Pump(window);
            view = (SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!;
            Require(view.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "外観"), "Japanese settings immediately translated");
            vm.Settings.Language = "zh-CN"; vm.ApplySettings(); L10n.SetLanguage("zh-CN"); Pump(window);
            view = (SettingsView)window.FindControl<ContentControl>("AlternatePage")!.Content!; search = view.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "SettingsSearch");
            search.Focus(); search.Text = "zzzz-not-a-setting"; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
            Require(view.GetVisualDescendants().OfType<TextBlock>().Any(label => label.IsVisible && label.Text == "没有匹配的设置项"), "Settings search reports no match");
            search.Focus(); search.Text = "背景"; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
            Require(view.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "背景图片不透明度"), "Background settings discoverable");
            search.Focus(); search.Text = ""; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
            Require(!view.GetVisualDescendants().OfType<CheckBox>().Any(), "Settings enable switches use ToggleSwitch");
            foreach (var size in new[] { (640d, 480d), (980d, 660d), (1440d, 900d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; search.Focus(); search.Text = "快捷键"; window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(window);
                var grid = view.GetVisualDescendants().OfType<ResponsiveUniformGrid>().Single();
                Require(grid.Columns is >= 1 and <= 2, "Responsive shortcut columns");
                using var frame = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96)); frame.Render(window); frame.Save(Path.Combine(output, $"settings-{size.Item1:0}x{size.Item2:0}.png"), PngBitmapEncoderOptions.Default);
            }
            var fixture = Path.Combine(output, "background-fixture.png");
            using (var fixtureBitmap = new RenderTargetBitmap(new PixelSize(2048, 1200)))
            {
                using (var drawing = fixtureBitmap.CreateDrawingContext()) drawing.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(0, 0, 2048, 1200));
                fixtureBitmap.Save(fixture, PngBitmapEncoderOptions.Default);
            }
            var imported = AppBackgroundService.Import(vm.Storage, fixture);
            Require(Path.GetDirectoryName(imported) == Path.Combine(vm.Storage.ArtworkFolder, "Backgrounds"), "Background imported into managed data");
            var image = AppBackgroundService.GetImage(imported);
            Require(image is not null && Math.Max(image.Size.Width, image.Size.Height) <= 1600 && AppBackgroundService.DecodedBytes <= 1600L * 1600 * 4, "Background decode is bounded");
            AppBackgroundService.GetImage(null); Require(AppBackgroundService.DecodedBytes == 0, "Background pixels released when disabled");
            vm.Navigate("statistics"); Pump(window);
            Require(!window.GetVisualDescendants().OfType<ListeningChart>().Any(), "Listening statistics use summaries and ranking lists");
            Console.WriteLine("PASS SETTINGS/THEME: live color contrast, visible responsive sections/search/toggles, bounded background and summary statistics");
        }
        finally
        {
            vm.Settings.Theme = originalTheme; vm.Settings.Accent = originalAccent; vm.Settings.ControlCornerRadius = originalRadius; vm.Settings.TouchMode = originalTouch;
            vm.Settings.BackgroundImagePath = originalBackground; vm.Settings.Language = originalLanguage; L10n.SetLanguage(originalLanguage); vm.ApplySettings(); window.Width = width; window.Height = height; vm.Navigate("songs"); Pump(window);
        }
    }
    private static Color ColorOf(string key) => ((SolidColorBrush)Application.Current!.Resources[key]!).Color;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
}
