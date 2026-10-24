using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>跟踪真实控件的挂接、变换和编辑状态，避免截图漏过单帧样式回退。</summary>
internal static class SettingsContinuityChecks
{
    public static void Run(string output, string? preset)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Directory.CreateDirectory(output);
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")))), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = vm }; window.Show(); vm.Navigate("settings"); Pump();
        var plain = new StyleSheet { Id = "fixture.settings-style", Name = "Settings fixture", Rules = [new() { Type = "Control", Classes = ["settings-continuity"], Motion = new() { EnterOffsetY = 14, EnterDurationMs = 90 } }] };
        CheckStyle(plain, "type-style");
        plain.Layout = new() { Regions = new() { ["ContentSurface"] = new() { Kind = "grid", Children = [new() { Kind = "ref", Target = "PageContentGrid" }] } } };
        CheckStyle(plain, "composed-style");
        if (preset is not null) { using var stream = File.OpenRead(preset); CheckStyle(StyleSheetContract.Read(stream), "preset"); }
        window.Close();
        Console.WriteLine("PASS settings retain page, composition, entrance transform, edit draft and viewport across scalar changes; structural changes still apply.");

        void CheckStyle(StyleSheet sheet, string name)
        {
            window.StyleLayers.Apply("fixture.settings", []); window.ApplyInterfaceLayers("fixture.settings", []);
            var page = window.GetVisualDescendants().OfType<SettingsView>().Single(); page.Classes.Set("settings-continuity", true);
            if (sheet != plain) sheet.Rules.Add(plain.Rules[0]);
            window.StyleLayers.Apply("fixture.settings", [sheet]); window.ApplyInterfaceLayers("fixture.settings", [sheet]); Pump(130);
            page = window.GetVisualDescendants().OfType<SettingsView>().Single();
            var hex = page.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "AccentHex"); hex.Focus(); hex.Text = "#123456";
            var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Bounds.Height > 300 && s.Extent.Height > s.Viewport.Height + 100); scroll.Offset = new(0, 320); Pump();
            var offset = scroll.Offset; var transform = page.RenderTransform; var detached = 0;
            void Detached(object? sender, VisualTreeAttachmentEventArgs e) => detached++;
            page.DetachedFromVisualTree += Detached;
            foreach (var change in new Action[] { () => vm.Settings.TerminalScrollbackLines++, () => vm.Settings.OptimizeMemoryWhenMinimized = !vm.Settings.OptimizeMemoryWhenMinimized,
                () => vm.Settings.Accent = "#C6548C", () => vm.Settings.Theme = "Dark", () => vm.Settings.Theme = "Light" })
            {
                change(); vm.ApplySettings(); Pump();
                Check(ReferenceEquals(page, window.GetVisualDescendants().OfType<SettingsView>().Single()), "Settings page was recreated.");
                Check(detached == 0, name + ": settings were detached during a scalar change.");
                Check(ReferenceEquals(transform, page.RenderTransform), name + ": entrance animation was rebuilt.");
                Check(hex.IsKeyboardFocusWithin && hex.Text == "#123456", name + ": an unfinished edit lost focus or text.");
                Check(Math.Abs(scroll.Offset.Y - offset.Y) < 1, name + ": viewport moved.");
            }
            page.DetachedFromVisualTree -= Detached; window.FocusManager?.Focus(null); Pump();
            using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.ApplyInterfaceLayers("fixture.settings", []); window.StyleLayers.Apply("fixture.settings", []); Pump();
            vm.Settings.NavigationRight = !vm.Settings.NavigationRight; vm.ApplySettings(); Pump();
            Check(window.InterfaceSettings.NavigationRight == vm.Settings.NavigationRight, "A structural navigation preference was ignored.");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump(int frames = 30) { for (var i = 0; i < frames; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(3); } }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } } public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
