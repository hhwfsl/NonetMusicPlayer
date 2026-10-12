using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>验证普通异常只停用归属插件，以及网易云分轨/YRC 兼容。</summary>
internal static class RuntimeFaultChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        AppLog.Initialize(root);
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(root)), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump();
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0/NonetMusicPlayer.ExtensionFixture.dll"));
        foreach (var action in new[] { "throw", "background", "event", "initialize" })
        {
            var manifest = new PluginManifest { Id = "fixture.fault-" + action, Name = "Fault " + action, Type = "extension", Runtime = "managed", ContractVersion = 2,
                ExtensionClass = "FaultExtension", Permissions = ["in-process"], PageEntry = "page.json", Events = ["fixture.failure"], EntryPoints = new() { [PluginPlatformPolicy.CurrentRid] = "fixture.dll" } };
            var package = Path.Combine(root, action + ".impp"); Package(package, manifest, fixture);
            var plugin = vm.Plugins.Install(package); plugin.ManagedExecutionConsent = true;
            if (action == "initialize") plugin.Configuration = "{\"failInit\":true}";
            vm.Plugins.SetEnabled(plugin, true); var session = vm.Plugins.Extension(plugin);
            try
            {
                Wait(session.StartAsync());
                if (action == "throw") { try { Wait(session.InvokeAsync("handled", new())); } catch (PluginOperationException) { } Check(plugin.Enabled, "Handled business rejection must not disable."); }
                Wait(action == "event" ? session.EventAsync(new("fixture.failure", new())) : session.InvokeAsync(action == "initialize" ? "throw" : action, new()));
            }
            catch (InvalidOperationException) { }
            var deadline = DateTime.UtcNow.AddSeconds(3); while (plugin.Enabled && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
            Check(!plugin.Enabled && session.IsDisposed, "Fault disables plugin: " + action);
            Check(!new PluginManager(new AppStorage(root)).Installed.Single(p => p.Id == plugin.Id).Enabled, "Fault state persisted.");
        }
        var legacyManifest = new PluginManifest { Id = "fixture.rpc-fault", Name = "Legacy RPC", Type = "provider", ContractVersion = 1, Runtime = "managed", ExtensionClass = "ManagedRpcFixture",
            Permissions = ["in-process", "network"], EntryPoints = new() { [PluginPlatformPolicy.CurrentRid] = "fixture.dll" } };
        var legacyPackage = Path.Combine(root, "legacy.impp"); Package(legacyPackage, legacyManifest, fixture);
        var legacy = vm.Plugins.Install(legacyPackage); legacy.ManagedExecutionConsent = true; vm.Plugins.SetEnabled(legacy, true);
        try { Wait(vm.Plugins.LoadCatalogAsync(legacy, vm.Lyrics)); } catch (InvalidOperationException) { }
        Check(!legacy.Enabled, "Contract v1 managed RPC faults disabled.");
        var lyrics = LyricsService.Parse("[nonet:original]\n[1000,1000](1000,500,0)原(1500,500,0)文\n[3000,1000](3000,1000,0)第二句\n[nonet:translation]\n[00:01.080]Translation\n[00:03.050]Second line");
        Check(lyrics.Count == 2 && lyrics[0].Text == "原文" && lyrics[0].Translation == "Translation" && lyrics[0].Words.Count == 2, "YRC separates translation with offset timestamps.");
        var imported = LyricsService.Parse("[00:01.00]原文\n[00:03.00]第二句\n[00:01.08]Translation\n[00:03.05]Second line");
        Check(imported.Count == 2 && imported[1].Translation == "Second line", "NetEase two-block LRC aligned.");
        Check(LyricsService.Parse("[00:01.00]原文\n[00:01.00]Translation").Single().Translation == "Translation", "Old bilingual LRC retained.");
        AppLog.Flush(); AppLog.Shutdown(); Check(Directory.GetFiles(Path.Combine(root, "Logs")).Any(p => File.ReadAllText(p).Contains("Fault throw (fixture.fault-throw)")), "Logs contain plugin name and ID.");
        window.Close(); Pump(); Console.WriteLine("PASS managed action/event/initialization/background fault disable, business rejection, persisted state, named logs, YRC and bilingual LRC.");
    }
    private static void Package(string path, PluginManifest manifest, string fixture)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, AppStorage.Json));
        using (var writer = new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("{\"schemaVersion\":2,\"root\":{\"type\":\"stack\"}}");
        zip.CreateEntryFromFile(fixture, "fixture.dll");
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Wait(Task task) { var deadline = DateTime.UtcNow.AddSeconds(10); while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class FakeAudio : NonetMusicPlayer.Core.Audio.IAudioPlayer
    {
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(100);
        public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
    }
}
