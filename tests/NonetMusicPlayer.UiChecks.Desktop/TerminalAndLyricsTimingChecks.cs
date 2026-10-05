using System.IO.Compression;
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
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class TerminalAndLyricsTimingChecks
{
    internal static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        var selected = vm.State.Tracks.First(); var mode = vm.Settings.PlayMode;
        var package = PluginTestFixtures.Timing(output);
        var plugin = vm.Plugins.Install(package); vm.Plugins.SetEnabled(plugin, true); vm.ApplySettings();
        Wait(owner, vm.PlayTrackAsync(selected, [selected])); vm.Navigate("lyrics"); Pump(owner);
        owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LyricsMore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
        var more = owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LyricsMore");
        var item = more.ContextMenu!.ItemsSource!.Cast<object>().OfType<MenuItem>().Single(m => m.Header?.ToString() == "精准歌词工具");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(owner);
        Require(vm.Page == "plugin:" + plugin.Id, "Generic lyric-menu contribution opens own page");
        var tool = owner.GetVisualDescendants().OfType<LyricsTimingControl>().Single();
        Require(tool.SelectedTrack?.Id == selected.Id, "Current song is selected by default");
        var songSelector = owner.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "TimingSong");
        var original = selected;
        selected = vm.State.Tracks.FirstOrDefault(t => t.Id != original.Id) ?? new TrackItem("timing-song-choice", "可选择的歌曲", original.Artist, original.Album, original.FilePath, original.Extension, original.FileSize) { DurationSeconds = original.DurationSeconds };
        if (!vm.State.Tracks.Contains(selected)) vm.State.Tracks.Add(selected);
        tool.SelectTrack(selected); Pump(owner);
        Require(songSelector.SelectedItem == selected && vm.CurrentTrack == original, "Song choice changes target without playing it prematurely");
        var lyricsFile = Path.Combine(output, "annotation-source.txt"); File.WriteAllText(lyricsFile, "第一行\n\n第二行\n第三行");
        var priorLyrics = vm.Lyrics.Read(selected.Id); tool.LoadFile(lyricsFile, selected); Wait(owner, tool.StartAsync()); Pump(owner);
        Require(tool.IsActive && vm.IsLyricsTimingActive && vm.IsPlaying && vm.AudioClockSeconds == 0 && vm.PlaybackQueue.Count == 1, "Start rewinds and reserves only selected song");
        var blocked = Wait(owner, owner.Commands.ExecuteAsync("nonet player next"));
        Require(!blocked.Success && vm.CurrentTrack == selected, "Next is rejected during annotation");
        Require(!Wait(owner, owner.Commands.ExecuteAsync("nonet player mode shuffle")).Success && vm.Settings.PlayMode == mode, "Playback mode cannot change");
        Require(!songSelector.IsEnabled, "Song selection locked during annotation");
        vm.Seek(2.345); owner.FindControl<Button>("PlayerMore")!.Focus(); Space(owner); Pump(owner);
        Require(vm.IsPlaying && tool.Session!.NextLine == 2 && tool.Session.Times[1] == 2.345, "Space timestamps instead of toggling playback");
        vm.Seek(4.567); owner.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "TimingLines").Focus();
        owner.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); owner.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); owner.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Pump(owner);
        Require(tool.Session!.Complete && vm.IsPlaying, "Final line marked with list focus; key repeat neither adds marks nor pauses playback");
        Wait(owner, vm.PlayRelativeAsync(1, automatic: true)); Require(!vm.IsPlaying && vm.CurrentTrack == selected && vm.IsLyricsTimingActive, "Natural end pauses without switching or looping");
        Wait(owner, vm.SetPlayingAsync(true)); Require(vm.IsPlaying && vm.AudioClockSeconds == 4.567, "Resume remains on reserved song and retained position");
        using (var frame = owner.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "lyrics-timing-plugin.png"), PngBitmapEncoderOptions.Default);
        tool.SaveCompleted(); Pump(owner);
        Require(!vm.IsLyricsTimingActive && !vm.IsPlaying && vm.Settings.PlayMode == mode && vm.Lyrics.Read(selected.Id).Contains("[00:04.567]第三行"), "Save associates LRC and releases playback lease");
        Require(File.ReadAllText(lyricsFile) == "第一行\n\n第二行\n第三行", "Source file unchanged");
        Require(songSelector.IsEnabled, "Song selection restored after annotation");
        var noConfigurationTask = owner.OpenPluginConfigurationAsync(plugin); Pump(owner);
        var noConfiguration = owner.GetVisualDescendants().OfType<PluginConfigView>().Single();
        Require(noConfiguration.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "无配置") && !noConfiguration.GetVisualDescendants().OfType<TextBox>().Any()
            && !noConfiguration.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SavePluginConfiguration").IsVisible, "Empty developer schema has no editable keys/values or save action");
        noConfiguration.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ClosePluginConfiguration").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
        Require(noConfigurationTask.IsCompletedSuccessfully && !noConfigurationTask.Result, "Closing no-config overlay does not modify plugin configuration");
        tool.LoadFile(lyricsFile, selected); Wait(owner, tool.StartAsync()); tool.Cancel();
        Require(!vm.IsLyricsTimingActive && vm.Lyrics.Read(selected.Id).Contains("[00:04.567]第三行"), "Cancel never overwrites saved lyrics");
        tool.LoadFile(lyricsFile, selected); Wait(owner, tool.StartAsync()); vm.Plugins.SetEnabled(plugin, false); Pump(owner);
        Require(!vm.IsLyricsTimingActive && !tool.IsActive, "Disabling plugin releases active lease immediately");
        vm.Plugins.SetEnabled(plugin, true); vm.Navigate("plugin:" + plugin.Id); Pump(owner);
        tool = owner.GetVisualDescendants().OfType<LyricsTimingControl>().Single(); tool.LoadFile(lyricsFile, selected); Wait(owner, tool.StartAsync()); vm.Navigate("library"); Pump(owner);
        Require(!vm.IsLyricsTimingActive, "Navigation cancels active session");
        var savedCover = selected.CoverPath; selected.CoverPath = null; selected.ReleaseArtwork();
        Wait(owner, vm.PlayTrackAsync(original, [original])); Wait(owner, vm.PlayTrackAsync(selected, [selected])); vm.Navigate("songs"); Pump(owner);
        var playerPlaceholder = owner.FindControl<Button>("PlayerArtwork")!.GetVisualDescendants().OfType<VectorIcon>().Single(i => i.Kind == IconKind.Music);
        var artworkButton = owner.FindControl<Button>("PlayerArtwork")!;
        Require(artworkButton.Content is Border placeholderFrame && placeholderFrame.Bounds.Width >= artworkButton.Bounds.Width - 2 && placeholderFrame.Bounds.Height >= artworkButton.Bounds.Height - 2,
            "Missing artwork keeps the complete player cover area instead of collapsing to an icon-sized circle");
        var rowPlaceholder = owner.FindControl<ListBox>("TracksList")!.GetVisualDescendants().OfType<VectorIcon>().First(i => i.Kind == IconKind.Music);
        Require(playerPlaceholder.Width == rowPlaceholder.Width && playerPlaceholder.Height == rowPlaceholder.Height
            && ((ISolidColorBrush)playerPlaceholder.Brush!).Color == ((ISolidColorBrush)rowPlaceholder.Brush!).Color, "Player and track row share the same no-artwork music icon style");
        selected.CoverPath = savedCover; selected.ReleaseArtwork(); vm.Navigate("library"); Pump(owner);
        var home = owner.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "MusicHome"); Require(home.Margin.Right >= 18, "Home content has scrollbar gutter");
        vm.Settings.FontFamily = "Consolas"; vm.Settings.FontSize = 16; vm.ApplySettings(); Pump(owner);
        var button = owner.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "查看全部");
        ToolTip.SetIsOpen(button, true); Pump(owner);
        // Popup 不在主窗口视觉树内，已打开的 Tip 对象通过附加属性引用访问。
        if (button.GetValue(Avalonia.Controls.Diagnostics.ToolTipDiagnostics.ToolTipProperty) is ToolTip tooltip)
        {
            Require(tooltip.FontFamily.Name.Contains("Consolas") && tooltip.FontSize == 16, "Popup tooltip uses selected font and font size");
            Require(((ISolidColorBrush)tooltip.Background!).Color.A == 255 && TextOptions.GetTextRenderingMode(tooltip) == TextRenderingMode.Antialias, "Tooltip background is opaque with grayscale text rendering");
        }
        else throw new InvalidOperationException("Opened tooltip instance was not accessible");
        ToolTip.SetIsOpen(button, false);
        vm.Navigate("settings"); Pump(owner);
        var rows = owner.GetVisualDescendants().OfType<NumericUpDown>().Where(c => c.Name == "TerminalScrollbackLines").ToArray(); Require(rows.Length == 1 && rows[0].Maximum == 10000 && rows[0].Value == 1000, "Dedicated terminal settings default and upper bound");
        vm.Settings.TerminalFontSize = 21; vm.ApplySettings(); vm.Navigate("terminal"); Pump(owner);
        Require(owner.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "TerminalOutput").FontSize == 21, "Terminal font size applies immediately");
        var help = Wait(owner, owner.Commands.ExecuteAsync("nonet help")); Require(help.Message.Contains("nonet window maximize") && !help.Message.Contains("--data <") && !help.Message.Contains("仅桌面"), "Desktop help exposes host commands without notes or CLI launcher flags");
        vm.Plugins.Uninstall(plugin); vm.Lyrics.Save(selected.Id, priorLyrics); vm.ReloadLyrics(); vm.Settings.FontFamily = ""; vm.Settings.FontSize = 13; vm.ApplySettings();
        Console.WriteLine("PASS TIMING UI: song selection/default/lock, window-routed Space with player/list focus and repeat guard, fixed/no-config schema, playback restriction and save/cancel/unload/navigation.");
    }
    private static void Space(Window window) { window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); }
    private static T Wait<T>(Window window, Task<T> task) { Wait(window, (Task)task); return task.GetAwaiter().GetResult(); }
    private static void Wait(Window window, Task task) { var until = Environment.TickCount64 + 10000; while (!task.IsCompleted && Environment.TickCount64 < until) { Pump(window); Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
