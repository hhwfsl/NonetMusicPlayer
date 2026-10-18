using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>背景取色只使用测试生成图片，验证默认值、取消竞态、持久化、主题同步和封面对齐。</summary>
internal static class AppearanceChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var blue = Image("blue.png", new SkiaSharp.SKColor(50, 136, 204), true);
        var red = Image("red.png", new SkiaSharp.SKColor(202, 83, 112), false);
        var transparent = Image("transparent.png", SkiaSharp.SKColors.Transparent, false);
        Check(AppBackgroundService.ExtractAccent(blue) == "#3288CC", "Large white borders do not suppress the representative color.");
        Check(AppBackgroundService.ExtractAccent(transparent) is null, "Fully transparent images have no invented color.");
        var storage = new AppStorage(Path.Combine(root, "data"));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        Check(!vm.Settings.AccentFromBackground, "Background color extraction is off by default.");
        vm.Settings.BackgroundImagePath = blue; vm.ApplySettings(); Check(vm.Settings.Accent == "#A895FF", "Background alone does not change manual accent.");
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump(); vm.Navigate("settings"); Pump();
        var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().Single(c => c.Name == "AccentFromBackground");
        toggle.IsChecked = true; Wait(vm.BackgroundAccentUpdate);
        Check(vm.Settings.Accent == "#3288CC" && ((ISolidColorBrush)Application.Current!.Resources["AccentBrush"]!).Color == Color.Parse("#3288CC"), "Toggle updates settings and all theme resources immediately.");
        var accent = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AccentHex");
        Check(accent.Text == "#3288CC" && !accent.IsEffectivelyEnabled, "Color editor reflects the automatic value.");
        vm.Settings.BackgroundImagePath = red; vm.ApplySettings(); Wait(vm.BackgroundAccentUpdate); Check(vm.Settings.Accent == "#CA5370", "Changing background retriggers extraction.");
        vm.Settings.BackgroundImagePath = blue; vm.ApplySettings(); vm.Settings.BackgroundImagePath = red; vm.ApplySettings(); Wait(vm.BackgroundAccentUpdate); Pump();
        Check(vm.Settings.Accent == "#CA5370", "An obsolete image cannot overwrite the latest background color.");
        vm.Settings.BackgroundImagePath = blue; vm.ApplySettings(); vm.Settings.AccentFromBackground = false; vm.ApplySettings(); Pump();
        Check(vm.Settings.Accent == "#CA5370", "Turning off extraction cancels pending changes.");
        vm.Settings.AccentFromBackground = true; vm.ApplySettings(); Wait(vm.BackgroundAccentUpdate); vm.Settings.BackgroundImagePath = null; vm.ApplySettings();
        Check(vm.Settings.Accent == "#3288CC", "Removing a background keeps the last selected color.");
        vm.Save(); Check(storage.Load().Settings.AccentFromBackground, "Automatic-color preference persists.");
        SettingsContract.Validate("accentFromBackground", System.Text.Json.Nodes.JsonValue.Create(true));
        try { SettingsContract.Validate("accentFromBackground", System.Text.Json.Nodes.JsonValue.Create("true")); throw new Exception("Invalid Boolean accepted."); } catch (InvalidDataException) { }
        foreach (var language in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            vm.Settings.Language = language; vm.ApplySettings(); L10n.SetLanguage(language); Pump();
            var text = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Check(text.Contains(L10n.T("Layout.NavigationPositionHint")) && text.Contains(L10n.T("Layout.PlayerPositionHint")), "Both layout descriptions are localized.");
        }
        var dialog = new SongInfoDialog(window, new TrackItem("fixture", "Title", "Artist", "Album", "missing.wav", ".wav", 0)); dialog.Show(window); Pump();
        var cover = dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SongInfoCover");
        var heading = dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == L10n.T("Common.TrackInformation412578"));
        Check(Math.Abs(cover.TranslatePoint(default, dialog)!.Value.X - heading.TranslatePoint(default, dialog)!.Value.X) < .1, "Song information cover aligns with the left content edge.");
        dialog.Close(); window.Close(); Pump();
        Console.WriteLine("PASS automatic background color default/toggle/replacement/cancellation/persistence, UI theme synchronization, layout descriptions and song-cover alignment.");
        string Image(string name, SkiaSharp.SKColor color, bool whiteBorder)
        {
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(96, 96));
            surface.Canvas.Clear(whiteBorder ? SkiaSharp.SKColors.White : color);
            if (whiteBorder) { using var paint = new SkiaSharp.SKPaint { Color = color }; surface.Canvas.DrawRect(25, 25, 46, 46, paint); }
            using var image = surface.Snapshot(); using var bytes = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); var path = Path.Combine(root, name); using var stream = File.Create(path); bytes.SaveTo(stream); return path;
        }
    }
    private static void Wait(Task task) { var until = Environment.TickCount64 + 10000; while (!task.IsCompleted) { if (Environment.TickCount64 > until) throw new TimeoutException(); Pump(1); } task.GetAwaiter().GetResult(); Pump(); }
    private static void Pump(int count = 20) { for (var n = 0; n < count; n++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
