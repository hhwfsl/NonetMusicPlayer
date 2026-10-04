using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using System.Globalization;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Models;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace NonetMusicPlayer.Desktop.Views;

public sealed class StatisticsView : UserControl, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly ResponsiveUniformGrid _metrics = new() { MinimumCellWidth = 185, MaximumColumns = 4 };
    private readonly StackPanel _periods = Ui.Stack();
    public StatisticsDatePicker DateSelector { get; }
    public StatisticsView(MainViewModel vm)
    {
        _vm = vm;
        DateSelector = new StatisticsDatePicker { Name = "StatisticsDate", SelectedDate = DateTime.Today, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 190, Margin = new(0, 0, 24, 0) };
        DateSelector.SelectedDateChanged += (_, _) => Refresh();
        Content = Ui.Scroll(Ui.Stack(_metrics, Ui.Row(L10n.T("Statistics.StatisticsDate"), L10n.T("Statistics.ChooseADateToViewItsDayMonthAnd"), DateSelector), _periods));
        Refresh(); _timer.Tick += (_, _) => Refresh(); _timer.Start();
    }
    private void Refresh()
    {
        var statistics = _vm.GetStatistics();
        _metrics.Children.Clear();
        string[] labels = [L10n.T("Statistics.ListeningTime"), L10n.T("Playback.Plays"), L10n.T("Statistics.ActiveDays"), L10n.T("Statistics.Today")];
        string[] values = [Time(statistics.TotalSeconds), statistics.TotalPlays.ToString(), statistics.ActiveDays.ToString(), Time(statistics.TodaySeconds)];
        for (var i = 0; i < labels.Length; i++) { var card = Ui.Card(labels[i], Ui.Text(values[i], 24)); card.Margin = new(0, 0, 8, 8); _metrics.Children.Add(card); }
        _periods.Children.Clear();
        var date = DateOnly.FromDateTime(DateSelector.SelectedDate ?? DateTime.Today);
        foreach (var (period, title, format) in new[] { ("day", L10n.T("Statistics.DailyStatistics"), "yyyy-MM-dd"), ("month", L10n.T("Statistics.MonthlyStatistics"), "yyyy-MM"), ("year", L10n.T("Statistics.YearlyStatistics"), "yyyy") })
        {
            var snapshot = _vm.GetStatistics(date, period);
            var card = Ui.Card(title, Ui.RawText(date.ToString(format), 12, true), Rank(L10n.T("Statistics.TotalListeningTime"), L10n.Format("Statistics.Plays", snapshot.TotalPlays), Time(snapshot.TotalSeconds)),
                Best(L10n.T("Statistics.SongWithMostListeningTime"), snapshot.Songs.FirstOrDefault(), false),
                Best(L10n.T("Playback.MostPlayedSong"), snapshot.Songs.OrderByDescending(x => x.PlayCount).ThenByDescending(x => x.Seconds).FirstOrDefault(), true),
                Best(L10n.T("Statistics.ArtistWithMostListeningTime"), snapshot.Artists.FirstOrDefault(), false),
                Best(L10n.T("Playback.MostPlayedArtist"), snapshot.Artists.OrderByDescending(x => x.PlayCount).ThenByDescending(x => x.Seconds).FirstOrDefault(), true));
            card.Name = "Statistics_" + period; _periods.Children.Add(card);
        }
    }
    private static Control Best(string label, ListeningRankItem? item, bool count) => Rank(label, item is null ? L10n.T("Common.NoDataYet") : item.Name + (string.IsNullOrEmpty(item.Artist) ? "" : " — " + item.Artist), item is null ? "—" : count ? L10n.Format("Statistics.Plays", item.PlayCount) : Time(item.Seconds));
    private static Grid Rank(string name, string detail, string value)
    {
        var labels = Ui.Stack(Ui.Text(name), Ui.RawText(detail, 12, true)); labels.Spacing = 4;
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 6), ColumnSpacing = 16 }; grid.Children.Add(labels);
        var duration = Ui.Text(value, 12, true); duration.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(duration, 1); grid.Children.Add(duration); return grid;
    }
    public static string Time(double seconds) => seconds < 60 ? string.Format(L10n.T("Common.S"), Math.Floor(seconds)) : seconds < 3600 ? string.Format(L10n.T("Common.Min"), seconds / 60) : string.Format(L10n.T("Common.H"), seconds / 3600);
    public void Dispose() => _timer.Stop();
}
