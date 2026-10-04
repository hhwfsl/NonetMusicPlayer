using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class AppearanceAndWorkflowChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        Require(new NonetMusicPlayer.Desktop.Models.AppSettings().BackgroundImageOpacity == .5, "New background default is fifty percent");
        Require(typeof(NonetMusicPlayer.Desktop.Models.AppSettings).GetProperty("DropAutoPlay") is null, "Drop behavior has no stored switch");
        vm.Navigate("settings"); Pump(owner);
        Require(!owner.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "拖入文件后播放"), "Removed drop setting");
        vm.Navigate("statistics"); Pump(owner);
        var picker = (StatisticsDatePicker)owner.GetVisualDescendants().OfType<StatisticsView>().Single().DateSelector;
        picker.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
        Require(picker.CalendarContent.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("StatisticsDay_") == true) == 42, "Calendar contains individual day targets");
        var frameRoot = (Control)picker.CalendarContent.Parent!;
        using (var image = new RenderTargetBitmap(new PixelSize(310, 380))) { image.Render(frameRoot); image.Save(Path.Combine(output, "beta11-calendar.png"), PngBitmapEncoderOptions.Default); }
        var dateButton = picker.CalendarContent.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("StatisticsDay_") == true);
        dateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner); Require(picker.SelectedDate is not null, "Calendar day selection applies");

        var fixture = vm.State.Tracks.First(); var playlist = vm.CreatePlaylist("Beta11 批量歌单"); vm.AddToPlaylist(playlist, vm.State.Tracks.Take(4)); vm.Navigate("playlist:" + playlist.Id); Pump(owner);
        owner.FindControl<Button>("BatchEntryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
        Require(owner.FindControl<TextBlock>("BatchEntryLabel")!.IsVisible && owner.FindControl<VectorIcon>("BatchEntryIcon")!.Kind == IconKind.Close, "Batch entry is now exit state");
        var selection = owner.FindControl<Grid>("SelectionToolbar")!; var entry = owner.FindControl<Button>("BatchEntryButton")!;
        Require(selection.Parent == owner.FindControl<Grid>("LibraryActions") && selection.TranslatePoint(default, owner)!.Value.X < entry.TranslatePoint(default, owner)!.Value.X, "Batch controls share toolbar on left");
        Require(Math.Abs(selection.TranslatePoint(new Point(0, selection.Bounds.Height / 2), owner)!.Value.Y - entry.TranslatePoint(new Point(0, entry.Bounds.Height / 2), owner)!.Value.Y) < 2, "Batch controls align on same row");
        var check = owner.FindControl<TrackListBox>("TracksList")!.GetVisualDescendants().OfType<CheckBox>().First(c => c.Classes.Contains("song-selector"));
        Require(check.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "SelectionBox" && b.Bounds.Width == 16), "Song selector glyph is sixteen pixels");
        foreach (var name in new[] { "ExecuteSearchButton", "ClearSearchButton" })
        {
            var button = owner.FindControl<Button>(name)!; var parent = (Control)button.Parent!;
            Require(button.Classes.Contains("search-icon") && button.Bounds.Right <= parent.Bounds.Width && button.Bounds.Bottom <= parent.Bounds.Height, "Search icon stays within field");
            var p = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), owner)!.Value; owner.MouseMove(p); Pump(owner);
            Require(!button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Any(c => c.Background is ISolidColorBrush b && b.Color.A > 0), "Search hover adds no background");
        }
        Capture(owner, output, "beta11-batch"); entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        vm.Settings.NavigationRight = true; vm.Settings.PlayerTop = true; vm.ApplySettings(); Pump(owner);
        Require(owner.FindControl<Border>("SidebarPanel")!.BorderThickness.Left == 1 && owner.FindControl<Border>("SidebarPanel")!.BorderThickness.Right == 0, "Right sidebar has visible inner divider");
        Require(owner.FindControl<Border>("PlayerResizeGrip")!.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Bottom, "Top player grip mirrored underneath");
        vm.Settings.NavigationRight = vm.Settings.PlayerTop = false; vm.ApplySettings(); Pump(owner);

        var source = Path.Combine(vm.Storage.Root, "fixture-cover.png"); using (var image = CoverCropService.Load(source))
        {
            foreach (var size in new[] { new Size(1360, 860), new Size(640, 480) })
            {
                owner.Width = size.Width; owner.Height = size.Height; Pump(owner);
                var dialog = new CoverCropDialog(owner, vm.Storage, image, true); dialog.Show(); Pump(dialog);
                try
                {
                    Require(!dialog.GetVisualDescendants().OfType<ScrollViewer>().Any(s => !s.GetVisualAncestors().Any(v => v is TextBox or ComboBox)), "Crop dialog form contains no scrolling regions (text editor internals excluded)");
                    var crop = dialog.GetVisualDescendants().OfType<CoverCropControl>().Single(); var parameters = dialog.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "CropParameters"); var save = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveCrop");
                    Require(crop.TranslatePoint(default, dialog)!.Value.X < parameters.TranslatePoint(default, dialog)!.Value.X && Equals(save.Content, L10n.T("Common.Save")), "Crop is left, parameters and Save right");
                    Require(save.TranslatePoint(new Point(save.Bounds.Width, save.Bounds.Height), dialog)!.Value.Y <= dialog.Bounds.Height, "Save remains fully visible at low resolution");
                    crop.Zoom(2); crop.SetCrop(0, 0, crop.Crop.Width); var path = CoverCropService.Export(vm.Storage, image, crop.Crop, true); using var result = new Bitmap(path);
                    Require(Math.Abs((double)result.PixelSize.Width / result.PixelSize.Height - size.Width / size.Height) < .03, "Background crop preserves chosen aspect ratio");
                    Capture(dialog, output, $"beta11-crop-{size.Width:0}");
                }
                finally { dialog.Close(); }
            }
        }
        owner.Width = 1360; owner.Height = 860; Pump(owner);

        vm.LyricLines.Clear();
        for (var i = 0; i < 20; i++) vm.LyricLines.Add(new(i * 5, i == 4 ? string.Concat(Enumerable.Repeat("这一句很长的歌词需要自然换行但仍然只是一个时间戳的句子。", 12)) : "歌词 " + i, i == 4 ? "Translation " + string.Concat(Enumerable.Repeat("must wrap together as one timed lyric. ", 12)) : "Translation " + i));
        vm.PlaybackPosition = 15; vm.Navigate("lyrics"); Pump(owner);
        var lyrics = (LyricsView)owner.FindControl<ContentControl>("FullLyricsHost")!.Content!; var list = lyrics.GetVisualDescendants().OfType<ListBox>().Single();
        Require(list.SelectedIndex == 3, "Current lyric tracks playback");
        var point = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), owner)!.Value;
        owner.MouseWheel(point, new Vector(0, -1)); Settle(owner, 750);
        Require(lyrics.IsPreviewing && list.SelectedIndex == 3, "Preview scroll does not move active highlight");
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(); var offset = scroll.Offset.Y;
        vm.PlaybackPosition = 16; Pump(owner); Require(list.SelectedIndex == 3 && Math.Abs(scroll.Offset.Y - offset) < 2, "Playback update preserves independent preview position");
        var longRow = list.GetVisualDescendants().OfType<Border>().First(b => b.Name == "LyricRow" && b.DataContext is LyricLine line && line.Seconds == 20);
        Require(longRow.Height > 112 && longRow.GetVisualDescendants().OfType<TextBlock>().All(t => t.MaxLines == 0), "Long original/translation wrap without truncation in one row");
        Capture(owner, output, "beta11-long-lyrics");
        VirtualizationAndOutputChecks.Settle(owner, 4450); Require(!lyrics.IsPreviewing && list.SelectedIndex == 3, $"Preview returns to playback after five idle seconds (preview={lyrics.IsPreviewing}, selected={list.SelectedIndex}, position={vm.PlaybackPosition})");
        vm.PlaybackPosition = 75; VirtualizationAndOutputChecks.Settle(owner, 800);
        var activeRow = (Control)list.ContainerFromIndex(15)!;
        Require(activeRow is not null && Math.Abs(activeRow.TranslatePoint(new Point(0, activeRow.Bounds.Height / 2), scroll)!.Value.Y - scroll.Viewport.Height / 2) < 4, "Playback after a wrapped lyric centers the actual active row");
        vm.Navigate("songs");
        vm.LyricLines.Clear(); vm.LyricLines.Add(new(0, "原文很长 " + new string('字', 50), "翻译"));
        using (var desktop = new DesktopLyricsWindow(owner, vm))
        {
            desktop.Toggle(); Pump(desktop);
            var lines = desktop.GetVisualDescendants().OfType<KaraokeLine>().ToArray(); Require(Math.Abs(lines[0].TextSize / lines[1].TextSize - 32d / 22) < .001, "Desktop original/translation share scale even for uneven lengths");
            desktop.Width = 600; desktop.Height = 240; Pump(desktop); Require(Math.Abs(lines[0].TextSize / lines[1].TextSize - 32d / 22) < .001, "Resized lyric scale ratio remains fixed");
            Require(!desktop.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "DesktopLyricsResize"), "Removed bottom-right combined grip"); desktop.HideLyrics();
        }
        var oldPackage = Path.Combine(output, "legacy.lmpkg"); File.WriteAllText(oldPackage, "invalid");
        var rejected = false; try { PluginManager.Inspect(oldPackage); } catch (InvalidDataException error) { rejected = error.Message.Contains(".impp"); }
        Require(rejected, "Non-impp extension rejected before archive processing");
        Console.WriteLine("PASS BETA11: themed monthly calendar, fixed drop contract, independent lyric preview and five-second follow, unlimited wrapping, side-by-side scrollbar-free background crop, compact batch toolbar, bounded search icons, mirrored dividers/grips, shared desktop lyric scale and impp-only import");
    }
    private static void Capture(Window window, string output, string name) { using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Settle(Window window, int milliseconds) { var end = Environment.TickCount64 + milliseconds; while (Environment.TickCount64 < end) { Pump(window); Thread.Sleep(15); } Pump(window); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Beta11: " + message); }
}
