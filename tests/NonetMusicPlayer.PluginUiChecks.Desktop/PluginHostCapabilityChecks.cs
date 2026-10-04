using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class PluginHostCapabilityChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), "beta5-plugins-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        foreach (var invalid in new[] {
            """[{"id":"author.invalid","name":"Invalid","type":"widget","widgets":[null]}]""",
            """[{"id":"author.invalid","name":"Invalid","type":"widget","widgets":[{"title":null,"text":null}]}]""",
            """[{"id":"author.invalid","name":"Invalid","type":"theme","tokens":{"Accent":null}}]""" })
        {
            var damagedStorage = new AppStorage(Path.Combine(root, "damaged-" + Guid.NewGuid().ToString("N")));
            var index = Path.Combine(damagedStorage.PluginsFolder, "installed.json"); AppStorage.AtomicWrite(index, invalid);
            using var recovered = new PluginManager(damagedStorage);
            Check(recovered.Installed.Count == 0 && recovered.RecoveryMessage is not null && File.ReadAllText(index) == invalid, "Malformed nullable plugin index reports recovery and preserves its source");
        }
        using var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(root)), audio); vm.Settings.ConfirmClose = false;
        var a = new TrackItem("pet-song-a", "music alpha", "artist", "album", Path.Combine(root, "a.wav"), ".wav", 0) { DurationSeconds = 120 };
        var b = new TrackItem("pet-song-b", "music beta", "artist", "album", Path.Combine(root, "b.wav"), ".wav", 0) { DurationSeconds = 120 };
        vm.State.Tracks.AddRange([a, b]); vm.ApplyFilter();
        // 音乐汇总仅展示歌单内歌曲；桌宠播放同样从真实歌单来源开始。
        var fixturePlaylist = vm.CreatePlaylist("桌宠播放夹具"); vm.AddToPlaylist(fixturePlaylist, [a, b]); vm.Navigate("playlist:" + fixturePlaylist.Id);
        var manifest = vm.Plugins.Install(Path.GetFullPath("artifacts/plugins/pet-controls-current.impp"));
        Check(!manifest.Enabled, "New .impp is disabled by default"); Reject(() => vm.Plugins.LoadPage(manifest), "Disabled pages cannot load");
        var window = new MainWindow { DataContext = vm }; window.Show(); Render(window); vm.Plugins.AttachHost(window, vm);
        vm.Plugins.SetEnabled(manifest, true); vm.ApplySettings(); Render(window);
        var pet = vm.Plugins.ActivePetWindows.Single(); Render(pet); Check(pet.Pet.IsTimerRunning, "Enabling generic pet starts bounded native animation");
        vm.PlayTrackAsync(a).GetAwaiter().GetResult(); Render(window); Check(pet.Message.Contains(a.Title, StringComparison.Ordinal), "Track-changed flow updates pet message");
        Click(pet, pet.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == "next")); Render(window);
        Check(vm.CurrentTrack == b && audio.LoadCount == 2, "Pet next button controls real host playback");
        Click(pet, pet.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == "favorite"));
        Check(b.IsFavorite && vm.LikedPlaylist.TrackIds.Contains(b.Id), "Pet favorite updates fixed playlist");
        Click(pet, pet.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == "search")); Render(window);
        Check(vm.Page == "songs" && vm.SearchText == "music" && vm.VisibleTracks.Count == 2, "Pet search changes host music query");
        vm.Navigate("plugins"); Render(window);
        var managerPage = window.GetVisualDescendants().OfType<PluginsView>().Single();
        var toggle = managerPage.GetVisualDescendants().OfType<ToggleSwitch>().Single();
        var trash = managerPage.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PluginUninstall");
        Check(Math.Abs(toggle.TranslatePoint(default, managerPage)!.Value.Y + toggle.Bounds.Height / 2 - trash.TranslatePoint(default, managerPage)!.Value.Y - trash.Bounds.Height / 2) < 4, "Enable and trash share the same action row");
        Check(managerPage.GetVisualDescendants().OfType<TextBlock>().All(t => t.Text?.Contains("让你的音乐库", StringComparison.Ordinal) != true), "Plugin center contains management only");
        using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No plugin center frame")) frame.Save(Path.Combine(Path.GetFullPath(output), "plugin-center-beta5.png"), PngBitmapEncoderOptions.Default);
        using (var frame = pet.CaptureRenderedFrame() ?? throw new InvalidOperationException("No pet frame")) frame.Save(Path.Combine(Path.GetFullPath(output), "pet-controls-beta5.png"), PngBitmapEncoderOptions.Default);
        vm.Settings.Language = "en-US"; vm.ApplySettings(); Render(window);
        Check(pet.GetVisualDescendants().OfType<Button>().Single(button => button.Tag as string == "next").Content as string == L10n.T("Common.Next"), "Existing floating pet actions change language without restart");
        vm.Settings.Language = "zh-CN"; vm.ApplySettings(); Render(window);
        toggle = window.GetVisualDescendants().OfType<PluginsView>().Single().GetVisualDescendants().OfType<ToggleSwitch>().Single();
        Click(window, toggle); Render(window); Check(!manifest.Enabled && pet.IsDisposed && !pet.Pet.IsTimerRunning && vm.Plugins.ActivePetWindows.Count == 0, "Actual toggle disables pet events and timer");
        Reject(() => PluginHostActions.Execute(vm.Plugins, manifest, vm, new("next")), "Disabled plugins cannot use host actions");
        vm.Plugins.SetEnabled(manifest, true); var second = vm.Plugins.ActivePetWindows.Single(); vm.Plugins.Uninstall(manifest);
        Check(second.IsDisposed && vm.Plugins.ActivePetWindows.Count == 0, "Uninstall closes active pet");
        var legacy = vm.Plugins.Install(Package(root, "legacy-widget", """{"id":"author.legacy-notes","name":"Notes","version":"1.0.0","contractVersion":1,"type":"widget","widgets":[{"title":"Note","text":"Content"}]}""", null));
        vm.Plugins.SetEnabled(legacy, true); Check(vm.Plugins.LoadPage(legacy).Widgets.Single().Text == "Content", "Legacy widget becomes a real page, not plugin-center decoration");
        var sourceManifest = File.ReadAllText("samples/MusicPet.Ui/manifest.json"); var sourcePage = File.ReadAllText("samples/MusicPet.Ui/page.json");
        Reject(() => PluginManager.Inspect(Package(root, "missing-permission", sourceManifest.Replace("\"player-control\", ", "", StringComparison.Ordinal), sourcePage)), "Player actions require explicit permission");
        Reject(() => PluginManager.Inspect(Package(root, "automatic-next", sourceManifest, sourcePage.Replace("\"kind\": \"show-message\", \"value\": \"正在播放：{title} · {artist}\"", "\"kind\": \"next\"", StringComparison.Ordinal))), "Automatic playback loops are rejected");
        Reject(() => PluginManager.Inspect(Package(root, "fast-flow", sourceManifest, sourcePage.Replace("120", "1", StringComparison.Ordinal))), "Message timers are bounded to 60 seconds or slower");
        Reject(() => PluginManager.Inspect(Package(root, "shell-action", sourceManifest, sourcePage.Replace("\"kind\": \"search\"", "\"kind\": \"shell\"", StringComparison.Ordinal))), "No arbitrary host process action");
        vm.Plugins.Uninstall(legacy); window.Close();
        RestartEnabledPet(root);
        Console.WriteLine("PASS BETA5 PLUGIN: .impp, native pet, host next/favorite/search, flows, same-row toggle/trash, lifecycle, enabled restart, legacy widgets, permission and flow limits");
    }
    private static void RestartEnabledPet(string root)
    {
        var storage = new AppStorage(Path.Combine(root, "restart-data"));
        using (var installer = new PluginManager(storage))
        {
            var installed = installer.Install(Path.GetFullPath("artifacts/plugins/pet-controls-current.impp")); installer.SetEnabled(installed, true);
            Check(installer.ActivePetWindows.Count == 0, "A plugin without a host allocates no pet window");
        }
        using var audio = new FakeAudio(); using var restartedVm = new MainViewModel(new MusicLibraryScanner(storage), audio); restartedVm.Settings.ConfirmClose = false;
        Check(restartedVm.Plugins.Installed.Single().Enabled, "Enabled plugin is persisted for restart");
        var restartedWindow = new MainWindow { DataContext = restartedVm };
        Check(!restartedWindow.IsVisible && restartedVm.Plugins.ActivePetWindows.Count == 0, "Unshown owner defers pet allocation and timers");
        restartedWindow.Show(); Render(restartedWindow);
        var restoredPet = restartedVm.Plugins.ActivePetWindows.Single(); Render(restoredPet);
        Check(restoredPet.IsVisible && restoredPet.Pet.IsTimerRunning, "Owner Opened starts persisted enabled pet without hidden-owner error");
        restartedWindow.Close();
        Check(restoredPet.IsDisposed && !restoredPet.Pet.IsTimerRunning && restartedVm.Plugins.ActivePetWindows.Count == 0, "Restarted host close releases pet subscriptions and animation");
    }
    private static void Render(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    private static string Package(string root, string name, string manifest, string? page)
    {
        var path = Path.Combine(root, name + ".impp"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("manifest.json", manifest); if (page is not null) Write("page.json", page); return path;
        void Write(string file, string content) { using var writer = new StreamWriter(zip.CreateEntry(file).Open(), new UTF8Encoding(false)); writer.Write(content); }
    }
    private static void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { return; } throw new InvalidOperationException(message); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; } public int LoadCount { get; private set; }
        public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(120); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; LoadCount++; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; } public void Dispose() { }
    }
}
