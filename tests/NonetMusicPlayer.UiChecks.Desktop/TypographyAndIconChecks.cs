using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class TypographyAndIconChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        var storage = new AppStorage(Path.Combine(output, "lyrics-restore-" + Guid.NewGuid().ToString("N")));
        var state = storage.Load(); state.Settings.DesktopLyricsVisible = true; storage.Save(state);
        using (var restored = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer()))
        {
            var main = new MainWindow { DataContext = restored }; main.Show(); Pump(main);
            var lyrics = (DesktopLyricsWindow)typeof(MainWindow).GetField("_desktopLyrics", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
            var icon = (VectorIcon)main.FindControl<Button>("PlayerDesktopLyrics")!.Content!;
            Require(lyrics.IsVisible && icon.Kind == IconKind.DesktopLyricsHide, "Startup restored lyrics has hide icon");
            lyrics.LockLyrics(); Pump(main); Require(icon.Kind == IconKind.Unlock, "Lock updates icon");
            lyrics.UnlockLyrics(); lyrics.HideLyrics(); Pump(main); Require(icon.Kind == IconKind.DesktopLyricsShow, "Hide updates icon");
            restored.Settings.CloseToTray = false; main.Close();
        }
        vm.Settings.DesktopLyricsFontSize = 28; vm.Navigate("settings"); Pump(owner);
        Require(!owner.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("紧凑列表") == true), "Compact list setting removed");
        var font = owner.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "DesktopLyricsFontSize");
        Require(font.Value == 28, "Font defaults to 28 px");
        var editor = font.GetVisualDescendants().OfType<TextBox>().First(); editor.Focus(); editor.Text = "24";
        Require(vm.Settings.DesktopLyricsFontSize == 28, "Font input deferred");
        font.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Pump(owner);
        Require(vm.Settings.DesktopLyricsFontSize == 24 && !editor.IsFocused, "Enter applies font and removes focus");
        vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, "短歌词", "Translation"));
        vm.Settings.DesktopLyricsVisible = false; vm.Settings.DesktopLyricsLocked = false;
        using (var lyrics = new DesktopLyricsWindow(owner, vm))
        {
            lyrics.Toggle(); Pump(lyrics);
            var primary = lyrics.GetVisualDescendants().OfType<KaraokeLine>().Single(l => l.Name == "DesktopCurrentLyric");
            var translated = lyrics.GetVisualDescendants().OfType<KaraokeLine>().Single(l => l.Name == "DesktopNextLyric");
            foreach (var size in new[] { new Size(360, 120), new Size(1200, 500), new Size(400, 180) })
            {
                lyrics.Width = size.Width; lyrics.Height = size.Height; Pump(lyrics);
                Require(primary.TextSize == 24 && translated.TextSize == 24d * 22 / 32, "Resizing never changes font size");
            }
            var fixedSize = new Size(lyrics.Width, lyrics.Height);
            vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, new string('长', 80), new string('译', 55))); Pump(lyrics);
            // 桌面长句保持单行并横向滚动；换行只适用于完整歌词页，不能自动撑大桌面框。
            Require(!primary.Wrap && !translated.Wrap && primary.NaturalSize(primary.TextSize).Width > primary.Bounds.Width, "Long desktop lyric uses a fixed single-line marquee");
            Require(new Size(lyrics.Width, lyrics.Height) == fixedSize && primary.TextSize == 24, "Long lyric never changes window or explicit font size");
            foreach (var line in new[] { primary, translated })
                Require(line.Bounds.Height >= line.WrappedSize(line.Bounds.Width).Height - 1 && line.TranslatePoint(new Point(0, line.Bounds.Height), lyrics)!.Value.Y < lyrics.Bounds.Height, "Single-line text vertically fits overlay");
            vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, "透明文字测试", "Transparent")); Pump(lyrics);
            using var image = new RenderTargetBitmap(new PixelSize((int)lyrics.Bounds.Width, (int)lyrics.Bounds.Height)); image.Render((Control)lyrics.Content!);
            image.Save(Path.Combine(output, "beta13-transparent-lyrics.png"), PngBitmapEncoderOptions.Default);
            using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = pixels.Lock())
            {
                image.CopyPixels(buffer); var bytes = new byte[buffer.RowBytes * buffer.Size.Height]; Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                var painted = 0; var transparent = 0;
                for (var row = 0; row < buffer.Size.Height; row++)
                for (var column = 0; column < buffer.Size.Width; column++)
                {
                    var offset = row * buffer.RowBytes + column * 4;
                    if (bytes[offset + 3] == 0) { transparent++; Require(bytes[offset] == 0 && bytes[offset + 1] == 0 && bytes[offset + 2] == 0, "Premultiplied transparent pixels are clean"); }
                    else painted++;
                }
                Require(transparent > bytes.Length / 4 * .85 && painted > 100, "Surface has text ink only, not opaque black rectangles");
            }
            lyrics.HideLyrics();
        }
        vm.Settings.DesktopLyricsFontSize = 28; vm.ApplySettings();
        var persisted = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(vm.Settings, AppStorage.Json), AppStorage.Json)!;
        Require(persisted.DesktopLyricsFontSize == 28, "Font persists"); persisted.DesktopLyricsFontSize = double.NaN; persisted.Validate(); Require(persisted.DesktopLyricsFontSize == 28, "Invalid font resets safely");
        vm.Navigate("statistics"); Pump(owner);
        var picker = owner.GetVisualDescendants().OfType<StatisticsDatePicker>().Single();
        foreach (var target in new[] { new DateTime(2004, 2, 29), new DateTime(StatisticsDatePicker.MinimumYear, 1, 1), new DateTime(StatisticsDatePicker.MaximumYear, 12, 31) })
        {
            picker.SelectedDate = new DateTime(2026, 10, 3);
            picker.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            var year = picker.CalendarContent.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "StatisticsYear");
            year.SelectedItem = target.Year; Pump(owner);
            var month = picker.CalendarContent.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "StatisticsMonth"); month.SelectedIndex = target.Month - 1; Pump(owner);
            var presenter = picker.CalendarContent.GetVisualAncestors().OfType<FlyoutPresenter>().Single();
            Require(presenter.BorderThickness == new Thickness(0) && presenter.GetVisualDescendants().OfType<Border>().First(b => b.Name == "LayoutRoot").BorderThickness == new Thickness(0), "Calendar outer template has no border");
            var frame = (Control)picker.CalendarContent.Parent!;
            using var snapshot = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(frame.Bounds.Width), (int)Math.Ceiling(frame.Bounds.Height))); snapshot.Render(frame); snapshot.Save(Path.Combine(output, $"beta13-calendar-{target.Year}.png"), PngBitmapEncoderOptions.Default);
            var day = picker.CalendarContent.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StatisticsDay_" + target.ToString("yyyyMMdd")); day.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            Require(picker.SelectedDate == target, "Direct year selection reaches leap day and date endpoints");
        }
        foreach (var page in new[] { "artists", "albums" })
        {
            vm.Navigate(page); Pump(owner);
            var card = owner.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "MusicGroupCard");
            Require(card.GetVisualDescendants().OfType<Button>().Count() == 1 && !card.GetVisualDescendants().OfType<VectorIcon>().Any(i => i.Kind == IconKind.More), "Card contains open button only");
            var point = card.TranslatePoint(new Point(40, 40), owner)!.Value; owner.MouseDown(point, MouseButton.Right); owner.MouseUp(point, MouseButton.Right); Pump(owner);
            var menu = (ContextMenu)typeof(MainWindow).GetField("_activeMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
            Require(menu.IsOpen && menu.Items.OfType<MenuItem>().Any(), "Card context menu retained"); menu.Close();
            card.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            Require(owner.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "ClassificationMore"), "Detail more action retained");
        }
        vm.Navigate("songs"); Pump(owner);
        Console.WriteLine("PASS BETA13: startup lyrics icon, fixed/deferred/persisted font, fixed marquee and clean alpha, removed compact setting, border-free calendar/direct year, cards with context/detail actions");
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Beta13: " + message); }
}
