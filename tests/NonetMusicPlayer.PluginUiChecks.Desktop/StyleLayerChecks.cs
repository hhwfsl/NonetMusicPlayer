using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>通用风格测试只使用生成数据；实际私人插件包仅通过显式参数加入隔离验收。</summary>
internal static class StyleLayerChecks
{
    public static void Run(string output, string? package = null, string? styleFile = null)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Directory.CreateDirectory(output);
        var controls = new StackPanel { Spacing = 12, Margin = new(20) };
        var first = new Button { Content = "First", CornerRadius = new(3) }; var toggle = new ToggleSwitch { IsChecked = false }; var text = new TextBox { Text = "Type style" };
        controls.Children.Add(first); controls.Children.Add(toggle); controls.Children.Add(text);
        var window = new Window { Width = 780, Height = 580, Content = controls }; window.Show(); Pump();
        using var service = new StyleLayerService(window);
        var originalAccent = Application.Current!.Resources["AccentBrush"];
        var a = Sheet("fixture.alpha", "Button", "cornerRadius", "18");
        a.Resources["AccentBrush"] = "#1677FF";
        a.Morph = new() { DurationMs = 320, Arc = 12 };
        a.Rules.Add(new() { Type = "TextBox", Setters = new() { ["cornerRadius"] = "12" } });
        var b = Sheet("fixture.beta", "Button", "cornerRadius", "26");
        b.Morph = new() { CornerRadius = 36 };
        b.Rules.Add(new() { Type = "ToggleSwitch", Setters = new() { ["minHeight"] = 52d } });
        b.Rules.Add(new() { Type = "ToggleSwitch", States = ["checked"], Setters = new() { ["opacity"] = .65d }, DurationMs = 0 });
        b.Rules.Add(new() { Type = "Button", Classes = ["fixture-class"], Setters = new() { ["padding"] = "22" }, DurationMs = 0 });
        service.Apply("fixture.owner", [a, b]); Pump();
        Check(service.Morph is { DurationMs: 320, Arc: 12, CornerRadius: 36 }, "Omitted animation fields retain lower-layer definitions.");
        Check(first.CornerRadius.TopLeft == 26 && text.CornerRadius.TopLeft == 12 && toggle.MinHeight == 52, "Later conflicting property wins; disjoint properties coexist.");
        var future = new Button { Content = "Future" }; controls.Children.Add(future); Pump(); Check(future.CornerRadius.TopLeft == 26, "Newly attached default controls are styled without rescanning each frame.");
        first.Classes.Add("fixture-class"); Pump(); Check(first.Padding.Left == 22, "A newly added class activates its rule.");
        toggle.IsChecked = true; Pump(); Check(Math.Abs(toggle.Opacity - .65) < .001, "Checked-state rules apply to real switches.");
        toggle.IsChecked = false; Pump(); Check(toggle.Opacity == 1, "Leaving a state restores the original property.");
        var invalid = Sheet("fixture.invalid", "Button", "opacity", 9d);
        try { service.Apply("fixture.owner", [invalid]); throw new Exception("Invalid opacity accepted."); } catch (InvalidDataException) { }
        Check(first.CornerRadius.TopLeft == 26, "Rejected layers preserve active styles.");
        service.Apply("fixture.owner", [a]); Pump(); Check(first.CornerRadius.TopLeft == 18 && toggle.MinHeight != 52, "Disabling upper layers reveals the lower layer.");
        controls.Children.Remove(future); Pump(); Check(future.CornerRadius.TopLeft == 6 || future.CornerRadius.TopLeft != 18, "Detached controls release their leases.");
        service.Remove("fixture.owner"); Pump(); Check(first.CornerRadius.TopLeft == 3 && ReferenceEquals(Application.Current.Resources["AccentBrush"], originalAccent) && service.TrackedControlCount == 0, "Reset restores local properties/resources and releases controls.");
        var parts = window.GetVisualDescendants().OfType<Control>().Where(c => c.TemplatedParent == toggle).Select(c => c.GetType().Name + ":" + c.Name).ToArray();
        Console.WriteLine("Toggle parts: " + string.Join(", ", parts));
        var from = new Rect(80, 70, 240, 140); var to = new Rect(0, 0, 780, 580);
        Check(StyleMorphMotion.Sample(from, to, 28, 0, 24, 0).Bounds == from && StyleMorphMotion.Sample(from, to, 28, 0, 24, 1).Bounds == to, "Morph endpoints do not overshoot.");
        var previous = 28d;
        for (var step = 0; step <= 100; step++) { var value = StyleMorphMotion.Sample(from, to, 28, 0, 24, step / 100d); Check(value.Radius <= previous && value.Radius >= 0 && double.IsFinite(value.Bounds.X), "Radius stays continuous and nonnegative."); previous = value.Radius; }
        window.Close(); Pump();
        if (styleFile is not null)
        {
            using var input = File.OpenRead(styleFile); var supplied = StyleSheetContract.Read(input);
            var number = new NumericUpDown { Value = 1000, Minimum = 0, Maximum = 10000, Width = 300, Height = 48, TextAlignment = TextAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var numericWindow = new Window { Width = 420, Height = 130, Content = number }; numericWindow.Show(); Pump();
            using var numericStyles = new StyleLayerService(numericWindow); numericStyles.Apply("fixture.numeric", [supplied]); Pump();
            var editor = number.GetVisualDescendants().OfType<TextBox>().Single();
            Check(editor.CornerRadius.TopRight == 0 && editor.BorderThickness == new Thickness(0) && editor.Background is ISolidColorBrush { Color.A: 0 }, "Numeric input parts do not paint a rounded opaque layer over spinner buttons.");
            number.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_IncreaseButton").RaiseEvent(new(Button.ClickEvent)); Pump();
            Check(number.Value == 1001, "The spinner retains its original numeric behavior after styling.");
            using (var rendered = numericWindow.CaptureRenderedFrame()) rendered?.Save(Path.Combine(output, "numeric.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            numericWindow.Close(); Pump();
        }
        if (package is not null) VerifyPackage(output, package, styleFile!);
        Console.WriteLine("PASS style merge/override/coexistence, state changes, late controls, classes, restore, invalid rollback and continuous morph geometry.");
    }
    private static void VerifyPackage(string output, string package, string styleFile)
    {
        var data = Path.Combine(Path.GetFullPath(output), "host-" + Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(data);
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var plugin = vm.Plugins.Install(package); plugin.ManagedExecutionConsent = true;
        using var source = File.OpenRead(styleFile); var sheet = StyleSheetContract.Read(source);
        vm.Plugins.SetEnabled(plugin, true);
        var host = new MainWindow { DataContext = vm }; host.Show();
        host.ApplyInterfaceLayers("fixture.styles", [sheet]);
        Wait(() => host.StyleLayers.Catalog()["activeLayers"]!.AsArray().Count > 0);
        vm.Navigate("plugin:" + plugin.Id); Wait(() => host.GetVisualDescendants().OfType<NativeExtensionView>().Any(v => v.Content is Control));
        using (var image = host.CaptureRenderedFrame()) image?.Save(Path.Combine(output, "style-manager.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        var native = host.GetVisualDescendants().OfType<NativeExtensionView>().Single();
        var nativeContent = native.Content;
        host.ApplyInterfaceLayers("fixture.styles", [sheet]); Pump();
        Check(ReferenceEquals(native.Content, nativeContent), "Reparenting the active plugin page retains its native view and session.");
        // 插件不必提供控件预览，测试在自身夹具中加入待变形容器。
        var panel = native.Content as Panel ?? native.GetVisualDescendants().OfType<Panel>().First();
        var preview = new Border { Width = 240, Height = 90, CornerRadius = new(7), Child = new Button { Content = "Fixture" } }; panel.Children.Add(preview); Pump();
        Check(preview.CornerRadius.TopLeft == 7, "The supplied style preserves structural corners instead of styling every Border.");
        var originalParent = preview.Parent;
        var expansion = host.MorphContainerAsync(preview); Wait(() => expansion.IsCompleted); expansion.GetAwaiter().GetResult();
        Check(preview.Parent != originalParent && host.GetVisualDescendants().Any(c => c is Border { Name: "NonetMorphContainer" }), "Container expansion retains the real interactive control, not a permanent screenshot.");
        host.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); host.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Wait(() => preview.Parent == originalParent);
        Check(!host.GetVisualDescendants().Any(c => c is Border { Name: "NonetMorphContainer" }), "Esc collapses the container and restores its original parent.");
        var switches = host.GetVisualDescendants().OfType<ToggleSwitch>().ToArray();
        foreach (var border in switches.SelectMany(s => s.GetVisualDescendants()).OfType<Border>().Where(b => b.Name is not null)) Console.WriteLine($"Switch visual {border.Name}: background={border.Background}, radius={border.CornerRadius}");
        vm.Navigate("settings"); Pump(); Check(native.Content is null, "Leaving the plugin page still releases its native view."); using (var image = host.CaptureRenderedFrame()) image?.Save(Path.Combine(output, "styled-settings.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        vm.Plugins.SetEnabled(plugin, false); host.ApplyInterfaceLayers("fixture.styles", []); Pump(); Check(host.StyleLayers.Catalog()["activeLayers"]!.AsArray().Count == 0, "Reset releases all fixture layers.");
        host.Close(); Pump();
        Console.WriteLine("PASS supplied managed native package, theme, morph and cleanup.");
    }
    private static StyleSheet Sheet(string id, string type, string property, JsonNode value) => new() { Id = id, Name = id, Rules = [new() { Type = type, Setters = new() { [property] = value }, DurationMs = 0 }] };
    private static void Wait(Func<bool> ready) { var end = Environment.TickCount64 + 12000; while (!ready()) { if (Environment.TickCount64 > end) throw new TimeoutException(); Pump(1); } Pump(); }
    private static void Pump(int count = 15) { for (var n = 0; n < count; n++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(6); } }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
