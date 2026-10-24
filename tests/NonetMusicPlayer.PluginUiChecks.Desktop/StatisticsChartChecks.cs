using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>隔离数据验证滚动时间窗和原版/外观方案表现，不读取真实聆听记录。</summary>
internal static class StatisticsChartChecks
{
    public static void Run(string output, string? presetFile)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Directory.CreateDirectory(output);
        var now = new DateTimeOffset(2026, 10, 24, 15, 30, 0, TimeSpan.FromHours(8)); var hour = now.ToUnixTimeSeconds() / 3600;
        var source = new AppState { ListeningEntries = [new() { TrackId = "fixture", Date = "2026-10-18", Seconds = 60 }, new() { TrackId = "fixture", Date = "2026-10-24", Seconds = 300 }, new() { TrackId = "fixture", Date = "2026-10-17", Seconds = 9999 }],
            HourlyListeningEntries = [new() { UtcHour = hour - 24, PlayCount = 9 }, new() { UtcHour = hour - 23, PlayCount = 1 }, new() { UtcHour = hour, PlayCount = 2 }, new() { UtcHour = hour + 1, PlayCount = 9 }] };
        var recent = RecentListeningCharts.Build(source.ListeningEntries, source.HourlyListeningEntries, now);
        Check(recent.Days.Count == 7 && recent.Days[0].Label == "10/18" && recent.Days.Sum(p => p.Value) == 360, "Seven-day window excludes older records and fills zero days.");
        Check(recent.Hours.Count == 24 && recent.Hours.Sum(p => p.Value) == 3 && recent.Hours[^1].Label == "15", "Hourly window excludes future/expired buckets and uses local labels.");
        Check(RecentListeningCharts.Build(source.ListeningEntries, [], now).Hours.All(p => p.Value == 0), "Daily legacy data is never invented as hourly activity.");
        source.Tracks = Enumerable.Range(0, 80).Select(i => new TrackItem("fixture-" + i, "Song " + i, "Artist " + i, "Album " + i, "fixture.wav", ".wav", 0)).ToList();
        source.Playlists = [new() { Id = "fixture-playlist", Name = "Fixture", TrackIds = source.Tracks.Select(t => t.Id).ToList() }];
        var storage = new AppStorage(Path.Combine(Path.GetFullPath(output), "data-" + Guid.NewGuid().ToString("N"))); storage.Save(source);
        Check(storage.Load().HourlyListeningEntries.Count == 4, "Optional hourly metadata round trips through SQLite.");
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio(), new FixedClock(now)); vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { Width = 1360, Height = 860, DataContext = vm }; window.Show(); vm.Navigate("statistics"); Pump();
        CheckCharts("default");
        CheckGroups("default"); vm.Navigate("statistics"); Pump();
        if (presetFile is not null)
        {
            using var file = File.OpenRead(presetFile); var sheet = StyleSheetContract.Read(file);
            window.StyleLayers.Apply("fixture.charts", [sheet]); window.ApplyInterfaceLayers("fixture.charts", [sheet]); Pump();
            CheckCharts("clear-blue");
            var slider = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PlaybackSlider");
            var previous = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PlayerPrevious");
            Check(previous.TranslatePoint(default, window)!.Value.Y > slider.TranslatePoint(default, window)!.Value.Y + slider.Bounds.Height, "Clear Blue transport row is below progress row.");
            var card = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "StatisticsWeekCard");
            Check(card.BorderThickness == new Thickness(0), "Chart cards use color blocks without borders.");
            CheckGroups("clear-blue");
            foreach (var theme in new[] { "Light", "Dark" })
                foreach (var color in new[] { "#D8458C", "#35A875" })
                {
                    vm.Settings.Theme = theme; vm.Settings.Accent = color; vm.ApplySettings(); vm.Navigate("statistics"); Pump();
                    Check(((ISolidColorBrush)Application.Current!.Resources["AccentBrush"]!).Color == Color.Parse(color), "Preset preserves configured theme color.");
                    Check(((ISolidColorBrush)window.GetVisualDescendants().OfType<ListeningBarChart>().Single(c => c.Name == "StatisticsHourChart").Fill!).Color == Color.Parse(color), "Chart reacts to theme color without restarting the preset.");
                }
            vm.Navigate("statistics"); vm.Settings.Theme = "Dark"; vm.ApplySettings(); Pump();
            Check(Application.Current!.ActualThemeVariant == ThemeVariant.Dark, "Dark theme is actually active."); CheckCharts("clear-blue-dark");
            window.Width = 720; window.Height = 850; Pump(); CheckCharts("clear-blue-narrow");
        }
        window.Close(); Console.WriteLine("PASS bounded chart data, SQLite, baseline/preset/dark/narrow charts, swapped player rows and classification headers.");
        void CheckGroups(string name)
        {
            foreach (var page in new[] { "albums", "artists" })
            {
                vm.Navigate(page); Pump();
                Check(!window.GetVisualDescendants().OfType<Button>().Any(c => (c.Name is "GroupsBack" or "GroupsBackToTop") && c.IsEffectivelyVisible), "Legacy classification action row is invisible.");
                var heading = window.FindControl<Grid>("PageHeading")!;
                var label = window.FindControl<StackPanel>("PageHeadingLabel")!;
                var top = window.FindControl<Button>("PageBackToTop")!;
                Check(top.IsEffectivelyVisible && top.GetVisualAncestors().Contains(heading), "Back-to-top belongs to the title row.");
                var position = top.TranslatePoint(default, heading)!.Value;
                Check(Math.Abs(position.X + top.Bounds.Width - heading.Bounds.Width) < 1, "Back-to-top is at the right edge of the title row.");
                Check(Math.Abs(top.TranslatePoint(new Point(0, top.Bounds.Height / 2), label)!.Value.Y - label.Bounds.Height / 2) < 1, "Title and action are vertically centered.");
                var gallery = window.GetVisualDescendants().OfType<VirtualizedCardGrid>().Single(); gallery.Offset = new(0, 500); Pump();
                Check(gallery.Offset.Y > 0, "Classification gallery scrolls.");
                top.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent)); Pump(); Check(gallery.Offset.Y == 0, "Title action returns gallery to top.");
                using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(output, name + "-" + page + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        void CheckCharts(string name)
        {
            var plots = window.GetVisualDescendants().OfType<ListeningBarChart>().ToArray(); Check(plots.Length == 2, "Two plots are realized.");
            var date = window.GetVisualDescendants().OfType<StatisticsDatePicker>().Single();
            Check(plots.All(p => p.TranslatePoint(new Point(0, p.Bounds.Height), date)!.Value.Y < 0), "Charts precede date selector.");
            Check(plots.All(p => p.Bounds.Width > 200), "Charts remain readable at current width.");
            using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Fixture+08", now.Offset, "Fixture", "Fixture");
    }
    private static void Pump() { for (var i = 0; i < 30; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(3); } }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(180); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } } public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
