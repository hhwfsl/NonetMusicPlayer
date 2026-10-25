using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>复用统计页的有界 24 小时数据，仅在主页挂接期间刷新；重排不销毁图表。</summary>
internal sealed class HomeListeningChart : UserControl, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly ListeningBarChart _chart = new() { Name = "HomeHourChart", Duration = true };
    private readonly TextBlock _total = Ui.RawText("", 12, true);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _disposed;
    public HomeListeningChart(MainViewModel vm)
    {
        _vm = vm;
        _chart.Bind(ListeningBarChart.FillProperty, new DynamicResourceExtension("AccentBrush"));
        _chart.Bind(ListeningBarChart.ForegroundProperty, new DynamicResourceExtension("TextSecondaryBrush"));
        var panel = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 6 }; panel.Children.Add(_total); Grid.SetRow(_chart, 1); panel.Children.Add(_chart); Content = panel;
        _timer.Tick += Tick;
        AttachedToVisualTree += (_, _) => { if (!_disposed) { Refresh(); _timer.Start(); } };
        Refresh();
    }
    private void Tick(object? sender, EventArgs e) => Refresh();
    private void Refresh()
    {
        var data = _vm.GetRecentListeningCharts(); _chart.Update(data.HourDurations);
        _total.Text = L10n.Format("Statistics.ChartTotal", StatisticsView.Time(data.HourDurations.Sum(p => p.Value)));
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e); _timer.Stop();
        Dispatcher.UIThread.Post(() => { if (!this.IsAttachedToVisualTree()) Dispose(); }, DispatcherPriority.Background);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _timer.Tick -= Tick; }
}
