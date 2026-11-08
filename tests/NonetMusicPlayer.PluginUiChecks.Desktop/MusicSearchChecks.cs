using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        // 按实际插件中心操作路径切页，避免重复导航被有意忽略后检查旧首页快照。
        vm.Navigate("plugins");var plugin=vm.Plugins.Install(package);vm.Plugins.SetEnabled(plugin,true);vm.Navigate("library");Pump();
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
        var selector=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="MusicSearchSource");Check(selector.SelectedIndex==0&&selector.Items.Count==2,$"Local default and optional source: selected={selector.SelectedIndex}, items={selector.Items.Count}, enabled={plugin.Enabled}, sources={vm.Plugins.MusicSearchSources.Count}");
        Wait(vm.SearchMusicAsync(plugin.Id,"fixture"));Check(vm.Page=="cloud-search"&&vm.VisibleTracks.Count==1&&vm.State.Tracks.Count==0,"Search without automatic import");
        var cloud=vm.VisibleTracks.Single();Check(cloud.HasArtwork&&cloud.HasSearchCover&&File.Exists(cloud.CoverPath),"Search preview has a cover before playback");
        var playlist=vm.CreatePlaylist("Mixed source");vm.AddToPlaylist(playlist,[cloud]);Check(vm.State.Tracks.Single().ProviderTrackId=="remote-1"&&cloud.IsCloud,"Cloud ID persisted");
        Check(!cloud.HasSearchCover&&DataDirectoryService.Contains(storage.ArtworkFolder,cloud.CoverPath!)&&cloud.HasArtwork,"Added cover promoted out of temporary cache");
        Wait(vm.RefreshProviderAsync(plugin));Check(vm.State.Tracks.Count==1&&playlist.TrackIds.Contains(cloud.Id),"Refresh cannot erase searched/cloud references");
        var catalog=Wait(vm.Plugins.ReadMusicCatalogAsync(plugin,"fixture-catalog"));Check(catalog.Count==1&&catalog[0].Id==cloud.Id,"Generic catalog identity");
        vm.Save();Check(storage.Load().Tracks.Single().ProviderId==plugin.Id,"SQLite roundtrip");
        Wait(vm.SearchMusicAsync("","Cloud fixture"));Check(vm.Page=="songs"&&vm.VisibleTracks.Count==1,"Local collection search uses imported references");
        vm.Navigate("library");Pump();using(var bitmap=new RenderTargetBitmap(new PixelSize(1200,800))) { bitmap.Render(window);bitmap.Save(Path.Combine(root,"music-search.png"),PngBitmapEncoderOptions.Default); }
        vm.Navigate("plugins");vm.DisablePlugin(plugin);vm.Navigate("library");Pump();Check(window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="MusicSearchSource").Items.Count==1,"Disabled source removed");
        Check(vm.State.Tracks.Count==1&&playlist.TrackIds.Count==1,"Disabling preserves playlist references");
        RunHeaderQueries(root,fixture,vm,window);
        RunPaged(root,fixture,vm,window);
        window.Close();Pump();Console.WriteLine("PASS generic music search registration/UI, cloud playlist references/SQLite, refresh, catalog, local filter and disable; "+root);
    }
    /// <summary>三个通用音源同时启用，经真实右上搜索框提交新词，覆盖结果替换、空结果和迟到响应。</summary>
    private static void RunHeaderQueries(string root, string fixture, MainViewModel vm, MainWindow window)
    {
        vm.Navigate("plugins"); Pump();
        var sources = Enumerable.Range(1, 3).Select(index =>
        {
            var manifest = new PluginManifest { Id="fixture.header-search-"+index,Name="Header source "+index,Version="1.0.0",ContractVersion=2,Type="extension",Runtime="process",PageEntry="page.json",
                Permissions=["network","process","navigation"],ProvidedServices=["media-source"],MusicSearch=new() {Label="Header source "+index},RequiredCapabilities=["media.search.v1"],EntryPoints=new() {["win-x64"]="bin/NonetMusicPlayer.ExtensionFixture.exe"} };
            var path = Path.Combine(root,"header-"+index+".impp");
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
            {
                using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest,AppStorage.Json));
                using(var writer=new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"Header fixture"}}""");
                foreach(var file in Directory.GetFiles(fixture).Where(f=>!f.EndsWith(".pdb"))) zip.CreateEntryFromFile(file,"bin/"+Path.GetFileName(file));
            }
            var plugin=vm.Plugins.Install(path); vm.Plugins.SetEnabled(plugin,true); Wait(vm.Plugins.Extension(plugin).StartAsync()); return plugin;
        }).ToArray();
        vm.Navigate("library"); Pump();
        void Submit(PluginManifest source, string query, bool enter)
        {
            var selector=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="MusicSearchSource");
            selector.SelectedIndex=selector.Items.Cast<string>().ToList().IndexOf(source.MusicSearch!.Label);
            var editor=window.GetVisualDescendants().OfType<TextBox>().Single(c=>c.Name=="HomeMusicSearch"); editor.Text=query;
            if(enter) {editor.Focus(); window.KeyPress(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);window.KeyRelease(Key.Enter,RawInputModifiers.None,PhysicalKey.Enter,null);}
            else window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="HomeMusicSearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var limit=DateTime.UtcNow.AddSeconds(10);
            while((vm.MusicSearchBusy||vm.LastMusicSearchQuery!=query||vm.LastMusicSearchSource!=source.Id)&&DateTime.UtcNow<limit) Pump();
            Check(!vm.MusicSearchBusy&&vm.LastMusicSearchQuery==query&&vm.LastMusicSearchSource==source.Id,$"Header search completed current query and source: expected={source.Id}/{query}; actual={vm.LastMusicSearchSource}/{vm.LastMusicSearchQuery}, page={vm.Page}, busy={vm.MusicSearchBusy}, status={vm.StatusText}, index={selector.SelectedIndex}");
        }
        foreach(var source in sources)
        {
            Submit(source,"header:first",false); Check(vm.VisibleTracks.Count==1&&vm.VisibleTracks[0].Title=="header:first"&&vm.VisibleTracks[0].ProviderId==source.Id,"Source switch replaces prior provider results");
            var rows=window.FindControl<ListBox>("TracksList"); vm.SearchText="no matching local filter";
            Submit(source,"header:second",true);
            Check(vm.VisibleTracks.Count==1&&vm.VisibleTracks[0].Title=="header:second"&&vm.SearchText.Length==0,"Changed header keyword replaces results and resets old list filter");
            Check(ReferenceEquals(rows,window.FindControl<ListBox>("TracksList"))&&vm.PageTitle.Contains(source.MusicSearch!.Label),"Result refresh preserves page controls and updates source title");
            Submit(source,"header:empty",false); Check(vm.VisibleTracks.Count==0&&!vm.HasMoreMusicResults,"Empty new search does not retain previous results");
        }
        var slow=vm.SearchMusicAsync(sources[2].Id,"slow"); Check(vm.VisibleTracks.Count==0&&vm.MusicSearchBusy,"Old visible snapshot clears before repeated search finishes");
        Submit(sources[0],"header:latest",false); Wait(slow);
        Check(vm.VisibleTracks.Count==1&&vm.VisibleTracks[0].Title=="header:latest"&&vm.LastMusicSearchSource==sources[0].Id,"Late old-provider response cannot replace latest header results");
        Submit(sources[0],"header:pages",false); var first=vm.VisibleTracks[0]; Wait(vm.LoadMoreMusicResultsAsync());
        Check(vm.VisibleTracks.Count==2&&ReferenceEquals(first,vm.VisibleTracks[0]),"Pagination continues appending within the current query");
        foreach(var source in sources) vm.DisablePlugin(source);
        Console.WriteLine("PASS upper-right search button/Enter, three enabled sources, repeated keyword replacement, filter reset, empty results, latest-response guard and pagination");
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
