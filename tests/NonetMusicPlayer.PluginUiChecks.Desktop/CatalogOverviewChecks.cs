using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>验证真实导航访问计数、分类名称范围、工具栏几何及旧方案兼容，不访问用户资料库。</summary>
internal static class CatalogOverviewChecks
{
    public static void Run(string output, string? preset)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Directory.CreateDirectory(output);
        var storage = new AppStorage(Path.Combine(Path.GetFullPath(output), "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new Audio()); vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var lists = Enumerable.Range(0, 8).Select(i => vm.CreatePlaylist("Plan " + i)).ToArray();
        var tracks = Enumerable.Range(0, 150).Select(i => new TrackItem("fixture-" + i, "OnlySongTitle " + i, "Artist " + i % 20, "Album " + i % 30, "file-" + i, ".flac", 0)).ToArray();
        vm.AddToPlaylist(lists[0], tracks);
        foreach (var (index, count) in new[] { (0, 2), (1, 5), (2, 4), (3, 3) })
            for (var n = 0; n < count; n++) { vm.Navigate("library"); vm.Navigate("playlist:" + lists[index].Id); }
        var expected = new[] { lists[1], lists[2], lists[3], lists[0] }.Select(p => p.Id).ToArray();
        Check(vm.MostVisitedPlaylists().Select(p => p.Id).SequenceEqual(expected), "Most-visited ordering");
        var current = vm.Page; var before = vm.State.PlaylistVisits[lists[3].Id].Count; vm.Navigate(current); Check(vm.State.PlaylistVisits[lists[3].Id].Count == before, "No-op navigation must not count twice");
        Check(new AppStorage(storage.Root).Load().PlaylistVisits[lists[1].Id].Count == 5, "SQLite playback-only persistence includes visits");
        vm.Navigate("library");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 }; window.Show(); Pump();
        try
        {
            foreach (var styled in preset is null ? new[] { false } : new[] { false, true })
            {
                if (styled) { using var stream = File.OpenRead(preset!); window.ApplyInterfaceLayers("fixture.catalog", [StyleSheetContract.Read(stream)]); Pump(); }
                vm.Navigate("library"); Pump();
                var cards = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "HomePlaylistCard").ToArray();
                Check(cards.Select(b => b.Tag?.ToString()).SequenceEqual(expected.Select(id => "playlist:" + id)), "Home row-major frequency order");
                Check(cards[0].TranslatePoint(default, window)!.Value.X < cards[1].TranslatePoint(default, window)!.Value.X && cards[0].TranslatePoint(default, window)!.Value.Y < cards[2].TranslatePoint(default, window)!.Value.Y, "Top-left to bottom-right positions");
                foreach (var page in new[] { "albums", "artists", styled ? "interface:playlists" : "playlists" })
                {
                    vm.Navigate(page); Pump();
                    var field = window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "CatalogSearch");
                    Search("OnlySongTitle"); Check(Cards() == 0, "Catalog search must not match a song title: " + page);
                    Search(page == "albums" ? "Album 29" : page == "artists" ? "Artist 19" : "Plan 7"); Check(Cards() == 1, "Only matching entity names: " + page);
                    Check(ReferenceEquals(field, window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "CatalogSearch")), "Typing must not rebuild the search editor");
                    var clear = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CatalogSearchClear"); clear.RaiseEvent(new(Button.ClickEvent)); Pump(); Check(Cards() > 1, "Clear restores categories");
                    if (page.EndsWith("playlists"))
                    {
                        var create = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CatalogCreatePlaylist");
                        var top = window.FindControl<Button>("PageBackToTop")!;
                        Check(field.TranslatePoint(default, window)!.Value.X < create.TranslatePoint(default, window)!.Value.X && create.TranslatePoint(default, window)!.Value.X < top.TranslatePoint(default, window)!.Value.X, "Search, create, top order");
                        var scroll = window.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single(); scroll.Offset = new(0, 300); Pump(); top.RaiseEvent(new(Button.ClickEvent)); Pump(); Check(scroll.Offset.Y == 0, "Back to top retains page");
                    }
                    using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(output, (styled ? "preset-" : "default-") + page.Replace(':', '-') + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    void Search(string text) { field.Text = text; field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Pump(); }
                    int Cards() => window.GetVisualDescendants().OfType<Button>().Count(b => b.Name is "OpenMusicGroup" or "InterfacePlaylistCard");
                }
                vm.Navigate("settings"); Pump();
                var bar = window.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && b.IsEffectivelyVisible);
                var thumb = bar.GetVisualDescendants().OfType<Thumb>().First(); Check(thumb.CornerRadius.TopLeft >= 8 && thumb.Width == 6, "Rounded narrow thumb");
                using (var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"id":"fixture.scroll","name":"Scrollbar fixture","version":"1.0.0","rules":[{"type":"Thumb","scope":"nonet-scrollbar","durationMs":0,"setters":{"width":8}}]}""")))
                    window.StyleLayers.Apply("fixture.scroll", [StyleSheetContract.Read(input)]);
                Pump(); Check(thumb.Width == 8, "Scrollbar style scope supports overrides");
                window.StyleLayers.Remove("fixture.scroll"); Pump(); Check(thumb.Width == 6, "Removing a style restores the original scrollbar");
                foreach (var size in new[] { new Size(640,480), new Size(900,600), new Size(1280,800) })
                {window.Width=size.Width;window.Height=size.Height;vm.Navigate("artists");Pump();var header=window.FindControl<ContentControl>("CatalogSearchHeader")!; Check(header.Bounds.Width>100 && header.TranslatePoint(new(header.Bounds.Width,0),window)!.Value.X<=window.Bounds.Width, "Header fits narrow screen");vm.Navigate("library");Pump();}
            }
        }
        finally {window.Close();Pump();}
        Console.WriteLine("PASS frequency ranking and SQLite persistence, row-major dashboard, scoped searches, clear, toolbar order, scrollbars, narrow screens and baseline/preset.");
    }
    private static void Check(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    private static void Pump() {for(var i=0;i<20;i++) {Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Thread.Sleep(2);}}
    private sealed class Audio: IAudioPlayer
    {
        public bool IsAvailable=>true;public bool IsPlaying=>false;public TimeSpan Position{get;set;}public TimeSpan Duration=>TimeSpan.FromSeconds(100);public float Volume{get;set;}
        public event EventHandler? PlaybackStopped{add{}remove{}}public Task LoadAsync(string path,CancellationToken cancellationToken=default)=>Task.CompletedTask;public void Play(){}public void Pause(){}public void Stop(){}public void Dispose(){}
    }
}
