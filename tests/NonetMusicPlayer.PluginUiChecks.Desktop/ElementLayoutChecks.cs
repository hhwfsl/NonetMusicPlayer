using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>使用最小数据插件测试布局扩展，不依赖任何私人插件或用户音乐库。</summary>
internal static class ElementLayoutChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var motionEnvironment = Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION");
        Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
        Check(!InterfaceMotion.IsEnabled, "Reduced-motion override disables effects.");
        Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", motionEnvironment);
        var storage = new AppStorage(Path.Combine(root, "data"));
        var oldPath = Path.Combine(storage.Root, "Layouts", "active.layout.json"); Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        const string ignored = "invalid obsolete user layout"; File.WriteAllText(oldPath, ignored);
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760 }; window.Show(); Pump();
        Check(window.LayoutConfiguration.AppliedJson == UiLayoutService.DefaultJson && File.ReadAllText(oldPath) == ignored, "Legacy user layout ignored and source preserved.");
        vm.Navigate("settings"); Pump();
        var navigation = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "NavigationPosition");
        var player = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PlayerPosition");
        navigation.SelectedIndex = 1; player.SelectedIndex = 1; Pump();
        Check(vm.Settings.NavigationRight && vm.Settings.PlayerTop, "Navigation/player dropdowns apply.");
        Check(!window.GetVisualDescendants().OfType<Button>().Any(b => (b.Content?.ToString() ?? "").Contains("JSON")), "No JSON layout buttons.");
        var controls = window.ElementLayouts.Catalog(0, 200);
        Check(controls["elements"]!.AsArray().Count > 20 && controls["total"]!.GetValue<int>() > 20, "Paged catalog includes all control types.");
        var grid = window.GetVisualDescendants().OfType<Grid>().Single(c => c.Name == "TitleBarLayout");
        var previousColumns = grid.ColumnDefinitions;
        var play = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "PlayerPlay");
        var originalWidth = play.Width;
        var subject = new MutableValue(originalWidth);
        using var binding = play.Bind(Control.WidthProperty, subject);
        window.ElementLayouts.Apply("layout-test", ElementLayoutContract.Update(
            new("PlayerPlay", new() { ["width"] = 60d, ["margin"] = "2,3,2,3" }),
            new("TitleBarLayout", new() { ["columns"] = "Auto,*,Auto" })) ["patches"]!.AsArray());
        Check(play.Width == 60 && play.Margin.Top == 3 && !ReferenceEquals(previousColumns, grid.ColumnDefinitions), "Button layout and grid tracks patched.");
        subject.Set(52); Check(play.Width == 60, "Live underlying binding is not replaced.");
        Reject(() => window.ElementLayouts.Apply("layout-test", new(new JsonObject { ["target"] = "PlayerPlay", ["properties"] = new JsonObject { ["width"] = -5 } })));
        Check(play.Width == 60, "Invalid batch keeps previous layout.");
        Reject(() => window.ElementLayouts.Apply("other", new(new JsonObject { ["target"] = "PlayerPlay", ["properties"] = new JsonObject { ["width"] = 80 } })));
        Check(play.Width == 60, "Conflicting plugin cannot take ownership.");
        window.ElementLayouts.Remove("layout-test");
        Check(play.Width == 52 && ReferenceEquals(previousColumns, grid.ColumnDefinitions), "Removal restores live binding and original grid tracks.");
        binding.Dispose(); play.Width = originalWidth;
        var target = window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "PlayerMode");
        var original = (Panel)target.Parent!; var index = original.Children.IndexOf(target);
        window.ElementLayouts.Apply("move-test", new(new JsonObject { ["target"] = "PlayerMode", ["parent"] = "TitleBrand" }));
        Check(target.Parent is StackPanel { Name: "TitleBrand" }, "Existing controls can be moved without cloning handlers.");
        window.ElementLayouts.Remove("move-test"); Check(target.Parent == original && original.Children.IndexOf(target) == index, "Parent/order restored.");
        // 两个单独合法的移动不能组合成父子循环；异常必须回滚整个批次。
        Reject(() => window.ElementLayouts.Apply("cycle-test", new(
            new JsonObject { ["target"] = "TitleBrand", ["parent"] = "PlayerColumns" },
            new JsonObject { ["target"] = "PlayerColumns", ["parent"] = "TitleBrand" })));
        var source = Path.Combine(root, "plugin"); Directory.CreateDirectory(source);
        var manifest = new PluginManifest { Id = "fixture.element-layout", Name = "Element layout", Version = "1.0.0", Type = "extension", Runtime = "declarative", ContractVersion = 2,
            HasPage = false, PageEntry = "", Permissions = ["ui-extend"], RequiredCapabilities = ["ui.elements.v1", "ui.page-optional.v1"],
            Contributions = [new("ui.element", "Inserted label", "show") { Target = "TitleBrand", Placement = "append", View = new() { Type = "text", Id = "InsertedFixture", Text = "Extension" } }] };
        File.WriteAllText(Path.Combine(source, "manifest.json"), JsonSerializer.Serialize(manifest, AppStorage.Json));
        var package = PluginPackageBuilder.Pack(source, Path.Combine(root, "fixture.impp"));
        var installed = vm.Plugins.Install(package); vm.Plugins.SetEnabled(installed, true); vm.ApplySettings(); Pump();
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Extension"), "Declarative custom control appears at named anchor.");
        var catalog = window.ElementLayouts.Catalog(0, 200)["elements"]!.AsArray();
        var anchorId = catalog.OfType<JsonObject>().Single(c => c["name"]?.GetValue<string>() == "TitleBrand")["id"]!.GetValue<string>();
        window.MountPluginElement(installed, new() { ["key"] = "runtime-label", ["target"] = anchorId, ["view"] = new JsonObject { ["type"] = "text", ["text"] = "Dynamic control" } });
        Pump(); Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Dynamic control"), "Runtime ID supports custom controls.");
        window.UnmountPluginElement(installed.Id, "runtime-label"); Pump();
        Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Dynamic control"), "Dynamic controls unmount without disabling plugin.");
        var layoutView = window.GetVisualDescendants().OfType<SettingsView>().Single();
        layoutView.FocusLayoutConfiguration(); Pump();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(root, "layout-dropdowns.png"), PngBitmapEncoderOptions.Default);
        window.ElementLayouts.Apply(installed.Id, new(new JsonObject { ["target"] = "PlayerPlay", ["properties"] = new JsonObject { ["width"] = 60d } }));
        vm.DisablePlugin(installed); Pump();
        Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Extension") && play.Width == originalWidth, "Disable removes custom control and layout overrides.");
        Check(File.ReadAllText(oldPath) == ignored, "Plugin layout does not write obsolete user JSON.");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(root, "settings-dropdowns.png"), PngBitmapEncoderOptions.Default);
        vm.Navigate("library"); Pump(); using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(root, "home.png"), PngBitmapEncoderOptions.Default);
        window.Close(); Pump();
        Console.WriteLine("PASS ignored legacy JSON, dropdowns, paged catalog, property/track/reparent leases, binding restoration, invalid/conflicting rollback, control mount/disable, screenshots.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Invalid layout accepted."); }
    private static void Pump() { for (var i = 0; i < 32; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }
    private sealed class MutableValue(double initial) : IObservable<object>
    {
        private IObserver<object>? _observer;
        public IDisposable Subscribe(IObserver<object> observer) { _observer = observer; observer.OnNext(initial); return new ElementLayoutService.ActionLease(() => _observer = null); }
        public void Set(double value) => _observer?.OnNext(value);
    }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
