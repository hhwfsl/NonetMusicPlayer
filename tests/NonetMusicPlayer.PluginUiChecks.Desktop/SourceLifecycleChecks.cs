using System.Diagnostics;
using System.IO.Compression;
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

internal static class SourceLifecycleChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new AppStorage(Path.Combine(root, "data")); using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0/NonetMusicPlayer.ExtensionFixture.dll"));
        var manifest = new PluginManifest { Id = "fixture.source-lifecycle", Name = "Music source", Version = "1.0.0", Type = "extension", Runtime = "managed", ContractVersion = 2, ExtensionClass = "SourceLifecycleExtension", PageEntry = "page.json", AutoStart = true, Permissions = ["in-process", "network", "navigation", "library-write"], ProvidedServices = ["media-source"], MusicSearch = new() { Label = "Fixture" }, RequiredCapabilities = ["runtime.startup.v1", "media.search.v1", "media.artwork-batch.v1"], EntryPoints = new() { [PluginPlatformPolicy.CurrentRid] = "fixture.dll" } };
        var package = Path.Combine(root, "source.impp"); using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", JsonSerializer.Serialize(manifest, AppStorage.Json));
            Write(zip, "page.json", """{"schemaVersion":2,"root":{"type":"text","bind":"account"}}""");
            Write(zip, "plugin_config_schema.json", """{"quality":{"type":"string","default":"standard"}}"""); zip.CreateEntryFromFile(fixture, "fixture.dll");
        }
        var plugin = vm.Plugins.Install(package); plugin.ManagedExecutionConsent = true; vm.Plugins.SetEnabled(plugin, true);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 }; window.Show();
        var data = Path.Combine(storage.PluginsFolder, "Storage", plugin.Id);
        Until(() => Directory.Exists(data) && Directory.GetFiles(data, "start-*").Length > 0);
        Require(vm.Page != "plugin:" + plugin.Id, "Source initializes without visiting page");
        var catalog = Wait(vm.Plugins.ReadMusicCatalogAsync(plugin, "fixture-catalog"));
        Require(catalog.Count == 1 && catalog[0].Artist == "Imported artist" && catalog[0].Album == "Imported album" && catalog[0].DurationSeconds == 180 && catalog[0].HasArtwork && File.Exists(catalog[0].CoverPath), "Catalog artwork and metadata persist before playback");
        var list = vm.CreatePlaylist("Imported"); vm.AddToPlaylist(list, catalog); var existing = vm.State.Tracks.Single(); existing.CoverPath = null;
        vm.AddToPlaylist(list, Wait(vm.Plugins.ReadMusicCatalogAsync(plugin, "fixture-catalog")));
        Require(vm.State.Tracks.Count == 1 && existing.CoverPath is not null, "Reimport enriches existing references without duplicates");
        for (var i = 0; i < 2; i++)
        {
            vm.Navigate("plugin:" + plugin.Id); Pump(); var prior = vm.Plugins.Extension(plugin); var invocation = prior.InvokeAsync("configure", new(), true);
            Until(() => window.HasPluginConfigurationOverlay);
            var form = window.GetVisualDescendants().OfType<PluginConfigView>().Single(); form.Draft["quality"] = i == 0 ? "high" : "standard";
            var watch = Stopwatch.StartNew(); form.Save(); watch.Stop();
            Require(watch.ElapsedMilliseconds < 1000, "Slow synchronous disposal cannot freeze configuration UI");
            Until(() => invocation.IsCompleted); try { invocation.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            var current = vm.Plugins.Extension(plugin); Wait(current.StartAsync());
            Require(plugin.Enabled && !ReferenceEquals(prior, current) && File.ReadAllText(Path.Combine(data, "fixture-session")) == "fixture-authorized", "Configuration reload preserves private session and plugin enabled state");
        }
        // 人为阻塞详情接口，验证歌单引用先出现、列表本地关闭不等待插件锁。
        using (var page = new ExtensionPageView(vm.Plugins, plugin, new() { Type = "scroll", Id = "ResultScroll", Children = [new() { Type = "stack", Children = [
            new() { Type = "button", Id = "CloseResults", Text = "Close", Action = "close", HideTargets = ["CloudResults"] },
            new() { Type = "selectable-text", Id = "CloudResults", Text = string.Join('\n', Enumerable.Range(0, 200).Select(i => "Cloud playlist " + i)), PreserveScroll = true }
        ] }] }))
        {
            var preview = new Window { Content = page, Width = 400, Height = 300 }; preview.Show(); Pump();
            var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new(0, 400); Pump(); var offset = scroll.Offset;
            var resultText = page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(); resultText.BringIntoView(); Pump();
            Require(scroll.Offset == offset, "Selecting readonly results cannot jump to top");
            var job = vm.Plugins.BeginCatalogImport(plugin, "background", "Background import");
            Until(() => File.Exists(Path.Combine(data, "metadata-started")));
            Require(job.Playlist.TrackIds.Count == 1 && job.ImportedCount == 1 && !job.Completion.IsCompleted, "All references imported before slow metadata completes");
            var imported = vm.State.Tracks.Single(t => t.ProviderTrackId == "remote-bg");
            Require(imported.CoverPath is null && imported.Title == "Pending metadata", "No artwork fetch blocks initial import");
            page.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseResults").RaiseEvent(new(Button.ClickEvent)); Pump();
            Require(!resultText.IsVisible && !job.Completion.IsCompleted, "Result dismissal independent of busy import RPC");
            File.WriteAllText(Path.Combine(data, "allow-metadata"), "continue"); Wait(job.Completion);
            Require(imported.Title == "Background enriched" && imported.DurationSeconds == 210 && imported.HasArtwork, "Background metadata updates existing row");
            preview.Close(); Pump();
        }
        vm.Navigate("plugins"); Pump(); var center = window.GetVisualDescendants().OfType<PluginsView>().Single();
        center.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PluginMore").RaiseEvent(new(Button.ClickEvent)); Pump();
        var menu = (ContextMenu)typeof(MainWindow).GetField("_activeMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        Require(menu.Items.OfType<MenuItem>().All(item => item.Header?.ToString() != L10n.T("Common.LoadCatalog")), "Search source has no redundant library-read action"); menu.Close();
        window.Close(); Pump();
        Console.WriteLine("PASS startup without page, imported covers/metadata before playback, live configuration/private session and nonblocking slow cleanup, source menu; background import before metadata, immediate local dismissal and scroll preservation.");
    }
    private static void Write(ZipArchive zip, string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
    private static T Wait<T>(Task<T> task) { Until(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> ready) { var limit = Environment.TickCount64 + 15000; while (!ready() && Environment.TickCount64 < limit) { Pump(); Thread.Sleep(5); } if (!ready()) throw new TimeoutException("Source lifecycle check timed out."); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : NonetMusicPlayer.Core.Audio.IAudioPlayer
    {
        public bool IsAvailable => false; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } } public Task LoadAsync(string source, CancellationToken token = default) => Task.CompletedTask; public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
