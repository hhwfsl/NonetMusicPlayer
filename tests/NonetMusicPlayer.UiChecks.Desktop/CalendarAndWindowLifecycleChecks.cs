using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class CalendarAndWindowLifecycleChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        Require(owner.FindControl<TextBlock>("ApplicationVersionLabel")!.Text == vm.VersionText && !string.IsNullOrEmpty(vm.VersionText), "Title version follows assembly metadata, not a hardcoded prior release");
        vm.Navigate("statistics"); Pump(owner);
        var picker = owner.GetVisualDescendants().OfType<StatisticsDatePicker>().Single();
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var button = picker.GetVisualDescendants().OfType<Button>().Single();
            Require(button.Focus(), "Date button obtains keyboard focus before popup");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), owner)!.Value;
            owner.MouseDown(point, MouseButton.Left); owner.MouseUp(point, MouseButton.Left); Pump(owner);
            var next = picker.CalendarContent.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StatisticsNextMonth");
            next.Focus(); next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            var selected = picker.CalendarContent.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("StatisticsDay_") == true);
            var expected = DateTime.ParseExact(selected.Name![14..], "yyyyMMdd", null);
            selected.Focus(); selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            Require(picker.SelectedDate == expected, "Focused month navigation and date selection apply without inherited template errors");
        }
        vm.Navigate("settings"); Pump(owner);
        Require(!owner.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("定时停止") == true), "Sleep timer removed from settings");

        var package = Path.Combine(output, "alignment-" + Guid.NewGuid().ToString("N") + ".impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(JsonSerializer.Serialize(new PluginManifest { Id = "test.beta12-align", Name = "开关居中检查", Type = "theme", Description = "Vertical track bounds regression", Author = "UI checks" }, AppStorage.Json));
        var plugin = vm.Plugins.Install(package); vm.Navigate("plugins"); Pump(owner);
        var toggle = owner.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "PluginEnabled" && Avalonia.Automation.AutomationProperties.GetName(t)?.StartsWith(plugin.Name) == true);
        var glyph = toggle.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SwitchKnobBounds");
        var uninstall = owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PluginUninstall" && Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith(plugin.Name) == true);
        var toggleCenter = glyph.TranslatePoint(new Point(0, glyph.Bounds.Height / 2), owner)!.Value.Y;
        var actionCenter = uninstall.TranslatePoint(new Point(0, uninstall.Bounds.Height / 2), owner)!.Value.Y;
        Require(Math.Abs(toggleCenter - actionCenter) < 1, $"Rendered toggle track aligns vertically with actions ({toggleCenter} vs {actionCenter})");
        using (var frame = owner.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "beta12-plugin-alignment.png"), PngBitmapEncoderOptions.Default);
        vm.Plugins.Uninstall(plugin); vm.Navigate("songs"); Pump(owner);

        foreach (var height in new[] { 96d, 140d })
        {
            vm.Settings.PlayerHeight = height;
            vm.Settings.PlayerTop = false; vm.ApplySettings(); Pump(owner);
            var grip = owner.FindControl<Border>("PlayerResizeGrip")!;
            var surface = owner.FindControl<Border>("PlayerSurface")!;
            var play = owner.FindControl<Button>("PlayerPlay")!;
            var normalGap = grip.TranslatePoint(default, surface)!.Value.Y;
            Require(normalGap >= 0 && grip.TranslatePoint(new Point(0, grip.Bounds.Height), surface)!.Value.Y <= play.TranslatePoint(default, surface)!.Value.Y + 1, "Bottom player grip is above transport");
            vm.Settings.PlayerTop = true; vm.ApplySettings(); Pump(owner);
            var topGap = surface.Bounds.Height - grip.TranslatePoint(new Point(0, grip.Bounds.Height), surface)!.Value.Y;
            Require(Math.Abs(topGap - normalGap) < 1, $"Mirrored grip edge spacing at player height {height}: {normalGap} vs {topGap}");
            Require(grip.TranslatePoint(default, surface)!.Value.Y >= play.TranslatePoint(new Point(0, play.Bounds.Height), surface)!.Value.Y - 1, "Top player grip is below transport");
        }
        vm.Settings.PlayerTop = false; vm.Settings.PlayerHeight = 96; vm.ApplySettings(); Pump(owner);

        var backgroundFolder = Path.Combine(vm.Storage.ArtworkFolder, "Backgrounds"); Directory.CreateDirectory(backgroundFolder);
        var source = Path.Combine(vm.Storage.Root, "fixture-cover.png");
        var backgrounds = Enumerable.Range(0, 8).Select(i => Path.Combine(backgroundFolder, $"prune-{i}.png")).ToArray();
        for (var i = 0; i < backgrounds.Length; i++) { File.Copy(source, backgrounds[i]); File.SetLastWriteTimeUtc(backgrounds[i], DateTime.UtcNow.AddMinutes(i - 10)); }
        var unrelated = Path.Combine(backgroundFolder, "notes.txt"); File.WriteAllText(unrelated, "Not a managed image");
        vm.Settings.BackgroundImagePath = backgrounds[0]; vm.ApplySettings();
        Require(Directory.GetFiles(backgroundFolder, "*.png").Length == 3 && File.Exists(backgrounds[0]), "Apply retains active plus at most two recent background copies");
        AppBackgroundService.CleanupUnused(vm.Storage, backgrounds[0], 0);
        Require(Directory.GetFiles(backgroundFolder, "*.png").SequenceEqual(new[] { backgrounds[0] }) && File.Exists(source) && File.Exists(unrelated), "Exit policy retains active image only and preserves originals/non-images");
        AppBackgroundService.CleanupUnused(vm.Storage, null, 0);
        Require(!Directory.GetFiles(backgroundFolder, "*.png").Any() && File.Exists(source), "No background clears managed copies without touching external original");
        vm.Settings.BackgroundImagePath = null; vm.ApplySettings();

        var line = new KaraokeLine { Width = 400, Height = 70, TextSize = 32 }; line.Update("当前歌词", 0);
        line.Measure(new(400, 70)); line.Arrange(new(0, 0, 400, 70));
        var original = line.TextBounds;
        line.Update("当前歌词已切换到更长的一句歌词", 0);
        Require(line.TextBounds.Width > original.Width, "Input region recalculates text bounds before next render");
        line.Update("", 0); Require(line.TextBounds.Width == 0, "Empty lyrics produce no stale input region");
        var exitStorage = new AppStorage(Path.Combine(output, "background-exit-" + Guid.NewGuid().ToString("N")));
        var exitFolder = Path.Combine(exitStorage.ArtworkFolder, "Backgrounds"); Directory.CreateDirectory(exitFolder);
        var exitCopies = Enumerable.Range(0, 6).Select(i => Path.Combine(exitFolder, $"exit-{i}.png")).ToArray();
        foreach (var path in exitCopies) File.Copy(source, path);
        var exitState = exitStorage.Load(); exitState.Settings.BackgroundImagePath = exitCopies[0]; exitStorage.Save(exitState);
        using (var exitVm = new MainViewModel(new MusicLibraryScanner(exitStorage), new ApplicationIntegrationChecks.SilentAudioPlayer()))
            Require(Directory.GetFiles(exitFolder).Length == 3 && File.Exists(exitCopies[0]), "Startup prunes stale copies to active plus two");
        Require(Directory.GetFiles(exitFolder).SequenceEqual(new[] { exitCopies[0] }), "Actual view-model shutdown retains active background only");
        var linkStorage = new AppStorage(Path.Combine(output, "background-link-" + Guid.NewGuid().ToString("N")));
        var linkFolder = Path.Combine(linkStorage.ArtworkFolder, "Backgrounds");
        try
        {
            Directory.CreateSymbolicLink(linkFolder, exitFolder);
            Require(AppBackgroundService.CleanupUnused(linkStorage, null, 0) == 0 && File.Exists(exitCopies[0]), "Linked managed folder is safely skipped, never traversed");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException) { Console.WriteLine("SKIP background symlink creation: OS disallowed test fixture"); }
        Console.WriteLine("PASS BETA12: focused calendar interaction, centered toggle glyph, symmetric player grip, bounded background cleanup and eager desktop lyric geometry");
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Beta12: " + message); }
}
