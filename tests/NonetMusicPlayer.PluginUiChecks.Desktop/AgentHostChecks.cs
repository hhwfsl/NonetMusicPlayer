using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class AgentHostChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), "agent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var audio = new FakeAudio();
        var storage = new AppStorage(root);
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.ConfirmClose = false; vm.Settings.CloseToTray = false;
        var window = new MainWindow { DataContext = vm }; window.Show(); Render(window);
        var package = Path.Combine(root, "fixture.impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", """{"id":"fixture.agent","name":"Generic Agent","version":"1.0.0","type":"agent","permissions":["network","process","agent-control","navigation"],"pageEntry":"page.json","entryPoints":{"win-x64":"worker.exe"}}""");
            Write(zip, "page.json", """{"schemaVersion":1,"title":"Generic Agent","widgets":[{"type":"agent-chat","title":"Chat"}]}""");
            Write(zip, PluginConfigSchema.FileName, """{"provider":{"type":"string","default":"custom","enum":[{"value":"custom","label":"Custom"},{"value":"fixture","label":"Fixture","defaults":{"baseUrl":"https://preset.invalid/v1","apiKey":"","model":""}}]},"baseUrl":{"type":"string","default":""},"model":{"type":"string","default":"","ui:widget":"agent-model"},"apiKey":{"type":"string","default":"","sensitive":true,"ui:widget":"password"},"maxTokens":{"type":"int","default":1200,"min":0},"timeoutSeconds":{"type":"int","default":90,"min":0}}""");
            Write(zip, "worker.exe", "Not executed by this UI fixture");
        }
        var plugin = vm.Plugins.Install(package); vm.Plugins.SetEnabled(plugin, true); vm.ApplySettings();
        vm.Plugins.Configure(plugin, """{"provider":"fixture","baseUrl":"https://fixture.invalid/v1","model":"fixture","apiKey":"TEST-ONLY-RESTART-KEY","maxTokens":0,"timeoutSeconds":0}""");
        var protectedPath = PluginConfigurationStore.PathFor(storage.PluginsFolder, plugin);
        Check(File.Exists(protectedPath) && !File.ReadAllText(protectedPath).Contains("TEST-ONLY-RESTART-KEY")
            && !File.ReadAllText(Path.Combine(storage.PluginsFolder, "installed.json")).Contains("TEST-ONLY-RESTART-KEY"), "No plaintext key on disk");
        using (var restarted = new PluginManager(storage))
        {
            var restored = restarted.ConfigurationValues(restarted.Installed.Single(p => p.Id == plugin.Id));
            Check(restored["apiKey"]!.GetValue<string>() == "TEST-ONLY-RESTART-KEY"
                && restored["maxTokens"]!.GetValue<int>() == 0, "New manager restores encrypted configuration and zero limits");
        }
        var overlay = window.OpenPluginConfigurationAsync(plugin); Render(window);
        var form = window.GetVisualDescendants().OfType<PluginConfigView>().Single();
        Check(form.Draft["apiKey"]!.GetValue<string>() == "TEST-ONLY-RESTART-KEY", "Opening saved provider does not reset its key");
        var selector = form.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PluginConfig_provider");
        selector.SelectedIndex = 0; selector.SelectedIndex = 1; Render(window);
        Check(form.Draft["apiKey"]!.GetValue<string>() == "" && form.Draft["baseUrl"]!.GetValue<string>() == "https://preset.invalid/v1", "Preset applies same-schema defaults and clears old key");
        Check(form.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "AgentFetchModels"), "Schema model selector has discovery button");
        form.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ClosePluginConfiguration").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Render(window);
        Check(overlay.IsCompleted && !overlay.Result, "Closing model draft does not save");
        vm.Navigate("plugin:" + plugin.Id); Render(window);
        var control = window.GetVisualDescendants().OfType<AgentChatControl>().Single();
        Check(control.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AgentTranscript") is { IsReadOnly: true }, "Read-only chat transcript");
        Check(FullTextToolTips.GetEnabled(control.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AgentInput")!) == false, "No full-text tooltip on chat input");
        typeof(AgentChatControl).GetMethod("Append", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, ["Nonet Agent", "已找到歌曲，可以继续播放。\nThis response is read-only.", "检查当前音乐列表。"]);
        Render(window);
        Check(control.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name != "AgentInput").All(t => t.IsReadOnly), "All conversation and reasoning controls are read-only");
        foreach (var size in new[] { (1280d, 800d), (900d, 650d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; Render(window);
            var input = control.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AgentInput");
            Check(input.Bounds.Height >= 70 && control.Bounds.Height > 300, "Composer and conversation fit window");
            using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(root, "chat-" + size.Item1 + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        var invoke = typeof(AgentChatControl).GetMethod("ExecuteToolAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var allowed = (Task<string>)invoke.Invoke(control, [new AgentToolCall("a","player.volume",["42"]), CancellationToken.None])!;
        Pump(allowed, window);
        Check(vm.Volume == 42 && allowed.Result.Contains("true"), "Agent action updates the actual VM");
        Check(window.Commands.Journal.Last().Operation == "player.volume", "Agent action uses shared terminal results");
        var rejected = (Task<string>)invoke.Invoke(control, [new AgentToolCall("b","plugins.install",["bad.impp"]), CancellationToken.None])!;
        Pump(rejected, window); Check(rejected.Result.Contains("false") && vm.Plugins.Installed.Count == 1, "Forbidden install does not execute");
        vm.Plugins.SetEnabled(plugin, false); Render(window);
        var stopped = (Task<string>)invoke.Invoke(control, [new AgentToolCall("c","player.volume",["90"]), CancellationToken.None])!;
        try { Pump(stopped, window); } catch (OperationCanceledException) { }
        Check(vm.Volume == 42, "Disabled agent cannot operate");

        vm.Navigate("lyrics"); Render(window);
        vm.LyricLines.Clear();
        foreach (var line in LyricsService.Parse("[00:00.000]原文[00:01.000]歌词\n[00:00.000]Translation")) vm.LyricLines.Add(line);
        Render(window);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            vm.Settings.Theme = theme; vm.Settings.Accent = "#AADDFF"; vm.ApplySettings(); Render(window);
            var expected = Color.Parse(vm.Settings.Accent);
            var lines = window.GetVisualDescendants().OfType<KaraokeLine>().ToArray();
            Check(lines.Length > 0 && lines.All(l => l.TextColor == expected), "Original/translation karaoke uses exact accent in " + theme);
        }
        vm.Navigate("settings"); Render(window);
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "GitHub"), "About repository displays GitHub");
        window.Close();
        Console.WriteLine("PASS Agent actual host page, shared commands, disabled lifecycle, no install capability, exact lyric accent and GitHub About");
    }
    private static void Write(ZipArchive zip, string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(content); }
    private static void Render(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Pump(Task task, Window window)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Render(window); Thread.Sleep(5); }
        task.GetAwaiter().GetResult();
    }
    private static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
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
