using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly ReleaseUpdateService _updates = new();
    private readonly CancellationTokenSource _updateStop = new();
    private Button? _newVersion;
    private UpdateRelease? _availableUpdate;
    private PreparedUpdate? _preparedUpdate;
    private bool _checkingUpdate, _downloadingUpdate;
    private Window? _releaseWindow;
    public string VersionNumber => ReleaseUpdateService.CurrentVersion;
    private void InitializeUpdates()
    {
        _newVersion = Ui.AsyncButton("New", ShowUpdateAsync); _newVersion.Name = "NewVersionBadge"; _newVersion.IsVisible = false;
        _newVersion.MinWidth = 38; _newVersion.MinHeight = 22; _newVersion.FontSize = 10; _newVersion.Padding = new(8, 1); _newVersion.VerticalAlignment = VerticalAlignment.Center;
        _newVersion.Classes.Add("primary"); ToolTip.SetTip(_newVersion, L10n.T("Update.New")); TitleBrand.Children.Add(_newVersion);
        Opened += async (_, _) =>
        {
            // 测试宿主和设计器不访问网络。离线检查只记录日志，不打断启动。
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
            {
                // 两类检查独立进行，软件 Release 的慢请求不会阻塞插件检查或首屏。
                await Task.WhenAll(CheckUpdatesAsync(false), CheckPluginUpdatesAsync());
            }
        };
        Closed += (_, _) => { _updateStop.Cancel(); _releaseWindow?.Close(); };
    }
    public async Task CheckUpdatesAsync(bool manual = true)
    {
        if (_checkingUpdate || _vm is null) return; _checkingUpdate = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_updateStop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            _availableUpdate = await _updates.CheckAsync(VersionNumber, cancellationToken: timeout.Token);
            _newVersion!.IsVisible = _availableUpdate is not null;
            if (manual)
            {
                if (_availableUpdate is not null) await ShowUpdateAsync();
                else _vm.ReportWarning(L10n.T("Update.LatestOrNoRelease"));
            }
        }
        catch (OperationCanceledException) { if (manual && !_updateStop.IsCancellationRequested) _vm.ReportWarning(L10n.T("Update.Failed")); }
        catch (Exception error)
        {
            AppLog.Warning("Update", "Unable to check public releases", error);
            if (manual) _vm.ReportError(L10n.T("Update.Failed"), error);
        }
        finally { _checkingUpdate = false; }
    }
    private Task ShowUpdateAsync()
    {
        if (_availableUpdate is not { } release || _vm is null) return Task.CompletedTask;
        if (_releaseWindow is not null) { _releaseWindow.Activate(); return Task.CompletedTask; }
        var notes = new TextBox { Text = string.IsNullOrWhiteSpace(release.Notes) ? L10n.T("Update.NoNotes") : release.Notes, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent };
        FullTextToolTips.SetEnabled(notes, false); ToolTip.SetTip(notes, null); notes.Name = "UpdateReleaseNotes";
        var heading = Ui.Text(L10n.T("Update.New") + " · " + release.Version, 22); heading.FontWeight = FontWeight.SemiBold;
        var window = new Window { Title = L10n.T("Update.New"), Width = Math.Clamp(Bounds.Width - 50, 360, 680), Height = Math.Clamp(Bounds.Height - 70, 330, 560), MinWidth = 340, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowDecorations = WindowDecorations.None, Icon = Icon, DataContext = _vm };
        var buttons = Ui.Actions(); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var cancel = Ui.Button(L10n.T("Common.Cancel"), () => window.Close());
        var status = Ui.Text(release.Asset is null ? L10n.T("Update.NoPackage") : "", 12, true);
        var action = Ui.AsyncButton(L10n.T(_preparedUpdate is null ? "Update.Download" : "Update.Restart"), async () =>
        {
            if (_vm.IsMigratingData) { _vm.ReportWarning(L10n.T("Storage.AppDataIsBeingCopiedWaitForCompletionBefore")); return; }
            if (_preparedUpdate is not null)
            {
                _vm.Save(); using var helper = UpdateInstaller.LaunchHelper(_preparedUpdate, _vm.Storage.Root, _vm.Storage.BackupFolder);
                window.Close(); ExitApplication(); return;
            }
            if (_downloadingUpdate) return; _downloadingUpdate = true;
            window.Close();
            using var download = CancellationTokenSource.CreateLinkedTokenSource(_updateStop.Token);
            using var toast = ShowPluginDownload(download.Cancel); toast.Downloading(release.Asset!.Name!, "Update.Downloading");
            status.Text = L10n.T("Update.Downloading");
            try
            {
                _preparedUpdate = await _updates.DownloadAsync(release, _vm.Storage.Root, new Progress<double>(v => toast.Report(v * 100)), download.Token);
                download.Token.ThrowIfCancellationRequested();
                // 用户点击下载即授权本次自动更新；校验完成、数据落盘后才交给独立助手并退出。
                if (_vm.IsMigratingData) throw new InvalidOperationException(L10n.T("Storage.AppDataIsBeingCopiedWaitForCompletionBefore"));
                _vm.Save(); using var helper = UpdateInstaller.LaunchHelper(_preparedUpdate, _vm.Storage.Root, _vm.Storage.BackupFolder);
                toast.Complete(release.Version, "Update.Ready"); ExitApplication();
            }
            catch (OperationCanceledException)
            {
                if (_preparedUpdate is { } canceled) { ReleaseUpdateService.Discard(canceled); _preparedUpdate = null; }
                if (!_updateStop.IsCancellationRequested) _vm.ReportWarning(L10n.T("Update.Cancelled"));
            }
            catch (Exception error) { status.Text = L10n.T("Update.DownloadFailed"); _vm.ReportError(status.Text, error); }
            finally { _downloadingUpdate = false; }
        }, true);
        action.IsEnabled = release.Asset is not null || _preparedUpdate is not null; action.Name = "DownloadUpdate";
        buttons.Children.Add(cancel); buttons.Children.Add(action);
        var grid = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 16 }; grid.Children.Add(heading);
        var scroll = Ui.Scroll(notes); Grid.SetRow(scroll, 1); grid.Children.Add(scroll); Grid.SetRow(status, 2); grid.Children.Add(status); Grid.SetRow(buttons, 3); grid.Children.Add(buttons);
        window.Content = new Border { Padding = new(26), Background = Ui.Brush("SurfaceBrush"), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new(1), CornerRadius = new(12), Child = grid };
        heading.PointerPressed += (_, e) => { if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) window.BeginMoveDrag(e); };
        window.Closed += (_, _) => _releaseWindow = null; _releaseWindow = window;
        return window.ShowDialog(this);
    }
    internal Control AboutSettings()
    {
        var github = new VectorIcon { Kind = IconKind.GitHub, Width = 22, Height = 22, Brush = Ui.Brush("TextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        var link = Ui.Button("", () => Process.Start(new ProcessStartInfo(ReleaseUpdateService.Repository) { UseShellExecute = true }));
        link.Content = Ui.RawText(ReleaseUpdateService.Repository, 12); link.Classes.Add("quiet"); link.HorizontalContentAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(link, ReleaseUpdateService.Repository);
        var repository = new Grid { ColumnDefinitions = new("24,*"), ColumnSpacing = 8 }; repository.Children.Add(github); Grid.SetColumn(link, 1); repository.Children.Add(link);
        return Ui.Card(L10n.T("Update.About"), Ui.Row(L10n.T("Update.Version"), VersionNumber, Ui.AsyncButton(L10n.T("Update.Check"), () => CheckUpdatesAsync())), Ui.Row(L10n.T("Update.Repository"), "", repository, "Auto,*"));
    }
}
