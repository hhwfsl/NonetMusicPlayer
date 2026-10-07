using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class ComposerExtensionChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var source = Path.Combine(root, "package"); Directory.CreateDirectory(source);
        // 完全独立的最小图片和控件夹具，不依赖私人插件或素材。
        using (var image = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(16,16), new Vector(96,96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul))
        {
            using(var pixels = image.Lock()) System.Runtime.InteropServices.Marshal.Copy(new byte[pixels.RowBytes * pixels.Size.Height],0,pixels.Address,pixels.RowBytes*pixels.Size.Height);
            image.Save(Path.Combine(source,"icon.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        File.Copy(typeof(UniversalFixture).Assembly.Location, Path.Combine(source,"fixture.dll"));
        var manifest = new PluginManifest { Id="fixture.composer", Name="Composer fixture", Type="extension", ContractVersion=2, Runtime="managed", ExtensionClass=typeof(UniversalFixture).FullName!, PageEntry="page.json", SupportsApprovalModes=true, NavigationImage="icon.png", Permissions=["in-process","native-ui","ui-extend","navigation","music-read"], RequiredCapabilities=["ui.native.v1","ui.overlay.v1","ui.assets.v1","ui.composer.v1","config.approval.v1"], EntryPoints={{PluginPlatformPolicy.CurrentRid,"fixture.dll"}}, Contributions=[new("overlay","Floating fixture","open-page"){Native=true,Overlay=new(180,160,true)}] };
        File.WriteAllText(Path.Combine(source,"manifest.json"),JsonSerializer.Serialize(manifest,AppStorage.Json));
        File.WriteAllText(Path.Combine(source,"page.json"),"""{"schemaVersion":2,"root":{"type":"border","cornerRadius":24,"children":[{"type":"stack","children":[{"type":"image","asset":"icon.png","width":20,"height":20},{"type":"input","id":"FixtureComposer","input":"message","multiline":true,"borderless":true,"maxHeight":180},{"type":"select","id":"FixtureChoice","bind":"options","selectedBind":"selected","input":"approval"}]}]}}""");
        File.WriteAllText(Path.Combine(source,"plugin_config_schema.json"),"""{"approvalMode":{"type":"string","default":"ask","enum":["ask","assist","full"]}}""");
        var package = PluginPackageBuilder.Pack(source,Path.Combine(root,"fixture.impp"));
        Check(PluginPackageInspector.Inspect(package).NavigationImage=="icon.png","Declared assets included automatically.");
        using var audio=new FakeAudio(); using var vm=new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root,"data"))),audio);
        vm.Settings.CloseToTray=false; vm.Settings.ConfirmClose=false;
        var window=new MainWindow{DataContext=vm,Width=1000,Height=720}; window.Show();
        var plugin=vm.Plugins.Install(package); plugin.ManagedExecutionConsent=true; vm.Plugins.SetEnabled(plugin,true); vm.ApplySettings(); Pump();
        var session=vm.Plugins.Extension(plugin); Wait(session.StartAsync()); vm.Navigate("plugin:"+plugin.Id); Pump();
        Check(window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="FixtureComposer").MaxHeight==180,"Composer height is applied.");
        var field=typeof(MainWindow).GetField("_extensionOverlays",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var overlays=(Dictionary<string,Window>)field.GetValue(window)!;
        Check(overlays.TryGetValue(plugin.Id,out var overlay) && overlay.WindowDecorations==WindowDecorations.None && overlay.Topmost,"Transparent native overlay loaded.");
        window.WindowState=WindowState.Minimized; Pump(); Check(overlay!.IsVisible,"Independent overlay remains visible after minimize."); window.WindowState=WindowState.Normal;
        Wait(session.InvokeAsync("config",new(){["operation"]="approval.read"},true));
        Check(session.Frame.State["result"]!["mode"]!.GetValue<string>()=="ask","Initial mode remains safe.");
        var setting=session.InvokeAsync("config",new(){["operation"]="approval.set",["mode"]="assist"},true);
        Until(()=>window.OwnedWindows.OfType<PlayerDialog>().Any()); window.OwnedWindows.OfType<PlayerDialog>().Single().Close(true); Wait(setting);
        Check(plugin.ApprovalMode=="assist" && !session.IsDisposed,"Inline approval persists without destroying the session.");
        Check(JsonNode.Parse(File.ReadAllText(PluginConfigurationStore.PathFor(vm.Storage.PluginsFolder,plugin)))!["values"]!["approvalMode"]!.GetValue<string>()=="assist" && JsonNode.Parse(PluginConfigurationStore.Read(vm.Storage.PluginsFolder,plugin)!)!["approvalMode"]!.GetValue<string>()=="assist","Public and protected configuration retain the inline mode.");
        var denied=session.InvokeAsync("config",new(){["operation"]="approval.set",["mode"]="full"});
        Wait(denied); Check(plugin.ApprovalMode=="assist" && session.Frame.State["result"]!["success"]!.GetValue<bool>()==false,"Background mode escalation is rejected.");
        vm.DisablePlugin(plugin); Pump(); Check(!overlays.ContainsKey(plugin.Id),"Disable releases overlay registration.");
        window.Close(); Console.WriteLine("PASS generic asset packing, composer properties, native overlay/minimize/lifecycle, persisted inline approval and blocked background escalation.");
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Pump(){for(var i=0;i<20;i++){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}}
    private static void Until(Func<bool> test){var deadline=DateTime.UtcNow.AddSeconds(15);while(!test()&&DateTime.UtcNow<deadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}Check(test(),"Fixture timeout.");}
    private static void Wait(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();Pump();}
    private sealed class FakeAudio:NonetMusicPlayer.Core.Audio.IAudioPlayer
    {
        public bool IsAvailable=>true; public bool IsPlaying=>false; public TimeSpan Position{get;set;} public TimeSpan Duration=>TimeSpan.FromSeconds(10); public float Volume{get;set;}
        public event EventHandler? PlaybackStopped{add{} remove{}} public Task LoadAsync(string source,CancellationToken cancellationToken=default)=>Task.CompletedTask; public void Play(){} public void Pause(){} public void Stop(){} public void Dispose(){}
    }
}
