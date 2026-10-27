using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>验证精简发行程序的配置表单；可选启动真实插件验证运行中保存，不连接模型。</summary>
    public static int RunPluginConfiguration(string package, string output, string? configureAction = null)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        MainWindow? window = null;
        try
        {
            var storage = new AppStorage(Path.Combine(root, "data"));
            Environment.SetEnvironmentVariable("NONET_DATA_DIR", storage.Root);
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            using var audio = new ConfigurationAudio();
            using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
            vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
            window = new MainWindow { DataContext = vm, Width = 1100, Height = 750 };
            var plugin = vm.Plugins.Install(Path.GetFullPath(package));
            window.Show(); Pump();
            Task? invocation = null;
            if (configureAction is not null)
            {
                plugin.ManagedExecutionConsent = true; vm.Plugins.SetEnabled(plugin, true); vm.Navigate("plugin:" + plugin.Id); Pump();
                invocation = vm.Plugins.Extension(plugin).InvokeAsync(configureAction, new(), true);
                Until(() => window.HasPluginConfigurationOverlay);
            }
            var overlay = window.OpenPluginConfigurationAsync(plugin); Pump();
            var form = window.GetVisualDescendants().OfType<PluginConfigView>().Single();
            Expand(form);
            // 提供商选项包含预设重建路径；不点击模型发现按钮，不保存任何真实凭据。
            var preset = form.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => c.Name == "PluginConfig_provider");
            if (preset is not null) for (var i = 0; i < preset.ItemCount; i++)
            {
                var current = form.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PluginConfig_provider");
                current.SelectedIndex = i; Pump(); Expand(form);
            }
            form.Save(); Pump();
            if (!overlay.IsCompletedSuccessfully || !overlay.Result) throw new InvalidDataException("Configuration save did not close overlay.");
            if (invocation is not null)
            {
                Until(() => invocation.IsCompleted);
                try { invocation.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                Pump(); if (!plugin.Enabled || vm.Plugins.Extension(plugin).IsDisposed) throw new InvalidDataException("Live configuration fault-disabled the plugin.");
            }
            overlay = window.OpenPluginConfigurationAsync(plugin); Pump();
            form = window.GetVisualDescendants().OfType<PluginConfigView>().Single(); Expand(form);
            form.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ClosePluginConfiguration")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
            if (!overlay.IsCompletedSuccessfully || overlay.Result) throw new InvalidDataException("Configuration discard failed.");
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), $"PASS\nActualPublishedHost=True\nNativeLayout=True\nRepeatedExpansion=True\nPresetRebuild=True\nSaveReopen=True\nLiveConfiguration={invocation is not null}\nNoModelRequests=True\n");
            return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + AppLog.Redact(error.ToString())); return 1; }
        finally { window?.Close(); AppLog.Flush(); AppLog.Shutdown(); }

        void Pump() { Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void Until(Func<bool> ready) { var end = Environment.TickCount64 + 20000; while (!ready() && Environment.TickCount64 < end) { Pump(); Thread.Sleep(5); } if (!ready()) throw new TimeoutException("Configuration verification timed out."); }
        void Expand(PluginConfigView view)
        {
            var groups = view.GetVisualDescendants().OfType<Expander>().ToArray();
            // 没有折叠分组也是合法配置页；实际存在分组时才重复展开验证。
            foreach (var group in groups) for (var repeat = 0; repeat < 3; repeat++)
            { group.IsExpanded = true; Pump(); group.IsExpanded = false; Pump(); }
            foreach (var group in groups) group.IsExpanded = true;
            Pump();
        }
    }

    private sealed class ConfigurationAudio : IAudioPlayer
    {
        public bool IsAvailable => false;
        public bool IsPlaying => false;
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.Zero;
        public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
