using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class NavigationAndLocalizationChecks
{
    internal static void Run(MainWindow window, MainViewModel vm, string output)
    {
        var page = vm.Page; var width = window.Width; var height = window.Height; var touch = vm.Settings.TouchMode;
        try
        {
            vm.Navigate("songs"); Pump(window);
            Require(window.GetVisualDescendants().OfType<Button>().All(b => b.Tag?.ToString() != "help"), "Help navigation removed");
            foreach (var (language, title) in new[] { ("en-US", "Tracks"), ("ja-JP", "曲"), ("zh-CN", "歌曲") })
            {
                vm.Settings.Language = language; L10n.SetLanguage(language); Pump(window);
                Require(vm.PageTitle == title, "Live page title translation: " + vm.PageTitle);
                Require(window.FindControl<TextBox>("LibrarySearch")!.PlaceholderText == L10n.T("Library.SearchTracksArtistsAlbums"), "Live XAML language resource");
                Require(vm.State.Tracks.Any(t => t.Title.Contains("夜空")), "Language changes never rewrite metadata");
                Capture(window, output, "language-" + language);
            }
            var one = vm.State.Tracks[0]; var two = vm.State.Tracks[1]; var three = vm.State.Tracks[2];
            var playlist = vm.CreatePlaylist("Drag ordering fixture"); vm.AddToPlaylist(playlist, [one, two, three]);
            vm.ReorderPlaylistTracks(playlist, [three.Id], one.Id, true);
            Require(playlist.TrackIds.SequenceEqual(new[] { three.Id, one.Id, two.Id }), "Drop before preserves remaining song order");
            vm.ReorderPlaylistTracks(playlist, [three.Id, one.Id], two.Id, false);
            Require(playlist.TrackIds.SequenceEqual(new[] { two.Id, three.Id, one.Id }), "Multi-track drop preserves relative manual order");
            vm.UndoCommand.Execute(null); Require(playlist.TrackIds[0] == three.Id, "Track reorder undo");
            vm.ReorderPlaylists(playlist.Id, vm.LikedPlaylist.Id, true);
            Require(vm.Playlists[0] == playlist && vm.State.Playlists[0] == playlist, "Sidebar playlist order synchronized");
            vm.Save(); Require(vm.Storage.Load().Playlists[0].Id == playlist.Id, "Sidebar order survives restart");
            vm.UndoCommand.Execute(null);
            vm.Navigate("playlist:" + playlist.Id); Pump(window); Capture(window, output, "playlist-header-beta5");
            Require(window.FindControl<ContentControl>("PlaylistHeader")!.GetVisualDescendants().OfType<Border>().Any(b => b.Width >= 112 && b.Height == b.Width), "Large playlist cover");
            var originalPage = vm.Page; var cover = window.FindControl<Button>("PlayerArtwork")!;
            cover.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
            Require(vm.Page == "lyrics" && window.FindControl<ContentControl>("FullLyricsHost")!.IsVisible && !window.FindControl<Grid>("PageContentGrid")!.IsVisible, "Full-width lyrics replaces sidebar content");
            cover.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window); Require(vm.Page == originalPage, "Cover toggles back to previous page");
            foreach (var groupPage in new[] { "albums", "artists" })
            {
                vm.Navigate(groupPage); Pump(window); Capture(window, output, groupPage + "-cards");
                var cards = window.FindControl<ContentControl>("AlternatePage")!.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single();
                Require(cards is not null && cards.RealizedCount > 0, "Independent album/artist cards are viewport-virtualized");
            }
            vm.Navigate("songs");
            foreach (var size in new[] { (640d,480d), (800d,600d), (980d,660d), (1366d,768d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; vm.Settings.TouchMode = size.Item1 <= 800; vm.ApplySettings(); Pump(window);
                var surface = window.FindControl<Border>("PlayerSurface")!; var play = window.FindControl<Button>("PlayerPlay")!; var progress = window.FindControl<Slider>("PlaybackSlider")!;
                Require(play.TranslatePoint(new Point(play.Bounds.Width / 2, 0), surface) is { } point && Math.Abs(point.X - surface.Bounds.Width / 2) < 2, "Player centered at " + size);
                Require(progress.Bounds.Width >= surface.Bounds.Width, "Edge-to-edge player progress");
                Require(play.Bounds.Width >= 44 && play.Bounds.Height >= 44, "Touch transport target");
                Capture(window, output, "responsive-" + size.Item1 + "x" + size.Item2);
            }
            window.Width = 640; window.Height = 480; vm.Settings.TouchMode = true; vm.ApplySettings();
            vm.Navigate("playlist:" + playlist.Id); Pump(window);
            Require(window.FindControl<ContentControl>("PlaylistHeader")!.Content is Expander { IsExpanded: false }, "Low-height playlist metadata collapses");
            Require(window.FindControl<ListBox>("TracksList")!.Bounds.Height >= 72, "Low-height playlist keeps a usable song viewport");
            Require(window.FindControl<Button>("LibraryAddFiles")!.IsVisible && window.FindControl<Button>("LibraryAddFolder")!.IsVisible, "Playlist keeps compact icon-only import actions");
            Capture(window, output, "playlist-640x480");
            vm.Navigate("settings"); Pump(window);
            var help = window.FindControl<Button>("UserManualButton")!; Require(help.IsVisible, "Manual beside settings heading");
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window); Require(window.OwnedWindows.OfType<UserManualWindow>().Any(w => w.IsVisible), "Offline native manual opens");
            foreach (var manual in window.OwnedWindows.OfType<UserManualWindow>().ToArray()) manual.Close();
            vm.DeletePlaylist(playlist);
            Console.WriteLine("PASS BETA5: live zh/en/ja, metadata, native manual, album/artist cards, playlist drag order/undo/persistence, full lyrics toggle, low-resolution/touch player");
        }
        finally { vm.Settings.Language = "zh-CN"; L10n.SetLanguage("zh-CN"); vm.Settings.TouchMode = touch; window.Width = width; window.Height = height; vm.ApplySettings(); vm.Navigate(page); Pump(window); }
    }
    private static void Capture(Window window, string output, string name) { using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
