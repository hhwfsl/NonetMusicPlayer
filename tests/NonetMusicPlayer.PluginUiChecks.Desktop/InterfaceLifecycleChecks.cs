using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>以真实托管夹具验证启动门控、插件页面重排及统一菜单，不依赖私人插件。</summary>
internal static class InterfaceLifecycleChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(root)), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var manifest = new PluginManifest { Id = "fixture.startup-style", Name = "Startup fixture", Version = "1.0.0", Type = "extension", Runtime = "managed", ContractVersion = 2, AutoStart = true, ExtensionClass = "StartupStyleExtension", PageEntry = "page.json", Permissions = ["in-process", "ui-extend"], RequiredCapabilities = ["runtime.startup.v1", "ui.styles.v1"], EntryPoints = new() { [PluginPlatformPolicy.CurrentRid] = "fixture.dll" } };
        var package = Path.Combine(root, "fixture.impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, AppStorage.Json));
            using (var writer = new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("""{"schemaVersion":2,"root":{"type":"stack","children":[{"type":"image","bind":"image","height":50},{"type":"text","bind":"count"},{"type":"button","text":"Increment","action":"increment"}]}}""");
            zip.CreateEntryFromFile(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0/NonetMusicPlayer.ExtensionFixture.dll")), "fixture.dll");
        }
        var plugin = vm.Plugins.Install(package); plugin.ManagedExecutionConsent = true; vm.Plugins.SetEnabled(plugin, true);
        var window = new MainWindow { DataContext = vm };
        var startup = vm.Plugins.PrepareAutomaticExtensionsAsync();
        Check(!window.IsVisible, "The initial appearance is prepared before the window becomes visible.");
        Wait(startup);
        Check(!window.IsVisible && ((ISolidColorBrush)Application.Current!.Resources["AccentBrush"]!).Color == Color.Parse("#4268CD"), "Startup style requests complete while the window is still hidden.");
        window.PrepareInitialAppearance();
        Check(!window.IsVisible && window.FindControl<Button>("PlayerPlay")!.CornerRadius.TopLeft == 13, "Offscreen template/layout/style preparation completes before showing the native window.");
        window.Show(); Pump(); vm.Navigate("plugin:" + plugin.Id); Pump();
        var page = window.GetVisualDescendants().OfType<ExtensionPageView>().Single();
        var session = vm.Plugins.Extension(plugin);
        var alternate = window.FindControl<ContentControl>("AlternatePage")!;
        // 和完整 Shell 重排相同的同步移除/挂接；图片释放后的绘制会在这里复现旧崩溃。
        for (var i = 0; i < 4; i++)
        {
            alternate.Content = null; alternate.Content = page; Pump();
            window.WindowState = i % 2 == 0 ? WindowState.Maximized : WindowState.Minimized; Pump(); window.WindowState = WindowState.Normal;
            Wait(session.InvokeAsync("increment", new(), true));
            using var frame = window.CaptureRenderedFrame();
            Check(plugin.Enabled && !session.IsDisposed && session.Frame.State["count"]!.GetValue<int>() == i + 1 && page.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == (i + 1).ToString()), "Reattached pages still update and render after maximize/minimize.");
        }
        vm.Navigate("plugins"); Pump();
        var center = window.GetVisualDescendants().OfType<PluginsView>().Single();
        Check(!center.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PluginOpenFolder"), "Open folder is no longer a standalone plugin-card button.");
        center.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PluginMore").RaiseEvent(new(Button.ClickEvent)); Pump();
        var menuField = typeof(MainWindow).GetField("_activeMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var more = (ContextMenu)menuField.GetValue(window)!;
        Check(more.Items.OfType<MenuItem>().Any(m => m.Name == "PluginOpenFolder") && more.Items.OfType<MenuItem>().All(m => m.Header?.ToString() != L10n.T("Common.Configure")), "More contains folder access without a duplicate configuration action.");
        more.Close();
        center.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PluginOpenPage").RaiseEvent(new(Button.ClickEvent)); Pump();
        Check(vm.Page == "plugin:" + plugin.Id, "The plugin card opens its actual page.");
        window.FindControl<Button>("PlayerMore")!.RaiseEvent(new(Button.ClickEvent)); Pump();
        var playerMenu = (ContextMenu)menuField.GetValue(window)!;
        Check(playerMenu.Items.OfType<Separator>().Any() && playerMenu.Items.OfType<MenuItem>().Any(m => m.Name == "PlayerImportLyrics"), "The player menu contains a separate lyrics group."); playerMenu.Close();
        vm.Navigate("lyrics"); Pump(); Check(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "LyricsMore"), "The lyric page has no separate More button.");
        window.Close(); Pump();
        Console.WriteLine("PASS hidden startup restoration, declarative page reparent/minimize/render, plugin card navigation and lyric menu relocation.");
    }
    private static void Pump() { for (var n = 0; n < 20; n++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }
    private static void Wait(Task task) { var end = Environment.TickCount64 + 15000; while (!task.IsCompleted && Environment.TickCount64 < end) Pump(); if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); Pump(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class FakeAudio : NonetMusicPlayer.Core.Audio.IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
