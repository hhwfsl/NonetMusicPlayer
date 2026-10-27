using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>覆盖字段叠加、按路由覆盖、快速取消、属性恢复及实际页面切换，不依赖私人插件。</summary>
internal static class PageTransitionChecks
{
    public static void Run(string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", null);
        var lower = new StyleSheet { Id = "fixture.motion", Name = "Motion", PageTransitions = new() { Defaults = new() { DurationMs = 320, OffsetX = 18, OffsetY = 0 },
            Rules = [new() { To = "playlist:*", Profile = new() { Effect = "slide" } }] } };
        var upper = new StyleSheet { Id = "fixture.upper", Name = "Upper", PageTransitions = new() { Defaults = new() { StartOpacity = .75 },
            Rules = [new() { From = "settings", To = "playlist:*", Profile = new() { DurationMs = 240 } }] } };
        StyleSheetContract.Validate(lower); StyleSheetContract.Validate(upper);
        var result = PageTransitionContract.Resolve([lower, upper], "settings", "playlist:abc");
        Check(result is { Effect: "slide", DurationMs: 240, OffsetX: 18, OffsetY: 0, StartOpacity: .75 }, "Fields and matching routes compose across layers.");
        var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(lower, StyleSheetJson.Default.StyleSheet), StyleSheetJson.Default.StyleSheet)!;
        Check(roundTrip.PageTransitions!.Rules[0].To == "playlist:*" && ExtensionContract.Capabilities.Contains(PageTransitionContract.Capability), "Source-generated style JSON and capability negotiation.");
        upper.PageTransitions.Defaults.DurationMs = 1001;
        var rejected = false; try { StyleSheetContract.Validate(upper); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Unbounded duration rejected."); upper.PageTransitions.Defaults.DurationMs = null;
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var probe = new Button { Content = "Motion", Opacity = .6, RenderTransform = new ScaleTransform(1.1, 1.1) }; var original = probe.RenderTransform;
        var host = new Window { Content = probe, Width = 250, Height = 120 }; host.Show(); Pump(2);
        using (var motion = new InterfaceMotion())
        {
            var offsets = new List<double>(); var opacities = new List<double>();
            probe.PropertyChanged += (_, change) =>
            {
                if (change.Property == Avalonia.Visual.OpacityProperty) opacities.Add(probe.Opacity);
                if (change.Property != Avalonia.Visual.RenderTransformProperty || probe.RenderTransform is not MatrixTransform matrix) return;
                offsets.Add(matrix.Matrix.M32);
                matrix.PropertyChanged += (_, property) => { if (property.Property == MatrixTransform.MatrixProperty) offsets.Add(matrix.Matrix.M32); };
            };
            motion.EnterPage(probe, PageTransitionContract.Default with { Effect = "fade-slide", DurationMs = 70 }); Pump(30);
            Check(!motion.IsRunning, "Completion reached before checking the full property sequence.");
            for (var i = 1; i < offsets.Count; i++) Check(offsets[i] <= offsets[i - 1] + .001, "Page offset must never rebound when the animation binding is removed.");
            for (var i = 1; i < opacities.Count; i++) Check(opacities[i] >= opacities[i - 1] - .001, "Page opacity must never dim again during final cleanup.");
            motion.EnterPage(probe, PageTransitionContract.Default);
            if (InterfaceMotion.IsEnabled) Check(motion.IsRunning && probe.Opacity < .6 && !ReferenceEquals(probe.RenderTransform, original), "Entry starts before the first frame.");
            Pump(4); motion.Cancel();
            Check(!motion.IsRunning && Math.Abs(probe.Opacity - .6) < .001 && ReferenceEquals(probe.RenderTransform, original), "Cancellation restores exact opacity and transform objects.");
            motion.EnterPage(probe, PageTransitionContract.Default with { Effect = "slide", DurationMs = 70 }); Pump(30);
            Check(!motion.IsRunning && Math.Abs(probe.Opacity - .6) < .001 && ReferenceEquals(probe.RenderTransform, original), "Natural completion restores underlying properties.");
            for (var i = 0; i < 12; i++) motion.EnterPage(probe, PageTransitionContract.Default); motion.Cancel(); Pump(3);
            Check(!motion.IsRunning && ReferenceEquals(probe.RenderTransform, original), "Superseded completions cannot restore stale transforms.");
        }
        host.Close();
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root, "data"))), new SilentAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 750 }; window.Show(); Pump(3);
        var pageMotion = (InterfaceMotion)typeof(MainWindow).GetField("_pageMotion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        vm.Navigate("history"); if (InterfaceMotion.IsEnabled) Check(pageMotion.IsRunning, "Default navigation uses the shared transition service.");
        vm.Navigate("settings"); var pageTransform = window.FindControl<ContentControl>("AlternatePage")!.RenderTransform;
        vm.ApplySettings(); Check(ReferenceEquals(window.FindControl<ContentControl>("AlternatePage")!.RenderTransform, pageTransform), "Same-page settings updates preserve the current transition instance."); Pump(30);
        var disabled = new StyleSheet { Id = "fixture.disable", Name = "Disable plugin entry", PageTransitions = new() { Rules = [new() { To = "plugins", Profile = new() { Enabled = false } }] } };
        window.ApplyInterfaceLayers("fixture.transitions", [disabled]); vm.Navigate("plugins"); Check(!pageMotion.IsRunning, "A style can disable one route.");
        Check(!window.ResolvePageTransition("settings", "plugins").Enabled, "Effective profile discovery includes other plugin-owned layers.");
        window.ApplyInterfaceLayers("fixture.transitions", []); vm.Navigate("history");
        if (InterfaceMotion.IsEnabled) Check(pageMotion.IsRunning, "Removing a layer restores default navigation motion.");
        window.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs(); Check(!pageMotion.IsRunning, "Minimize releases transition bindings.");
        Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1"); vm.Navigate("settings"); Check(!pageMotion.IsRunning, "Reduced motion respects the accessibility preference.");
        window.Close(); Console.WriteLine("PASS page transition contract/stack/routes, immediate entry, completion/cancel restoration, rapid navigation, same-page updates, disable/remove, minimize and reduced motion.");
    }
    private static void Pump(int frames) { for (var i = 0; i < frames; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(8); } }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class SilentAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
