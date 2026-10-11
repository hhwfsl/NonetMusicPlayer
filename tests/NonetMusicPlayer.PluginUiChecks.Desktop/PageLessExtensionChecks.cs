using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>验证新增无页面能力及旧包默认行为；夹具不依赖私人桌宠源码或资产。</summary>
internal static class PageLessExtensionChecks
{
    public static void Run(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        var original = new PluginManifest
        {
            Id = "fixture.page-less", Name = "Optional page fixture", ContractVersion = 2, Type = "extension", Runtime = "declarative", PageEntry = "page.json",
            Permissions = ["ui-extend"], Contributions = [new("overlay", "Independent overlay", "show") { View = new() { Type = "text", Text = "Fixture" } }]
        };
        void Write(PluginManifest manifest) => File.WriteAllText(Path.Combine(source, "manifest.json"), JsonSerializer.Serialize(manifest, AppStorage.Json));
        original.Validate(); Check(original.HasPage, "Old manifest defaults to a page.");
        var oldJson = JsonSerializer.Serialize(original, AppStorage.Json).Replace("\"hasPage\":true,", "");
        Check(JsonSerializer.Deserialize<PluginManifest>(oldJson, AppStorage.Json)!.HasPage, "Missing optional field retains old behavior.");
        File.WriteAllText(Path.Combine(source, "page.json"), """{"schemaVersion":2,"root":{"type":"text","text":"Legacy page"}}""");
        Write(original); var oldPackage = PluginPackageBuilder.Pack(source, Path.Combine(root, "old.impp"));
        var replacement = JsonSerializer.Deserialize<PluginManifest>(JsonSerializer.Serialize(original, AppStorage.Json), AppStorage.Json)!;
        replacement.Version = "1.0.1"; replacement.HasPage = false; replacement.PageEntry = "";
        CheckThrows(() => replacement.Validate(), "Missing capability is rejected, not silently accepted.");
        replacement.RequiredCapabilities = ["ui.page-optional.v1"]; replacement.Validate();
        Write(replacement); var package = PluginPackageBuilder.Pack(source, Path.Combine(root, "new.impp"));
        using (var zip = ZipFile.OpenRead(package)) Check(!zip.Entries.Any(e => e.FullName == "page.json"), "Unused legacy page excluded from package.");
        Check(!PluginPackageInspector.Inspect(package).HasPage, "Page-less package validates.");
        replacement.PageEntry = "page.json"; CheckThrows(() => replacement.Validate(), "Page-less manifest cannot hide a page path."); replacement.PageEntry = "";
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root, "data"))), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 720 }; window.Show();
        var old = vm.Plugins.Install(oldPackage); vm.Plugins.SetEnabled(old, true); vm.ApplySettings(); Pump();
        var plugin = vm.Plugins.Install(package); vm.ApplySettings(); Pump();
        Check(plugin.Enabled && !plugin.HasPage, "Same-ID update preserves enabled state.");
        var overlays = (Dictionary<string, Window>)typeof(MainWindow).GetField("_extensionOverlays", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Check(overlays.TryGetValue(plugin.Id, out var overlay) && overlay.IsVisible, "No-page extension still opens contribution overlay.");
        Check(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Tag?.ToString() == "plugin:" + plugin.Id), "No phantom navigation button.");
        var before = vm.Page; vm.Navigate("plugin:" + plugin.Id); Check(vm.Page == before, "Direct navigation does not create a blank page.");
        vm.DisablePlugin(plugin); Pump(); Check(!overlays.ContainsKey(plugin.Id), "Disable releases page-less overlay.");
        // 卸载保留包后重新导入亦不得尝试读取不存在的页面。
        vm.Plugins.Uninstall(plugin, false);
        var reconnect = vm.Plugins.Install(package); vm.Plugins.SetEnabled(reconnect, true); vm.ApplySettings(); Pump();
        Check(overlays.ContainsKey(plugin.Id), "Retained package reconnect works without a page.");
        vm.DisablePlugin(reconnect); window.Close(); Pump();
        Console.WriteLine("PASS optional page packing/install/update/reconnect/navigation/overlay; legacy manifest default.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void CheckThrows(Action action, string message) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException(message); }
    private static void Pump() { for (var i = 0; i < 30; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }
    private sealed class FakeAudio : NonetMusicPlayer.Core.Audio.IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } } public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
