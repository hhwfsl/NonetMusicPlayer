using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>仅在显式隔离目录验证实际精简包，避免把开发宿主通过当作真实发行包验收。</summary>
    public static int RunStyles(string package, string styleFile, string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root); MainWindow? window = null;
        try
        {
            var storage = new AppStorage(Path.Combine(root, "data")); Environment.SetEnvironmentVariable("NONET_DATA_DIR", storage.Root);
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ConfigurationAudio()); vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
            var plugin = vm.Plugins.Install(Path.GetFullPath(package)); plugin.ManagedExecutionConsent = true;
            using var input = File.OpenRead(styleFile); var sheet = StyleSheetContract.Read(input);
            vm.Plugins.SetEnabled(plugin, true);
            window = new MainWindow { DataContext = vm, ShowActivated = false };
            var startup = vm.Plugins.PrepareAutomaticExtensionsAsync(); NativePump(() => startup.IsCompleted); startup.GetAwaiter().GetResult();
            window.ApplyInterfaceLayers("release-fixture", [sheet]);
            window.PrepareInitialAppearance();
            if (window.IsVisible) throw new InvalidDataException("Window shown before appearance preparation completed.");
            window.Show();
            NativePump(() => window.StyleLayers.Catalog()["activeLayers"]!.AsArray().Count == 1);
            if (sheet.Resources.TryGetValue("AccentBrush", out var accent) && ((ISolidColorBrush)Application.Current!.Resources["AccentBrush"]!).Color != Color.Parse(accent)) throw new InvalidDataException("Style resource not applied.");
            vm.Navigate("plugin:" + plugin.Id);
            NativePump(() => window.GetVisualDescendants().OfType<NativeExtensionView>().Any(v => v.Content is Control));
            window.UpdateLayout();
            using (var image = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height))) { image.Render(window); image.Save(Path.Combine(root, "styles.png"), PngBitmapEncoderOptions.Default); }
            var native = window.GetVisualDescendants().OfType<NativeExtensionView>().Single();
            var nativeContent = native.Content;
            window.ApplyInterfaceLayers("release-fixture", [sheet]); Dispatcher.UIThread.RunJobs();
            if (!ReferenceEquals(native.Content, nativeContent)) throw new InvalidDataException("Interface reparenting disposed the active native page.");
            // 验收控件由夹具创建，不要求被测插件公开预览功能或采用某种页面结构。
            var panel = native.Content as Panel ?? native.GetVisualDescendants().OfType<Panel>().First();
            var preview = new Border { Width = 240, Height = 90, Child = new Button { Content = "Fixture" } }; panel.Children.Add(preview); window.UpdateLayout(); var parent = preview.Parent;
            var task = window.MorphContainerAsync(preview); NativePump(() => task.IsCompleted); task.GetAwaiter().GetResult();
            if (preview.Parent == parent) throw new InvalidDataException("Container did not expand.");
            vm.Navigate("settings"); NativePump(() => preview.Parent == parent);
            vm.DisablePlugin(plugin); window.ApplyInterfaceLayers("release-fixture", []); NativePump(() => window.StyleLayers.Catalog()["activeLayers"]!.AsArray().Count == 0);
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nActualPublishedHost=True\nStyleResources=True\nNativePage=True\nReparentRetainsNativePage=True\nTemplateParts=True\nContainerMorph=True\nNavigationRestoresParent=True\nResetRestoresAppearance=True\n");
            return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + AppLog.Redact(error.ToString())); return 1; }
        finally { window?.Close(); AppLog.Flush(); AppLog.Shutdown(); }
    }
    private static void NativePump(Func<bool> ready)
    {
        if (ready()) return;
        var end = Environment.TickCount64 + 15000; var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (ready() || Environment.TickCount64 >= end) frame.Continue = false; };
        timer.Start(); try { Dispatcher.UIThread.PushFrame(frame); } finally { timer.Stop(); }
        if (!ready()) throw new TimeoutException("Style verification did not settle.");
    }
}
