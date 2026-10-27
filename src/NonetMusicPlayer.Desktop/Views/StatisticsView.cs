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
    private readonly ListeningBarChart _week = new() { Duration = true, Height = 190, Name = "StatisticsWeekChart" };
    private readonly ListeningBarChart _hours = new() { Duration = true, Height = 190, Name = "StatisticsHourChart" };
    private readonly TextBlock _weekTotal = Ui.RawText("", 13, true), _hourTotal = Ui.RawText("", 13, true);
    private readonly List<TextBlock> _metricValues = [];
    private readonly List<PeriodControls> _periodViews = [];
    public StatisticsDatePicker DateSelector { get; }
    public StatisticsView(MainViewModel vm)
    {
        _vm = vm;
        DateSelector = new StatisticsDatePicker { Name = "StatisticsDate", SelectedDate = DateTime.Today, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 190, Margin = new(0, 0, 24, 0) };
        DateSelector.SelectedDateChanged += (_, _) => Refresh();
        var charts = new ResponsiveUniformGrid { MinimumCellWidth = 360, MaximumColumns = 2 };
        _week.Bind(ListeningBarChart.FillProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentSecondaryBrush"));
        _hours.Bind(ListeningBarChart.FillProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentBrush"));
        foreach (var plot in new[] { _week, _hours }) plot.Bind(ListeningBarChart.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("TextSecondaryBrush"));
        var weekCard = Ui.Card(L10n.T("Statistics.RecentSevenDays"), _weekTotal, _week); weekCard.Name = "StatisticsWeekCard";
        var hourCard = Ui.Card(L10n.T("Statistics.RecentTwentyFourHoursDuration"), _hourTotal, _hours); hourCard.Name = "StatisticsHourCard";
        foreach (var card in new[] { weekCard, hourCard }) { card.Classes.Add("statistics-chart-card"); card.Margin = new(0, 0, 8, 8); charts.Children.Add(card); }
        Content = Ui.Scroll(Ui.Stack(_metrics, charts, Ui.Row(L10n.T("Statistics.StatisticsDate"), L10n.T("Statistics.ChooseADateToViewItsDayMonthAnd"), DateSelector), _periods));
        // 卡片与排行行只建立一次；计时刷新只更新文本，不重复构建整棵控件树。
        foreach (var label in new[] { "Statistics.ListeningTime", "Playback.Plays", "Statistics.ActiveDays", "Statistics.Today" })
        {
            var value = Ui.RawText("", 24); _metricValues.Add(value);
            var card = Ui.Card(L10n.T(label), value); card.Margin = new(0, 0, 8, 8); _metrics.Children.Add(card);
        }
        foreach (var (period, title, format) in new[] { ("day", "Statistics.DailyStatistics", "yyyy-MM-dd"), ("month", "Statistics.MonthlyStatistics", "yyyy-MM"), ("year", "Statistics.YearlyStatistics", "yyyy") })
        {
            var date = Ui.RawText("", 12, true);
            var total = CreateRank("Statistics.TotalListeningTime");
            var songTime = CreateRank("Statistics.SongWithMostListeningTime"); var songCount = CreateRank("Playback.MostPlayedSong");
            var artistTime = CreateRank("Statistics.ArtistWithMostListeningTime"); var artistCount = CreateRank("Playback.MostPlayedArtist");
            var card = Ui.Card(L10n.T(title), date, total.Root, songTime.Root, songCount.Root, artistTime.Root, artistCount.Root);
            card.Name = "Statistics_" + period; _periods.Children.Add(card);
            _periodViews.Add(new(period, format, date, total, songTime, songCount, artistTime, artistCount));
        }
        Refresh(); _timer.Tick += (_, _) => Refresh(); _timer.Start();
    }
    private void Refresh()
    {
        var statistics = _vm.GetStatistics();
        var recent = _vm.GetRecentListeningCharts(); _week.Update(recent.Days); _hours.Update(recent.HourDurations);
        _weekTotal.Text = L10n.Format("Statistics.ChartTotal", Time(recent.Days.Sum(p => p.Value)));
        _hourTotal.Text = L10n.Format("Statistics.ChartTotal", Time(recent.HourDurations.Sum(p => p.Value)));
        string[] values = [Time(statistics.TotalSeconds), statistics.TotalPlays.ToString(), statistics.ActiveDays.ToString(), Time(statistics.TodaySeconds)];
        for (var i = 0; i < values.Length; i++) _metricValues[i].Text = values[i];
        var date = DateOnly.FromDateTime(DateSelector.SelectedDate ?? DateTime.Today);
        foreach (var view in _periodViews)
        {
            var snapshot = _vm.GetStatistics(date, view.Period);
            view.Date.Text = date.ToString(view.Format); view.Total.Set(L10n.Format("Statistics.Plays", snapshot.TotalPlays), Time(snapshot.TotalSeconds));
            view.SongTime.SetBest(snapshot.Songs.FirstOrDefault(), false);
            view.SongCount.SetBest(snapshot.Songs.MaxBy(x => (x.PlayCount, x.Seconds)), true);
            view.ArtistTime.SetBest(snapshot.Artists.FirstOrDefault(), false);
            view.ArtistCount.SetBest(snapshot.Artists.MaxBy(x => (x.PlayCount, x.Seconds)), true);
        }
    }
    private static RankControls CreateRank(string label)
    {
        var detail = Ui.RawText("", 12, true); var labels = Ui.Stack(Ui.Text(L10n.T(label)), detail); labels.Spacing = 4;
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 6), ColumnSpacing = 16 }; grid.Children.Add(labels);
        var duration = Ui.RawText("", 12, true); duration.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(duration, 1); grid.Children.Add(duration); return new(grid, detail, duration);
    }
    private sealed record PeriodControls(string Period, string Format, TextBlock Date, RankControls Total, RankControls SongTime, RankControls SongCount, RankControls ArtistTime, RankControls ArtistCount);
    private sealed record RankControls(Grid Root, TextBlock Detail, TextBlock Value)
    {
        public void Set(string detail, string value) { Detail.Text = detail; Value.Text = value; }
        public void SetBest(ListeningRankItem? item, bool count) => Set(item is null ? L10n.T("Common.NoDataYet") : item.Name + (string.IsNullOrEmpty(item.Artist) ? "" : " — " + item.Artist), item is null ? "—" : count ? L10n.Format("Statistics.Plays", item.PlayCount) : Time(item.Seconds));
    }
    public static string Time(double seconds) => seconds < 60 ? string.Format(L10n.T("Common.S"), Math.Floor(seconds)) : seconds < 3600 ? string.Format(L10n.T("Common.Min"), seconds / 60) : string.Format(L10n.T("Common.H"), seconds / 3600);
    public void Dispose() => _timer.Stop();
}
