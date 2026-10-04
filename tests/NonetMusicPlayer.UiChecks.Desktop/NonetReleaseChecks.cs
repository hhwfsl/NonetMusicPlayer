using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>Nonet 首发的回顶、歌单收起和无译文歌词检查；仅操作隔离数据。</summary>
internal static class NonetReleaseChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        Require(owner.Title == "Nonet", "Desktop presents the Nonet product name");
        var toggle = owner.FindControl<Button>("PlaylistsCollapseButton")!;
        var navigation = owner.FindControl<StackPanel>("PlaylistNavigation")!;
        var create = owner.FindControl<Button>("PlaylistsCollapseButton")!.GetVisualAncestors().OfType<Grid>().First().Children.OfType<Button>().First();
        Require(toggle.TranslatePoint(default, owner)!.Value.X >= create.TranslatePoint(default, owner)!.Value.X + create.Bounds.Width, "Collapse is immediately right of new playlist");
        Click(toggle); Pump(owner);
        Require(!navigation.IsVisible && !vm.Settings.PlaylistsExpanded, "Playlists collapse without removal");
        vm.Save(); Require(!vm.Storage.Load().Settings.PlaylistsExpanded, "Collapsed state persists in SQLite");
        Click(toggle); Pump(owner); Require(navigation.IsVisible, "Playlists can expand again");

        vm.Navigate("settings"); Pump(owner);
        var settings = owner.GetVisualDescendants().OfType<SettingsView>().Single();
        var scroll = settings.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Extent.Height > s.Viewport.Height);
        scroll.Offset = new Vector(0, 400); Pump(owner);
        var settingsTop = settings.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "SettingsBackToTop");
        var search = settings.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "SettingsSearch");
        Require(settingsTop.Bounds.Left > search.Bounds.Right + 40, "Settings top button follows clear search");
        Click(settingsTop); Pump(owner); Require(scroll.Offset.Y == 0 && vm.Page == "settings", "Settings returns to top without navigation");

        var first = vm.State.Tracks.First();
        var list = vm.CreatePlaylist("top-button-fixture");
        var added = Enumerable.Range(0, 80).Select(i => new TrackItem(Guid.NewGuid().ToString("N"), "Fixture " + i, "Fixture artist", "Fixture album", first.FilePath, ".wav", 1)).ToArray();
        vm.State.Tracks.AddRange(added); vm.AddToPlaylist(list, added);
        foreach (var route in new[] { "playlist:" + list.Id, "songs", "album:Fixture album", "artist:Fixture artist", "history" })
        {
            if (route == "history") vm.State.History.AddRange(added.Select(t => t.Id));
            vm.Navigate(route); Pump(owner);
            var tracks = owner.FindControl<ListBox>("TracksList")!;
            // 与用户点击一致，先取消进入页面时尚未完成的自动定位请求。
            Click(owner.FindControl<Button>("TracksBackToTop")!);
            tracks.ScrollIntoView(vm.VisibleTracks.Last()); Pump(owner);
            var trackScroll = tracks.GetVisualDescendants().OfType<ScrollViewer>().First();
            Require(trackScroll.Offset.Y > 0, "Fixture scrolls: " + route);
            var top = owner.FindControl<Button>("TracksBackToTop")!; var batch = owner.FindControl<Button>("BatchEntryButton")!;
            Require(top.IsEffectivelyVisible && top.Content is VectorIcon && top.TranslatePoint(default, owner)!.Value.X >= batch.TranslatePoint(default, owner)!.Value.X + batch.Bounds.Width, "Track top button follows batch: " + route);
            Click(top); Pump(owner); Require(trackScroll.Offset.Y == 0 && vm.Page == route, "Track page returns to top: " + route);
        }
        vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, "Current lyric")); vm.LyricLines.Add(new(1000, "Next lyric"));
        using (var lyrics = new DesktopLyricsWindow(owner, vm))
        {
            lyrics.Toggle(); Pump(lyrics);
            var translation = lyrics.GetVisualDescendants().OfType<KaraokeLine>().Single(l => l.Name == "DesktopNextLyric");
            Require(!translation.IsVisible && translation.Text == "", "No translation never previews the next line");
            vm.LyricLines[0] = new(0, "Current lyric", "Current translation"); Pump(lyrics);
            Require(translation.IsVisible && translation.Text == "Current translation", "Translation shows only its current original line");
            lyrics.HideLyrics();
        }
        vm.Navigate("playlist:" + list.Id); Pump(owner);
        using (var frame = owner.CaptureRenderedFrame()) frame?.Save(System.IO.Path.Combine(output, "nonet-playlist-toolbar.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Console.WriteLine("PASS Nonet: branding, sidebar collapse persistence, back-to-top and current-line translation");
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(Window window) { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); } }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
