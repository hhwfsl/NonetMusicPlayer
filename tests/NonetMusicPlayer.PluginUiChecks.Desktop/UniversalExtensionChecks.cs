using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class UniversalExtensionChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var gain = new ExtensionPcmLease(new GainProcessor(false));
        var bad = new ExtensionPcmLease(new GainProcessor(true));
        var pipe = new NonetMusicPlayer.Core.Audio.ExtensionAudioPipeline(); pipe.SetProcessors([gain, bad]);
        var samples = new float[] { .2f, -.4f }; pipe.Process(samples, 2);
        Check(Math.Abs(samples[0] - .1f) < .00001 && Math.Abs(samples[1] + .2f) < .00001 && !bad.Enabled, "PCM effects run on small buffers; faulty stage restores input and bypasses.");
        gain.ReleaseAsync().GetAwaiter().GetResult(); bad.ReleaseAsync().GetAwaiter().GetResult();
        var store = new ExtensionStorageService(Path.Combine(root, "values"));
        store.Execute(new() { ["operation"] = "put", ["key"] = "example", ["value"] = "中文" });
        Check(new ExtensionStorageService(Path.Combine(root, "values")).Execute(new() { ["key"] = "example" })["value"]!.GetValue<string>() == "中文", "Store persists Unicode.");
        Reject(() => store.Execute(new() { ["key"] = "../outside" }));
        Reject(() => store.Execute(new() { ["operation"] = "put", ["key"] = "large", ["value"] = new string('x', 2_000_001) }));
        Check(UniversalExtensionContract.MatchesEvent(["player.*"], "player.position"), "Versioned wildcard events.");
        using var audio = new FakeAudio(); var storage = new AppStorage(Path.Combine(root, "data"));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        for (var n = 0; n < 205; n++) vm.State.Tracks.Add(new("fixture-" + n, "歌曲 " + n, "作者", "专辑", "missing.wav", ".wav", 0));
        vm.ApplyFilter(); var window = new MainWindow { DataContext = vm }; window.Show(); Pump();
        var manifest = new PluginManifest { Id = "fixture.universal", Name = "Universal", Version = "1.0.0", Type = "extension", ContractVersion = 2, Runtime = "managed", ExtensionClass = nameof(UniversalFixture), PageEntry = "page.json",
            EntryPoints = new() { [PluginPlatformPolicy.CurrentRid] = "fixture.dll" }, Permissions = ["in-process", "native-ui", "ui-extend", "workflow", "music-read", "navigation", "player-control", "library-write", "network", "lyrics-write", "plugin-services"],
            Configuration = "{\"autoFetch\":false}", ProvidedServices = ["media-source", "lyrics-source"], RequiredCapabilities = ["ui.native.v1", "workflow.v1", "host.query.v1"], Hooks = ["navigation.before", "playback.before", "command.before"],
            Contributions = [new("page.music", "Native", "open-page") { Native = true }, new("shell.player", "Player", "open-page") { View = new() { Type = "text", Id = "UniversalPlayer", Bind = "host.title" } }] };
        var package = Path.Combine(root, "universal.impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", JsonSerializer.Serialize(manifest, AppStorage.Json)); Write(zip, "page.json", "{\"schemaVersion\":2,\"root\":{\"type\":\"text\",\"text\":\"Universal\"}}");
            zip.CreateEntryFromFile(typeof(UniversalFixture).Assembly.Location, "fixture.dll");
        }
        var installed = vm.Plugins.Install(package); installed.ManagedExecutionConsent = true; vm.Plugins.SetEnabled(installed, true);
        var session = vm.Plugins.Extension(installed); Wait(session.StartAsync());
        Wait(session.InvokeAsync("query", new())); Check(session.Frame.State["result"]!["total"]!.GetValue<int>() == 205 && session.Frame.State["result"]!["data"]!.AsArray().Count == 5, "Paged host query is actual library data.");
        Wait(session.InvokeAsync("host", new())); Check(session.Frame.State["result"]!["slots"]!.AsArray().Any(n => n!.GetValue<string>() == "shell.workspace"), "Discover real surface catalog.");
        Wait(session.InvokeAsync("store", new())); Check(session.Frame.State["result"]!["success"]!.GetValue<bool>(), "Runtime storage service.");
        Wait(session.InvokeAsync("lyrics", new())); Check(session.Frame.State["result"]!["lines"]!.AsArray()[0]!["words"]!.AsArray().Count == 2, "Enhanced lyrics serialize absent word end times without NaN.");
        var catalog = Wait(vm.Plugins.LoadCatalogAsync(installed, vm.Lyrics)); Check(catalog.Count == 1 && catalog[0].ProviderId == installed.Id, "Composite extension supplies a validated media catalog.");
        var match = Wait(vm.Plugins.MatchLyricsAsync(installed, new("fixture", "artist", "", 120))); Check(match.Text.Contains("matched"), "Composite extension supplies matching lyrics through existing validation.");
        vm.Navigate("library"); Pump();
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Native universal view"), "Trusted native view renders through shared Avalonia identity.");
        Check(window.FindControl<Border>("PlayerSurface")!.Opacity == 0, "Player surface is replaceable.");
        vm.Plugins.Configure(installed, "{\"autoFetch\":false}"); vm.ApplySettings(); vm.Navigate("library"); Pump();
        Check(window.FindControl<Border>("PlayerSurface")!.Opacity == 0, "Configuration restart restores surfaces without recording a false failure.");
        vm.Navigate("artists"); Pump(); Check(vm.Page == "library", "Navigation hook cancels UI flow.");
        vm.Navigate("settings"); Pump(); Check(vm.Page == "settings" && window.FindControl<Border>("PlayerSurface")!.Opacity == 1, "Recovery page bypasses hooks and restores built-in shell.");
        Wait(vm.PlayTrackAsync(vm.State.Tracks[0], vm.State.Tracks.Take(2))); Check(vm.CurrentTrack?.Id == "fixture-1", "Playback selection hook changes only existing queue IDs.");
        var result = Wait(window.Commands.ExecuteAsync("player pause")); Check(!result.Success && vm.IsPlaying, "Command hook can cancel without modifying confirmation.");
        vm.DisablePlugin(installed); Pump(); result = Wait(window.Commands.ExecuteAsync("player pause")); Check(result.Success && !vm.IsPlaying, "Disable removes workflow and view immediately.");
        vm.Plugins.SetEnabled(installed, true); vm.Navigate("library"); Pump(); Check(window.FindControl<Border>("PlayerSurface")!.Opacity == 0, "Re-enabling restores contributions rather than preserving disable failure markers.");
        vm.DisablePlugin(installed); Pump();
        Reject(() => UniversalPluginCommandPolicy.ValidateFor(new("bad", "plugins.config", ["other"]), ["plugins-control"]));
        Reject(() => AgentCommandPolicy.ValidateAndEncode(new("bad", "plugins.uninstall", ["other"])));
        window.Close(); Pump(); Console.WriteLine("PASS universal host catalog/query/storage, native and data surfaces, UI recovery, navigation/playback/command hooks, disable, old Agent permissions and bounded storage.");
    }
    private static void Write(ZipArchive zip, string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
    private static void Pump() { for (var i = 0; i < 40; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); } }
    private static void Wait(Task task) { var deadline = Environment.TickCount64 + 15000; while (!task.IsCompleted) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Extension fixture exceeded budget."); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } task.GetAwaiter().GetResult(); }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Unsafe request accepted."); }
    private sealed class GainProcessor(bool invalid) : INonetPcmProcessor
    {
        public void Process(Span<float> samples, ExtensionPcmFormat format) { for (var i = 0; i < samples.Length; i++) samples[i] = invalid ? float.NaN : samples[i] * .5f; }
        public void Dispose() { }
    }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(120); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() => IsPlaying = false; public void Dispose() { }
    }
}

/// <summary>独立托管测试夹具，不引用或包含任何私人插件，亦不作为发行插件。</summary>
public sealed class UniversalFixture : INonetExtension, INonetWorkflowExtension, INonetNativeViewExtension
{
    private readonly ExtensionFrame _frame = new();
    public ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization initialization, CancellationToken cancellationToken) => SyncAsync(cancellationToken);
    public ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.Action == "service:media-source")
        {
            _frame.Requests.Clear(); _frame.State["serviceResult"] = new JsonObject { ["tracks"] = new JsonArray(new JsonObject { ["id"] = "remote", ["title"] = "Remote fixture", ["artist"] = "Artist", ["album"] = "Album", ["durationSeconds"] = 120, ["format"] = "wav" }), ["nextCursor"] = null }; return SyncAsync(cancellationToken);
        }
        if (invocation.Action == "service:lyrics-source")
        {
            _frame.Requests.Clear(); _frame.State["serviceResult"] = new JsonObject { ["format"] = "lrc", ["text"] = "[00:00.000]matched", ["source"] = "fixture" }; return SyncAsync(cancellationToken);
        }
        var service = invocation.Action == "store" ? "storage" : invocation.Action;
        var args = invocation.Action == "query" ? new JsonObject { ["operation"] = "music.list", ["offset"] = 100, ["limit"] = 5 } : invocation.Action == "store" ? new JsonObject { ["operation"] = "put", ["key"] = "persisted", ["value"] = 1 } : invocation.Action == "lyrics" ? new JsonObject { ["operation"] = "parse", ["trackId"] = "fixture-0", ["text"] = "[00:00.000]<00:00.000>first <00:00.300>second" } : new JsonObject();
        _frame.Requests = [new(Guid.NewGuid().ToString("N"), service, args)]; return SyncAsync(cancellationToken);
    }
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_frame);
    public ValueTask<ExtensionFrame> CompleteAsync(string requestId, JsonObject result, CancellationToken cancellationToken) { _frame.Requests.Clear(); _frame.State["result"] = result.DeepClone(); return SyncAsync(cancellationToken); }
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken cancellationToken) => SyncAsync(cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public ValueTask<ExtensionHookDecision> EvaluateAsync(ExtensionHook hook, CancellationToken cancellationToken)
        => ValueTask.FromResult(new ExtensionHookDecision { Cancel = hook.Name == "navigation.before" && hook.Data["page"]?.GetValue<string>() == "artists" || hook.Name == "command.before" && hook.Data["operation"]?.GetValue<string>() == "player.pause", Data = hook.Name == "playback.before" ? new() { ["trackId"] = "fixture-1" } : new() });
    public INonetNativeView CreateView(ExtensionViewContext context) => new NativeFixtureView();
    private sealed class NativeFixtureView : INonetNativeView
    {
        public object View { get; } = new TextBlock { Text = "Native universal view" };
        public void Update(ExtensionFrame frame) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
