using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class ToolTipChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        var language = vm.Settings.Language;
        var theme = vm.Settings.Theme;
        try
        {
            foreach (var locale in new[] { "en-US", "ja-JP", "zh-CN" })
            {
                vm.Settings.Language = locale; vm.ApplySettings();
                foreach (var page in new[] { "library", "favorites", "recent", "statistics", "albums", "artists", "settings", "plugins", "lyrics" })
                {
                    vm.Navigate(page); Pump(owner);
                    var buttons = owner.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToArray();
                    var missing = buttons.Where(b => string.IsNullOrWhiteSpace(ToolTip.GetTip(b)?.ToString())).ToArray();
                    Require(missing.Length == 0, locale + " " + page + " buttons have semantic tooltips; missing: " + string.Join(", ", missing.Select(b => b.Name + ":" + b.Content?.GetType().Name + ":" + b.Content)));
                    foreach (var action in buttons.Where(b => b.Content is string s && s.Any(char.IsLetter)))
                        Require(Tip(action).Contains((string)action.Content!), "Whole button includes full name: " + action.Content);
                }
                vm.Navigate("statistics"); Pump(owner);
                var date = owner.GetVisualDescendants().OfType<StatisticsDatePicker>().Single();
                date.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
                foreach (var day in date.CalendarContent.GetVisualDescendants().OfType<Button>())
                    Require(!string.IsNullOrWhiteSpace(Tip(day)), "All calendar day/navigation buttons have names");
                foreach (var selector in date.CalendarContent.GetVisualDescendants().OfType<ComboBox>())
                    Require(Tip(selector).Contains(AutomationProperties.GetName(selector)!), "Calendar selections have named tooltips");
                date.CalendarContent.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("StatisticsDay_") == true && b.IsEnabled).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            }
            vm.Settings.Language = "en-US"; vm.ApplySettings();
            const string full = "The full translated name of a button that cannot fit within the available width";
            var button = new Button { Content = full, Width = 125 };
            var described = new Button { Content = "Complete action", Width = 125 }; ToolTip.SetTip(described, "Additional help");
            var disabled = new Button { Content = "Unavailable action", IsEnabled = false };
            var caption = new TextBlock { Text = "Composite action", TextWrapping = TextWrapping.NoWrap };
            var composite = new Button { Content = new Grid { Children = { new VectorIcon { Kind = IconKind.Edit }, caption } } };
            var choice = new ComboBox { ItemsSource = new[] { "First option", "Second option", "Common.Fill" }, SelectedIndex = 0,
                ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((value, _) => new TextBlock { Text = L10n.T(value) }) }; AutomationProperties.SetName(choice, "Setting name");
            var input = new TextBox { Text = "private-access-token-123", PlaceholderText = "Enter access token", PasswordChar = '●' };
            var path = new TextBox { Text = @"D:\Music\A long path to an audio file.flac", IsReadOnly = true }; AutomationProperties.SetName(path, "File path");
            var clipped = new TextBlock { Text = full, Width = 125, TextWrapping = TextWrapping.NoWrap };
            var toggle = new ToggleSwitch { OnContent = "Enabled", OffContent = "Disabled" }; AutomationProperties.SetName(toggle, "Feature name");
            var host = new Window { Width = 260, Height = 560, Content = new StackPanel { Children = { button, described, disabled, composite, choice, input, path, clipped, toggle } } };
            try
            {
                host.Show(); Pump(host);
                Require(Tip(button) == full, "Button hint covers its whole hit area, not only its label");
                Require(Tip(described).Contains("Complete action") && Tip(described).Contains("Additional help"), "Existing action explanation is retained");
                Require(Tip(disabled) == "Unavailable action" && ToolTip.GetShowOnDisabled(disabled), "Disabled actions retain hints");
                Require(Tip(composite) == "Composite action" && ToolTip.GetTip(caption) is null, "Composite button owns single hint");
                caption.Text = "Updated live caption"; Pump(host); Require(Tip(composite) == caption.Text, "In-place translated caption updates parent hint");
                button.Content = "New button state"; Require(Tip(button) == "New button state", "Button state updates hint"); button.Content = full;
                ToolTip.SetTip(described, "Updated help"); Require(Tip(described).Contains("Updated help") && !Tip(described).Contains("Additional help"), "Externally updated descriptions stay live");
                choice.SelectedIndex = 1; Pump(host); Require(Tip(choice).Contains("Setting name") && Tip(choice).Contains("Second option"), "Selection name and value update");
                choice.SelectedIndex = 2; Pump(host); Require(Tip(choice).Contains(L10n.T("Common.Fill")) && !Tip(choice).Contains("填充"), "Selection tooltip uses the translated template, not the stored key");
                choice.IsDropDownOpen = true; Pump(host);
                var option = choice.GetRealizedContainers().OfType<ComboBoxItem>().Single(i => Equals(i.Content, "Common.Fill"));
                Require(Tip(option) == L10n.T("Common.Fill"), "Dropdown item tooltip matches its translated display"); choice.IsDropDownOpen = false; Pump(host);
                Require(!Tip(input).Contains(input.Text!) && Tip(input).Contains("Enter access token"), "Never expose editable token/password contents");
                Require(Tip(path).Contains(path.Text!) && Tip(path).Contains("File path"), "Read-only metadata and path hints show full values");
                Require(clipped.TextLayout.TextLines.Any(l => l.HasCollapsed) && Tip(clipped) == full, "Ellipsized passive text retains full content");
                toggle.IsChecked = true; Require(Tip(toggle).Contains("Enabled") && Tip(toggle).Contains("Feature name"), "Toggle hint follows state");
                ToolTip.SetIsOpen(button, true); Pump(host);
                // Tooltip popups are separate visual roots; use the public tooltip instance.
                var tooltipProperty = (AvaloniaProperty)typeof(ToolTip).GetField("ToolTipProperty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
                var actual = button.GetValue(tooltipProperty) as ToolTip ?? throw new InvalidOperationException("Tooltip was not instantiated");
                Require(actual.IsVisible && actual.MaxWidth == 520, "Full-name tooltip actually opens");
                var tipText = actual.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == full);
                Require(tipText.TextWrapping == TextWrapping.Wrap && tipText.TextTrimming == TextTrimming.None && !tipText.TextLayout.TextLines.Any(l => l.HasCollapsed), "Tooltip text wraps, never ellipsizes again");
                foreach (var variant in new[] { "Dark", "Light" })
                {
                    vm.Settings.Theme = variant; vm.ApplySettings(); Pump(host);
                    Require(tipText.Foreground is ISolidColorBrush foreground && Application.Current!.Resources["TextPrimaryBrush"] is ISolidColorBrush expected && foreground.Color == expected.Color, "Tooltip text follows theme for readable contrast");
                }
                using var frame = host.CaptureRenderedFrame(); frame?.Save(Path.Combine(output, "beta15-tooltips.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                ToolTip.SetIsOpen(button, false);
                ToolTip.SetShowDelay(button, 0);
                host.MouseMove(new Point(255, 555)); Pump(host);
                host.MouseMove(button.TranslatePoint(new Point(2, button.Bounds.Height / 2), host)!.Value); Pump(host);
                Require(ToolTip.GetIsOpen(button), "Hovering button padding opens its full-name hint");
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (actual.Opacity < .99 && DateTime.UtcNow < deadline) { Thread.Sleep(15); Pump(host); }
                var tooltipRoot = TopLevel.GetTopLevel(actual);
                using var tipFrame = tooltipRoot?.CaptureRenderedFrame(); tipFrame?.Save(Path.Combine(output, "beta15-tooltip-popup.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                host.MouseMove(new Point(255, 555)); Pump(host); ToolTip.SetIsOpen(button, false);
            }
            finally { host.Close(); }
        }
        finally { vm.Settings.Language = language; vm.Settings.Theme = theme; vm.ApplySettings(); vm.Navigate("songs"); Pump(owner); }
        Console.WriteLine("PASS BETA15: full button names across three languages/pages, preserved help, live composite/state/selection hints, disabled controls, private-input safety, read-only paths, clipped text and opened wrapping tooltip");
    }
    private static string Tip(Control control) => ToolTip.GetTip(control)?.ToString() ?? "";
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Beta15: " + message); }
}
