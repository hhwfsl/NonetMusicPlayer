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
        Wait(vm.Plugins.Extension(plugin).StartAsync());
        // 通用图片夹具独立于私人扫码业务，验证替换、无效输入与资源释放。
        using(var view=new ExtensionPageView(vm.Plugins,plugin,new() { Type="image",ImageDataBind="image",Width=96,Height=96 }))
        {
            var imageWindow=new Window { Content=view,Width=160,Height=160 };imageWindow.Show();Pump();
            var image=view.GetVisualDescendants().OfType<Image>().Single();Check(image.Source is not null,"Dynamic image binding");
            Wait(vm.Plugins.Extension(plugin).InvokeAsync("clear-image",new()));Pump();Check(image.Source is null,"Empty state releases image");
            Wait(vm.Plugins.Extension(plugin).InvokeAsync("invalid-image",new()));Pump();Check(image.Source is null&&!vm.Plugins.Extension(plugin).IsDisposed,"Invalid image is harmless");
            imageWindow.Close();Pump();
        }
        var selector=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="MusicSearchSource");Check(selector.SelectedIndex==0&&selector.Items.Count==2,"Local default and optional source");
        Wait(vm.SearchMusicAsync(plugin.Id,"fixture"));Check(vm.Page=="cloud-search"&&vm.VisibleTracks.Count==1&&vm.State.Tracks.Count==0,"Search without automatic import");
        var cloud=vm.VisibleTracks.Single();Check(cloud.HasArtwork&&cloud.HasSearchCover&&File.Exists(cloud.CoverPath),"Search preview has a cover before playback");
        var playlist=vm.CreatePlaylist("Mixed source");vm.AddToPlaylist(playlist,[cloud]);Check(vm.State.Tracks.Single().ProviderTrackId=="remote-1"&&cloud.IsCloud,"Cloud ID persisted");
        Check(!cloud.HasSearchCover&&DataDirectoryService.Contains(storage.ArtworkFolder,cloud.CoverPath!)&&cloud.HasArtwork,"Added cover promoted out of temporary cache");
        Wait(vm.RefreshProviderAsync(plugin));Check(vm.State.Tracks.Count==1&&playlist.TrackIds.Contains(cloud.Id),"Refresh cannot erase searched/cloud references");
        var catalog=Wait(vm.Plugins.ReadMusicCatalogAsync(plugin,"fixture-catalog"));Check(catalog.Count==1&&catalog[0].Id==cloud.Id,"Generic catalog identity");
        vm.Save();Check(storage.Load().Tracks.Single().ProviderId==plugin.Id,"SQLite roundtrip");
        Wait(vm.SearchMusicAsync("","Cloud fixture"));Check(vm.Page=="songs"&&vm.VisibleTracks.Count==1,"Local collection search uses imported references");
        vm.Navigate("library");Pump();using(var bitmap=new RenderTargetBitmap(new PixelSize(1200,800))) { bitmap.Render(window);bitmap.Save(Path.Combine(root,"music-search.png"),PngBitmapEncoderOptions.Default); }
        vm.DisablePlugin(plugin);vm.Navigate("library");Pump();Check(window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="MusicSearchSource").Items.Count==1,"Disabled source removed");
        Check(vm.State.Tracks.Count==1&&playlist.TrackIds.Count==1,"Disabling preserves playlist references");
        RunPaged(root,fixture,vm,window);
        window.Close();Pump();Console.WriteLine("PASS generic music search registration/UI, cloud playlist references/SQLite, refresh, catalog, local filter and disable; "+root);
    }
    /// <summary>六千条元数据只创建视口附近的控件，滚动追加不重建已显示的数据和页面。</summary>
    private static void RunPaged(string root,string fixture,MainViewModel vm,MainWindow window)
    {
        var manifest=new PluginManifest { Id="fixture.paged-search",Name="Paged source",Version="1.0.0",ContractVersion=2,Type="extension",Runtime="process",PageEntry="page.json",Permissions=["network","process","navigation"],ProvidedServices=["media-source"],MusicSearch=new() { Label="Paged",DeferredArtwork=true },RequiredCapabilities=["media.search.v1","media.artwork-batch.v1"],EntryPoints=new() { ["win-x64"]="bin/NonetMusicPlayer.ExtensionFixture.exe" } };
        var package=Path.Combine(root,"paged.impp");using(var zip=ZipFile.Open(package,ZipArchiveMode.Create))
        {
            using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(JsonSerializer.Serialize(manifest,AppStorage.Json));
            using(var writer=new StreamWriter(zip.CreateEntry("page.json").Open()))writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"Fixture"}}""");
            foreach(var file in Directory.GetFiles(fixture).Where(f=>!f.EndsWith(".pdb")))zip.CreateEntryFromFile(file,"bin/"+Path.GetFileName(file));
        }
        var plugin=vm.Plugins.Install(package);vm.Plugins.SetEnabled(plugin,true);Wait(vm.Plugins.Extension(plugin).StartAsync());
        var loading=vm.SearchMusicAsync(plugin.Id,"slow");Check(vm.Page=="cloud-search"&&vm.ShowMusicSearchSpinner,"Navigate and spinner before network completes");
        Wait(loading);Check(vm.VisibleTracks.Count==30&&!vm.ShowMusicSearchSpinner,"First 30 metadata results");
        Wait(vm.LoadMusicSearchArtworkAsync(vm.VisibleTracks.Take(5)));Check(vm.VisibleTracks.Take(5).All(t=>t.HasArtwork),"Deferred cover batch");
        var first=vm.VisibleTracks[0];Pump();var list=window.GetVisualDescendants().OfType<ListBox>().Single(l=>l.Name=="TracksList");
        var scroll=list.GetVisualDescendants().OfType<ScrollViewer>().First();scroll.Offset=new(0,scroll.Extent.Height);
        var deadline=DateTime.UtcNow.AddSeconds(8);while(vm.VisibleTracks.Count<60&&DateTime.UtcNow<deadline)Pump();
        Check(vm.VisibleTracks.Count==60&&ReferenceEquals(first,vm.VisibleTracks[0]),$"Bottom auto-load appends 30 without reset: count={vm.VisibleTracks.Count}, extent={scroll.Extent}, viewport={scroll.Viewport}, offset={scroll.Offset}, list={list.Bounds}, page={vm.Page}, busy={vm.MusicSearchBusy}, more={vm.HasMoreMusicResults}");
        while(vm.HasMoreMusicResults)Wait(vm.LoadMoreMusicResultsAsync());Pump();
        Check(vm.VisibleTracks.Count==6000&&vm.VisibleTracks.Select(t=>t.Id).Distinct().Count()==6000,"All pages loaded and deduplicated");
        var realized=list.GetVisualDescendants().OfType<ListBoxItem>().Count();Check(realized is >0 and <100,"Virtualized 6000-song rows: "+realized);
        using(var bitmap=new RenderTargetBitmap(new PixelSize(1200,800))) { bitmap.Render(window);bitmap.Save(Path.Combine(root,"paged-search.png"),PngBitmapEncoderOptions.Default); }
        var again=vm.SearchMusicAsync(plugin.Id,"slow");vm.Navigate("settings");Wait(again);Check(vm.Page=="settings"&&!vm.MusicSearchBusy,"Late response cannot steal navigation");
        vm.Navigate("library");Pump();var editor=window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="HomeMusicSearch");editor.Text="query";vm.Navigate("history");vm.Navigate("library");Pump();
        Check(window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Name=="HomeMusicSearch").Text=="","Leaving Music clears query");
        vm.DisablePlugin(plugin);Console.WriteLine("PASS immediate spinner, bottom auto-load, deferred artwork, 6000 results / "+realized+" realized rows, navigation cancellation and query clearing");
    }
    private static T Wait<T>(Task<T> task) { while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}task.GetAwaiter().GetResult(); }
    private static void Pump()
    {
        // 定时器需要真实 Dispatcher 消息循环，仅 RunJobs 不会推进无头平台的计时调度。
        using var stop=new CancellationTokenSource(100);Dispatcher.UIThread.MainLoop(stop.Token);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private sealed class FakeAudio : NonetMusicPlayer.Core.Audio.IAudioPlayer
    {
        public bool IsAvailable=>true; public bool IsPlaying=>false; public TimeSpan Position {get;set;}public TimeSpan Duration=>TimeSpan.Zero;public float Volume {get;set;}
        public event EventHandler? PlaybackStopped {add{}remove{}}public Task LoadAsync(string source,CancellationToken token=default)=>Task.CompletedTask;public void Play(){}public void Pause(){}public void Stop(){}public void Dispose(){}
    }
}
