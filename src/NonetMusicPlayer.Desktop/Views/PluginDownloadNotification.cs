using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>应用内下载进度提示，完成后切换为自动关闭的完成通知。</summary>
public sealed class PluginDownloadNotification : IDisposable, IProgress<double>
{
    private readonly MainWindow _owner;
    private readonly StackPanel _host;
    private readonly Border _toast;
    private readonly TextBlock _title, _message, _percent;
    private readonly ProgressBar _progress;
    private bool _disposed;
    private int _lastQueuedPercent = -1;
    public PluginDownloadNotification(MainWindow owner, StackPanel host, Action cancel)
    {
        _owner = owner; _host = host;
        _title = Ui.Text(L10n.T("Plugins.FindingPluginAssets"), 14); _title.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        _message = Ui.Text(L10n.T("Plugins.ReadingGitHubReleaseInformation"), 12, true);
        _percent = Ui.Text("", 12, true); _percent.HorizontalAlignment = HorizontalAlignment.Right;
        _progress = new ProgressBar { Name = "PluginDownloadProgress", Minimum = 0, Maximum = 100, Height = 6, IsIndeterminate = true };
        _toast = Build(Ui.Stack(_title, _message, _progress, _percent), () => { cancel(); Dispose(); }, L10n.T("Common.CancelDownload"));
        _toast.Name = "PluginDownloadToast"; host.Children.Insert(0, _toast); owner.Closed += OwnerClosed;
    }
    public void Downloading(string filename, string? title = null)
    {
        if (_disposed) return;
        _title.Text = L10n.T(title ?? "Plugins.DownloadingPlugin"); _message.Text = filename; _percent.Text = L10n.T("Common.WaitingForDownload");
    }
    public void Report(double value)
    {
        if (_disposed || !double.IsFinite(value)) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            // 快速下载可能产生大量数据块；每个百分比最多排入一次界面更新。
            if (Interlocked.Exchange(ref _lastQueuedPercent, (int)Math.Clamp(value, 0, 100)) != (int)Math.Clamp(value, 0, 100)) Dispatcher.UIThread.Post(() => Report(value));
            return;
        }
        // 总长度未知时使用忙碌动画，不将尚未完成的下载误显示为零进度。
        if (value > 0) _progress.IsIndeterminate = false;
        _progress.Value = Math.Clamp(value, 0, 100); _percent.Text = _progress.IsIndeterminate ? L10n.T("Common.Downloading") : $"{_progress.Value:0}%";
    }
    public void Complete(string filename, string? title = null)
    {
        if (_disposed) return;
        Dispose();
        var heading = Ui.Text(L10n.T(title ?? "Plugins.PluginDownloadComplete"), 14); heading.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        var body = Ui.Stack(heading, Ui.RawText(filename, 12, true));
        Border? completed = null;
        var expiration = new CancellationTokenSource(); var removed = false;
        void Remove() { if (removed) return; removed = true; expiration.Cancel(); expiration.Dispose(); _owner.Closed -= CloseCompleted; if (completed is not null) _host.Children.Remove(completed); }
        void CloseCompleted(object? sender, EventArgs e) => Remove();
        completed = Build(body, Remove, L10n.T("Common.ExitApplication56F252")); completed.Name = "PluginDownloadComplete";
        _host.Children.Insert(0, completed); _owner.Closed += CloseCompleted;
        async Task Expire()
        {
            try { await Task.Delay(TimeSpan.FromSeconds(4), expiration.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            Dispatcher.UIThread.Post(Remove);
        }
        _ = Expire();
    }
    private static Border Build(Control content, Action close, string closeTip)
    {
        var icon = new VectorIcon { Kind = IconKind.Plugins, Brush = Ui.Brush("AccentBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0) };
        var button = Ui.Button("", close); button.Content = new VectorIcon { Kind = IconKind.Close, Width = 16, Height = 16 }; button.Classes.Add("transport"); button.Width = button.Height = 28; button.MinHeight = button.MinWidth = 28;
        ToolTip.SetTip(button, L10n.T(closeTip)); Avalonia.Automation.AutomationProperties.SetName(button, L10n.T(closeTip));
        content.Margin = new(10, 0); var grid = new Grid { ColumnDefinitions = new("24,*,28") }; grid.Children.Add(icon); Grid.SetColumn(content, 1); grid.Children.Add(content); Grid.SetColumn(button, 2); grid.Children.Add(button);
        return new Border { Padding = new(16), CornerRadius = new(12), Background = Ui.Brush("SurfaceRaisedBrush"), BorderBrush = Ui.Brush("AccentBrush"), BorderThickness = new(1), Child = grid };
    }
    private void OwnerClosed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _host.Children.Remove(_toast); _owner.Closed -= OwnerClosed;
    }
}
