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
        using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(root)), audio);
        vm.Settings.ConfirmClose = false; vm.Settings.CloseToTray = false;
        var window = new MainWindow { DataContext = vm }; window.Show(); Render(window);
        var package = Path.Combine(root, "fixture.impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "manifest.json", """{"id":"fixture.agent","name":"Generic Agent","version":"1.0.0","type":"agent","permissions":["network","process","agent-control","navigation"],"pageEntry":"page.json","entryPoints":{"win-x64":"worker.exe"}}""");
            Write(zip, "page.json", """{"schemaVersion":1,"title":"Generic Agent","widgets":[{"type":"agent-chat","title":"Chat"}]}""");
            Write(zip, "worker.exe", "Not executed by this UI fixture");
        }
        var plugin = vm.Plugins.Install(package); vm.Plugins.SetEnabled(plugin, true); vm.ApplySettings();
        vm.Navigate("plugin:" + plugin.Id); Render(window);
        var control = window.GetVisualDescendants().OfType<AgentChatControl>().Single();
        Check(control.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AgentTranscript") is { IsReadOnly: true }, "Read-only chat transcript");
        Check(FullTextToolTips.GetEnabled(control.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AgentInput")!) == false, "No full-text tooltip on chat input");
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
