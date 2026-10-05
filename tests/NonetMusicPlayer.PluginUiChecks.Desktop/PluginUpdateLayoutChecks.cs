using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>验证触摸/3:2 布局及插件操作行；只使用隔离数据和最小主题夹具。</summary>
internal static class PluginUpdateLayoutChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), "update-layout-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var storage = new AppStorage(Path.Combine(root, "Data"), root);
        using var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.ConfirmClose = false; vm.Settings.CloseToTray = false;
        string Package(string version)
        {
            var source = Path.Combine(root, "source-" + version); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "manifest.json"), JsonSerializer.Serialize(new PluginManifest { Id = "layout.update", Name = "Update fixture", Type = "theme", Version = version, RepositoryOwner = "developer", RepositoryName = "nonet_plugin_fixture", Tokens = new() { ["Accent"] = "#6699CC" } }, AppStorage.Json));
            return PluginPackageBuilder.Pack(source, Path.Combine(root, version + ".impp"));
        }
        var installing = vm.Plugins.InstallAsync(Package("1.0.0"), "origin/actual_repo");
        // 保持 UI 消息泵运行，让安装事务回到创建者线程提交，而不是同步等待造成死锁。
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!installing.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(15)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Check(installing.IsCompleted, "Asynchronous installation returns to UI thread");
        var installed = installing.GetAwaiter().GetResult();
        vm.Plugins.Configure(installed, "{\"retained\":true}");
        var upgraded = vm.Plugins.Install(Package("1.0.1"));
        Check(upgraded.OriginRepository == "origin/actual_repo" && upgraded.Configuration.Contains("retained"), "Local update preserves original remote provenance/config");
        using (var restarted = new PluginManager(storage)) Check(restarted.Installed.Single().OriginRepository == "origin/actual_repo", "Remote provenance persists across restart");
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump(window);
        foreach (var touch in new[] { false, true })
        foreach (var left in new[] { false, true })
        foreach (var size in new[] { new Size(1440, 960), new Size(1080, 720), new Size(900, 600), new Size(640, 480) })
        {
            vm.Settings.TouchMode = touch; vm.Settings.TitleButtonsOnLeft = left; vm.ApplySettings();
            window.Width = size.Width; window.Height = size.Height; Pump(window);
            var lights = window.FindControl<StackPanel>("TitleButtons")!;
            Check(lights.Bounds.Width <= 66.1 && lights.Children.All(b => Math.Abs(b.Bounds.Width - 22) < .1), "Touch and 3:2 sizing retain compact 22 DIP button slots");
            Check(lights.Children.Select(b => b.Name).SequenceEqual(left ? new[] { "CloseWindowButton", "MinimizeWindowButton", "MaximizeWindowButton" } : new[] { "MinimizeWindowButton", "MaximizeWindowButton", "CloseWindowButton" }), "Both titlebar orders preserved");
        }
        window.Width = 1080; window.Height = 720; vm.Navigate("plugins"); Pump(window);
        var view = window.GetVisualDescendants().OfType<PluginsView>().Single();
        var configure = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PluginConfigure");
        var update = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PluginUpdate");
        var trash = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PluginUninstall");
        Check(configure.Bounds.Center.Y == update.Bounds.Center.Y && update.Bounds.Center.Y == trash.Bounds.Center.Y && Grid.GetColumn(configure) < Grid.GetColumn(update) && Grid.GetColumn(update) < Grid.GetColumn(trash), "Configure/update/delete share vertically centered row and order");
        var dialogResult = PlayerDialog.Uninstall(window, "Uninstall fixture"); Pump(window);
        var dialog = window.OwnedWindows.Single();
        var row = dialog.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PluginDeleteFilesRow");
        var toggle = row.GetVisualDescendants().OfType<ToggleSwitch>().Single(); var label = row.Children.OfType<TextBlock>().Single();
        Console.WriteLine($"Uninstall row={row.Bounds}, toggle={toggle.Bounds}, label={label.Bounds}");
        Check(Grid.GetColumn(toggle) == 1 && Math.Abs(toggle.Bounds.Center.Y - label.Bounds.Center.Y) <= 1, "Delete files toggle stays on same row at right (pixel rounding tolerance)");
        dialog.Close(); Pump(window); Check(dialogResult.IsCompleted, "Uninstall cancellation completes");
        window.Close(); Console.WriteLine("PASS compact Surface-like 3:2/touch titlebar, update button order/alignment, uninstall toggle row, persisted provenance/config");
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
