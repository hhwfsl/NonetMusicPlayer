using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>使用独立通用夹具验证音源注册、搜索、持久引用，不依赖任何私人插件。</summary>
internal static class MusicSearchChecks
{
    public static void Run(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        if(Application.Current is null)AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).SetupWithoutStarting();
        var repo=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
        var fixture=Path.Combine(repo,"tests/NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0");
        var manifest=new PluginManifest { Id="fixture.music-search",Name="Source fixture",Version="1.0.0",ContractVersion=2,Type="extension",Runtime="process",PageEntry="page.json",Permissions=["network","process","navigation","library-write"],ProvidedServices=["media-source"],MusicSearch=new() { Label="Cloud fixture" },RequiredCapabilities=["media.search.v1"],EntryPoints=new() { ["win-x64"]="bin/NonetMusicPlayer.ExtensionFixture.exe" } };
        var package=Path.Combine(root,"fixture.impp");using(var zip=ZipFile.Open(package,ZipArchiveMode.Create))
        {
            using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(JsonSerializer.Serialize(manifest,AppStorage.Json));
            using(var writer=new StreamWriter(zip.CreateEntry("page.json").Open()))writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"Fixture"}}""");
            foreach(var file in Directory.GetFiles(fixture).Where(f=>!f.EndsWith(".pdb")))zip.CreateEntryFromFile(file,"bin/"+Path.GetFileName(file));
        }
        var storage=new AppStorage(Path.Combine(root,"data"));using var vm=new MainViewModel(new MusicLibraryScanner(storage),new FakeAudio());
        vm.Settings.CloseToTray=false;vm.Settings.ConfirmClose=false;var window=new MainWindow { DataContext=vm,Width=1200,Height=800 };window.Show();
        var plugin=vm.Plugins.Install(package);vm.Plugins.SetEnabled(plugin,true);vm.Navigate("library");Pump();
        var selector=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="MusicSearchSource");Check(selector.SelectedIndex==0&&selector.Items.Count==2,"Local default and optional source");
        Wait(vm.SearchMusicAsync(plugin.Id,"fixture"));Check(vm.Page=="cloud-search"&&vm.VisibleTracks.Count==1&&vm.State.Tracks.Count==0,"Search without automatic import");
        var cloud=vm.VisibleTracks.Single();var playlist=vm.CreatePlaylist("Mixed source");vm.AddToPlaylist(playlist,[cloud]);Check(vm.State.Tracks.Single().ProviderTrackId=="remote-1"&&cloud.IsCloud,"Cloud ID persisted");
        Wait(vm.RefreshProviderAsync(plugin));Check(vm.State.Tracks.Count==1&&playlist.TrackIds.Contains(cloud.Id),"Refresh cannot erase searched/cloud references");
        var catalog=Wait(vm.Plugins.ReadMusicCatalogAsync(plugin,"fixture-catalog"));Check(catalog.Count==1&&catalog[0].Id==cloud.Id,"Generic catalog identity");
        vm.Save();Check(storage.Load().Tracks.Single().ProviderId==plugin.Id,"SQLite roundtrip");
        Wait(vm.SearchMusicAsync("","Cloud fixture"));Check(vm.Page=="songs"&&vm.VisibleTracks.Count==1,"Local collection search uses imported references");
        vm.Navigate("library");Pump();using(var bitmap=new RenderTargetBitmap(new PixelSize(1200,800))) { bitmap.Render(window);bitmap.Save(Path.Combine(root,"music-search.png"),PngBitmapEncoderOptions.Default); }
        vm.DisablePlugin(plugin);vm.Navigate("library");Pump();Check(window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="MusicSearchSource").Items.Count==1,"Disabled source removed");
        Check(vm.State.Tracks.Count==1&&playlist.TrackIds.Count==1,"Disabling preserves playlist references");
        window.Close();Pump();Console.WriteLine("PASS generic music search registration/UI, cloud playlist references/SQLite, refresh, catalog, local filter and disable; "+root);
    }
    private static T Wait<T>(Task<T> task) { while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}task.GetAwaiter().GetResult(); }
    private static void Pump() { for(var i=0;i<20;i++){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);} }
    private static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private sealed class FakeAudio : NonetMusicPlayer.Core.Audio.IAudioPlayer
    {
        public bool IsAvailable=>true; public bool IsPlaying=>false; public TimeSpan Position {get;set;}public TimeSpan Duration=>TimeSpan.Zero;public float Volume {get;set;}
        public event EventHandler? PlaybackStopped {add{}remove{}}public Task LoadAsync(string source,CancellationToken token=default)=>Task.CompletedTask;public void Play(){}public void Pause(){}public void Stop(){}public void Dispose(){}
    }
}
