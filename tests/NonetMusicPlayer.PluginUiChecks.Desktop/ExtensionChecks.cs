using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class ExtensionChecks
{
    // 首次基线的 JSON 不从当前 SDK 生成，保留其默认字段行为。
    private const string Page = """{"schemaVersion":2,"root":{"type":"grid","rows":"Auto,Auto,Auto","children":[{"type":"selectable-text","id":"FixtureValue","bind":"count"},{"type":"input","row":1,"input":"draft","multiline":true,"enterAction":"increment"},{"type":"button","row":2,"text":"Increment","action":"increment"}]}}""";
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var fixture = Path.Combine(repo, "tests/NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0");
        var frozen = Path.Combine(repo, "artifacts/frozen-extension-v2");
        // 首次构建后冻结字节；以后宿主升级不重新编译/覆盖此基线程序集。
        if (!Directory.Exists(frozen))
        {
            Directory.CreateDirectory(frozen);
            foreach (var file in Directory.GetFiles(fixture).Where(f => !f.EndsWith(".pdb"))) File.Copy(file, Path.Combine(frozen, Path.GetFileName(file)));
        }
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var audio = new FakeAudio(); var storage = new AppStorage(root);
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump();
        foreach (var managed in new[] { false, true })
        {
            var id = managed ? "baseline.managed" : "baseline.process";
            var manifest = new PluginManifest { Id = id, Name = id, Type = "extension", ContractVersion = 2, PageEntry = "page.json",
                Runtime = managed ? "managed" : "process", ExtensionClass = "BaselineExtension",
                Permissions = [managed ? "in-process" : "process", "navigation", "ui-extend"],
                Events = ["player.track-changed"], RequiredCapabilities = ["ui.tree.v2", "state.v1"],
                EntryPoints = new() { ["win-x64"] = "bin/" + (managed ? "NonetMusicPlayer.ExtensionFixture.dll" : "NonetMusicPlayer.ExtensionFixture.exe") } };
            var package = Path.Combine(root, id + ".impp");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                Write(zip, "manifest.json", JsonSerializer.Serialize(manifest, AppStorage.Json)); Write(zip, "page.json", Page);
                foreach (var file in Directory.GetFiles(frozen)) zip.CreateEntryFromFile(file, "bin/" + Path.GetFileName(file));
            }
            var installed = vm.Plugins.Install(package);
            if (managed)
            {
                var denied = false; try { vm.Plugins.SetEnabled(installed, true); } catch (InvalidOperationException) { denied = true; }
                Check(denied && !installed.ManagedExecutionConsent, "Managed mode cannot bypass host consent.");
                installed.ManagedExecutionConsent = true;
            }
            vm.Plugins.SetEnabled(installed, true);
            var session = vm.Plugins.Extension(installed); Wait(session.StartAsync());
            Check(session.Frame.State["count"]!.GetValue<int>() == 0, "Frozen v2 worker initializes.");
            vm.Navigate("plugin:" + id); Pump();
            var view = window.GetVisualDescendants().OfType<ExtensionPageView>().Single();
            Check(view.GetVisualDescendants().OfType<SelectableTextBlock>().Any(), "Generic selectable text renderer.");
            var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is TextBlock { Text: "Increment" });
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Wait(session.InvokeAsync("increment", new()));
            Check(session.Frame.State["count"]!.GetValue<int>() >= 1, "Plugin-defined action changes state.");
            var input = view.GetVisualDescendants().OfType<TextBox>().Single(); input.Text = "draft"; input.Focus(); Pump();
            var countBeforeKey = session.Frame.State["count"]!.GetValue<int>();
            window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null); Pump();
            Check(session.Frame.State["count"]!.GetValue<int>() == countBeforeKey && input.Text!.Contains('\n'), "Shift+Enter inserts a newline without invoking.");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var keyDeadline = DateTime.UtcNow.AddSeconds(5);
            while (session.Frame.State["count"]!.GetValue<int>() == countBeforeKey && DateTime.UtcNow < keyDeadline) { Pump(); Thread.Sleep(5); }
            Check(session.Frame.State["count"]!.GetValue<int>() > countBeforeKey, "Enter invokes the declared action.");
            vm.Navigate("library"); Pump(); vm.Navigate("plugin:" + id); Pump();
            Check(ReferenceEquals(session, vm.Plugins.Extension(installed)), "Navigation keeps plugin state/session.");
            Wait(session.EventAsync(new("player.track-changed", new())));
            Check(session.Frame.State["lastEvent"]!.GetValue<string>() == "player.track-changed", "Generic event reaches worker.");
            vm.Plugins.SetEnabled(installed, false); Pump();
            Check(!installed.Enabled, "Disable releases session and page.");
        }
        // 新增能力单独使用当前夹具；上面的冻结包保持旧二进制字节不变。
        const string dynamicPage = """{"schemaVersion":2,"root":{"type":"grid","id":"DynamicFixture","columns":"180,*","columnsBind":"columns","children":[{"type":"repeat","bind":"items","template":{"type":"button","text":"Item","contextActions":[{"type":"button","text":"Item action","action":"increment","parameter":"item.id"}]}},{"type":"button","id":"ExpandedContent","column":1,"text":"Collapse","action":"collapse","openMenuOnClick":true,"contextActions":[{"type":"button","text":"Expand","action":"expand"}]}]}}""";
        var dynamicPackage = Path.Combine(root, "dynamic.impp");
        var dynamicManifest = new PluginManifest { Id = "fixture.dynamic", Name = "Dynamic", Type = "extension", ContractVersion = 2, PageEntry = "page.json",
            Permissions = ["process","navigation","ui-extend"], RequiredCapabilities = ["ui.layout-bind.v1","ui.context-menu.v1"],
            EntryPoints = new() { ["win-x64"] = "bin/NonetMusicPlayer.ExtensionFixture.exe" } };
        using (var zip = ZipFile.Open(dynamicPackage, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", JsonSerializer.Serialize(dynamicManifest, AppStorage.Json)); Write(zip, "page.json", dynamicPage);
            foreach (var file in Directory.GetFiles(fixture).Where(f => !f.EndsWith(".pdb"))) zip.CreateEntryFromFile(file, "bin/" + Path.GetFileName(file));
        }
        var dynamicPlugin = vm.Plugins.Install(dynamicPackage); vm.Plugins.SetEnabled(dynamicPlugin, true);
        var dynamicSession = vm.Plugins.Extension(dynamicPlugin); Wait(dynamicSession.StartAsync()); vm.Navigate("plugin:" + dynamicPlugin.Id); Pump();
        var dynamicView = window.GetVisualDescendants().OfType<ExtensionPageView>().Single();
        var dynamicGrid = dynamicView.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "DynamicFixture");
        var expandingContent = dynamicView.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExpandedContent");
        window.UpdateLayout(); var beforeWidth = expandingContent.Bounds.Width;
        Wait(dynamicSession.InvokeAsync("collapse", new())); window.UpdateLayout(); Pump();
        Check(dynamicGrid.ColumnDefinitions[0].Width.Value == 0 && expandingContent.Bounds.Width > beforeWidth + 100, "State-bound columns release space instead of leaving a hidden sidebar gap");
        Wait(dynamicSession.InvokeAsync("expand", new())); window.UpdateLayout(); Pump();
        Check(dynamicGrid.ColumnDefinitions[0].Width.Value == 180, "State-bound columns restore original size");
        expandingContent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(expandingContent.ContextMenu!.IsOpen, "Left click opens the optional menu without invoking the button action");
        expandingContent.ContextMenu.Close(); Pump();
        var candidates = dynamicView.GetVisualDescendants().OfType<Button>().ToArray();
        if (!candidates.Any(b => b.Content is TextBlock { Text: "Item" })) throw new InvalidOperationException("Item missing. State=" + dynamicSession.Frame.State + " Buttons=" + string.Join("|", candidates.Select(b => b.Content?.GetType().Name + ":" + (b.Content as TextBlock)?.Text)));
        var itemButton = candidates.Single(b => b.Content is TextBlock { Text: "Item" });
        var menuAction = itemButton.ContextMenu!.Items.OfType<MenuItem>().Single();
        menuAction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var contextDeadline = DateTime.UtcNow.AddSeconds(5);
        while (dynamicSession.Frame.State["parameter"]?.GetValue<string>() != "first" && DateTime.UtcNow < contextDeadline) { Pump(); Thread.Sleep(5); }
        Check(dynamicSession.Frame.State["parameter"]?.GetValue<string>() == "first", "Context action forwards the selected item parameter");
        var noAuthor = JsonSerializer.Deserialize<PluginManifest>("""{"id":"fixture.author","name":"No author","author":"  ","type":"widget","widgets":[{"title":"Title","text":"Text"}]}""", AppStorage.Json)!;
        noAuthor.Validate(); Check(noAuthor.Author == "Author", "Unspecified author uses the literal Author fallback");
        vm.Plugins.SetEnabled(dynamicPlugin, false);
        var lyricsPackage = Path.Combine(root, "layout-lyrics.impp");
        using (var zip = ZipFile.Open(lyricsPackage, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", """{"id":"fixture.lyrics","name":"Lyrics","version":"1.0.0","type":"lyrics","permissions":["network","process","lyrics-search","navigation"],"pageEntry":"page.json","entryPoints":{"win-x64":"worker.exe"}}""");
            Write(zip, "page.json", """{"schemaVersion":1,"title":"Lyrics","widgets":[{"type":"lyrics-search"}]}""");
            Write(zip, "worker.exe", "Layout-only fixture, never executed.");
        }
        var lyricsPlugin = vm.Plugins.Install(lyricsPackage);
        using (var legacyLyrics = new LyricsSearchControl(vm.Plugins, lyricsPlugin))
        {
            var all = legacyLyrics.GetLogicalDescendants().OfType<Control>().ToArray();
            var actions = all.OfType<Grid>().Single(g => g.Name == "LyricsSearchActions");
            var operations = all.Single(c => c.Name == "LyricsSearchOperations"); var pagination = all.Single(c => c.Name == "LyricsSearchPagination");
            Check(ReferenceEquals(operations.Parent, actions) && ReferenceEquals(pagination.Parent, actions) && Grid.GetColumn(pagination) == 1, "Legacy lyrics actions share a row with right-aligned pagination");
            Check(all.OfType<TextBlock>().Single(t => t.Name == "LyricsSearchPageNumber").VerticalAlignment == Avalonia.Layout.VerticalAlignment.Center, "Lyrics page number is vertically centered");
        }
        var managedBaseline = vm.Plugins.Installed.Single(p => p.Id == "baseline.managed");
        vm.Plugins.SetEnabled(managedBaseline, true);
        var managedUpdate = Path.Combine(root, "managed-update.impp");
        using (var original = ZipFile.OpenRead(Path.Combine(root, "baseline.managed.impp")))
        using (var updatedZip = ZipFile.Open(managedUpdate, ZipArchiveMode.Create))
        {
            foreach (var entry in original.Entries)
            {
                if (entry.FullName == "manifest.json")
                {
                    using var reader = new StreamReader(entry.Open()); var document = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
                    document["version"] = "2.0.0"; Write(updatedZip, "manifest.json", document.ToJsonString());
                }
                else { using var input = entry.Open(); using var outputFile = updatedZip.CreateEntry(entry.FullName).Open(); input.CopyTo(outputFile); }
            }
        }
        var managedReplacement = vm.Plugins.Install(managedUpdate);
        Check(!managedReplacement.Enabled && !managedReplacement.ManagedExecutionConsent, "Managed update needs new host consent, even with unchanged permissions.");
        // 以已启用的旧 Agent 包验证单向迁移，不能因为旧类型不同而把更新认作另一插件。
        var oldPackage = Path.Combine(root, "migration-v1.impp");
        using (var zip = ZipFile.Open(oldPackage, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", """{"id":"migration.agent","name":"Migration","version":"1.0.0","type":"agent","permissions":["network","process","navigation","agent-control"],"pageEntry":"page.json","entryPoints":{"win-x64":"worker.exe"}}""");
            Write(zip, "page.json", """{"schemaVersion":1,"title":"Migration","widgets":[{"type":"agent-chat"}]}""");
            Write(zip, "plugin_config_schema.json", """{"caption":{"type":"string","default":"default"}}""");
            Write(zip, "worker.exe", "Not executed by the old fixture.");
        }
        var oldPlugin = vm.Plugins.Install(oldPackage); vm.Plugins.Configure(oldPlugin, """{"caption":"配置保留"}"""); vm.Plugins.SetEnabled(oldPlugin, true);
        var newPackage = Path.Combine(root, "migration-v2.impp");
        using (var zip = ZipFile.Open(newPackage, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", """{"id":"migration.agent","name":"Migration","version":"2.0.0","contractVersion":2,"type":"extension","permissions":["process","navigation"],"pageEntry":"page.json","entryPoints":{"win-x64":"bin/NonetMusicPlayer.ExtensionFixture.exe"}}""");
            Write(zip, "page.json", Page); Write(zip, "plugin_config_schema.json", """{"caption":{"type":"string","default":"default"}}""");
            foreach (var file in Directory.GetFiles(frozen)) zip.CreateEntryFromFile(file, "bin/" + Path.GetFileName(file));
        }
        var migrated = vm.Plugins.Install(newPackage);
        Check(migrated.ContractVersion == 2 && migrated.Type == "extension" && !migrated.Enabled
            && vm.Plugins.Installed.Count(p => p.Id == migrated.Id) == 1
            && vm.Plugins.ConfigurationValues(migrated)["caption"]!.GetValue<string>() == "配置保留", "V1 to V2 preserves identity/configuration and rechecks changed permissions.");
        var reverseRejected = false; try { PluginUpdatePolicy.Evaluate(migrated, oldPlugin); } catch (InvalidDataException) { reverseRejected = true; }
        Check(reverseRejected, "V2 cannot migrate back to V1.");
        Check(!PluginCommandPolicy.CatalogFor(["music-read"]).Contains("player.pause"), "Catalog filters ungranted controls.");
        var rejected = false; try { PluginCommandPolicy.ValidateFor(new("x", "player.pause", []), ["music-read"]); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Command permission gate is enforced.");
        var track = new TrackItem("lyrics-fixture", "Pure music", "", "", "", ".wav", 0) { LyricsDisabled = true };
        vm.State.Tracks.Add(track); vm.Lyrics.Save(track.Id, "[00:00.000]must be hidden"); vm.CurrentTrack = track; vm.ReloadLyrics();
        Check(vm.LyricLines.Count == 0, "Unlink hides external lyrics.");
        vm.Save(); var state = storage.Load();
        Check(state.Tracks.Single(t => t.Id == track.Id).LyricsDisabled, "Unlink survives persistence.");
        vm.ImportLyrics(vm.Lyrics.PathFor(track.Id)); Check(!track.LyricsDisabled && vm.LyricLines.Count > 0, "Manual association restores lyrics.");
        window.Close(); Console.WriteLine("PASS frozen Contract v2 process/managed, generic UI/actions/events, consent, navigation, permissions and unlink.");
    }
    /// <summary>可选实际包外观验证；默认回归不依赖任何具体插件目录。</summary>
    public static void RenderPackage(string package, string output, string? action = null)
    {
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var audio = new FakeAudio(); var storage = new AppStorage(Path.Combine(root, "data"));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm };
        var plugin = vm.Plugins.Install(Path.GetFullPath(package));
        Check(plugin.Runtime == "process", "Inspection never silently authorizes managed code.");
        vm.Plugins.SetEnabled(plugin, true); window.Show(); Pump();
        Wait(vm.Plugins.Extension(plugin).StartAsync()); vm.Navigate("plugin:" + plugin.Id); Pump();
        if (action is not null) { Wait(vm.Plugins.Extension(plugin).InvokeAsync(action, new(), userGesture: true)); Pump(); }
        foreach (var size in new[] { (1280d, 800d), (900d, 650d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; Pump(); window.UpdateLayout(); Pump();
            var view = window.GetVisualDescendants().OfType<ExtensionPageView>().Single();
            Check(!view.GetVisualDescendants().OfType<ListBox>().Any(), "Messages are not selectable list containers.");
            Check(view.GetVisualDescendants().OfType<TextBox>().All(t => !t.IsReadOnly && t.Bounds.Height <= 92), "Only bounded input controls are editable.");
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No extension frame.");
            frame.Save(Path.Combine(root, "extension-" + size.Item1 + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        window.Close(); Console.WriteLine("PASS actual optional process package uses generic renderer and bounded composer.");
    }
    private static void Wait(Task task)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < timeout) { Pump(); Thread.Sleep(5); }
        task.GetAwaiter().GetResult(); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Write(ZipArchive zip, string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true;
        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(100);
        public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Dispose() { }
    }
}
