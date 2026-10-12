using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>在实际精简包内验证动态图像；插件、配置和截图只写入显式隔离目录。</summary>
    public static int RunExtensionImage(string package,string action,string output)
    {
        var root=Path.GetFullPath(output);Directory.CreateDirectory(root);MainWindow? window=null;
        try
        {
            var storage=new AppStorage(Path.Combine(root,"data"));Environment.SetEnvironmentVariable("NONET_DATA_DIR",storage.Root);
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            using var vm=new MainViewModel(new MusicLibraryScanner(storage),new ConfigurationAudio());
            vm.Settings.CloseToTray=false;vm.Settings.ConfirmClose=false;
            window=new MainWindow { DataContext=vm,Width=1100,Height=850 };window.Show();
            var plugin=vm.Plugins.Install(Path.GetFullPath(package));vm.Plugins.SetEnabled(plugin,true);
            var session=vm.Plugins.Extension(plugin);Wait(session.StartAsync());vm.Navigate("plugin:"+plugin.Id);
            Wait(session.InvokeAsync(action,new(),userGesture:true));
            var deadline=DateTime.UtcNow.AddSeconds(20);
            while(!window.GetVisualDescendants().OfType<ExtensionPageView>().SelectMany(v=>v.GetVisualDescendants().OfType<Image>()).Any(i=>i.Source is not null)&&DateTime.UtcNow<deadline)
            { Dispatcher.UIThread.RunJobs();window.UpdateLayout();Thread.Sleep(20); }
            if(!window.GetVisualDescendants().OfType<ExtensionPageView>().SelectMany(v=>v.GetVisualDescendants().OfType<Image>()).Any(i=>i.Source is not null))throw new InvalidDataException("Dynamic image did not appear.");
            using(var bitmap=new RenderTargetBitmap(new PixelSize(1100,850))) { bitmap.Render(window);bitmap.Save(Path.Combine(root,"image.png"),PngBitmapEncoderOptions.Default); }
            vm.DisablePlugin(plugin);
            AppStorage.AtomicWrite(Path.Combine(root,"result.txt"),"PASS\nActualPublishedHost=True\nDynamicImage=True\nLifecycleRelease=True\n");return 0;
        }
        catch(Exception error) { AppStorage.AtomicWrite(Path.Combine(root,"result.txt"),"FAIL\n"+AppLog.Redact(error.ToString()));return 1; }
        finally { window?.Close();AppLog.Flush();AppLog.Shutdown(); }
    }
}
