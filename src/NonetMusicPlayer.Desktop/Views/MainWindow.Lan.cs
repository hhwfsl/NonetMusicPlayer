using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private IDisposable? _lanDisconnectNotification;
    private void LanDisconnectedNotification(string message)
    {
        if (IsActive && IsVisible && WindowState != WindowState.Minimized) return;
        _lanDisconnectNotification?.Dispose();
        var notice = SystemConnectionNotification.Show(this, L10n.T("Lan.Title"), message, 10, RestoreFromTray);
        _lanDisconnectNotification = notice; DispatcherTimer.RunOnce(notice.Dispose, TimeSpan.FromSeconds(10));
    }
    private void LanNavigationChanged(object? sender, EventArgs e) => BuildPlaylists();
    /// <summary>配对由真实用户确认；关闭、超时、隐藏界面均不能隐式授权。</summary>
    private async Task<bool> ConfirmLanConnectionAsync(LanConnectionRequest request, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (token.IsCancellationRequested || _vm is null) return false;
            var dialog = new Window { Name = "LanPairConfirmation", Title = L10n.T("Lan.PairTitle"), Width = Math.Clamp(Bounds.Width - 40, 280, 460), SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 10 };
            var reject = Ui.Button(L10n.T("Lan.Reject"), () => dialog.Close(false)); reject.Name = "RejectLanPair";
            var accept = Ui.Button(L10n.T("Lan.Accept"), () => dialog.Close(true)); accept.Name = "AcceptLanPair";
            buttons.Children.Add(reject); buttons.Children.Add(accept);
            var body = Ui.Stack(Ui.Text(L10n.T("Lan.PairTitle"), 20), Ui.Text(L10n.Format("Lan.PairBody", request.Name, request.Address, request.WaitSeconds), 14), buttons);
            body.Margin = new Avalonia.Thickness(24); dialog.Content = body;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var shown = false;
            async void Reveal(object? sender, EventArgs e)
            {
                if (shown || token.IsCancellationRequested || !IsVisible || WindowState == WindowState.Minimized) return;
                shown = true;
                try { completion.TrySetResult(await dialog.ShowDialog<bool>(this)); }
                catch (Exception error) { AppLog.Warning("LAN", "Connection confirmation unavailable", error); completion.TrySetResult(false); }
            }
            // 后台只发系统通知；恢复窗口或点击通知时再显示同一待处理审批。
            Activated += Reveal;
            using var notification = SystemConnectionNotification.Show(this, L10n.T("Lan.PairTitle"), L10n.Format("Lan.PairBody", request.Name, request.Address, request.WaitSeconds), request.WaitSeconds,
                () => { if (!token.IsCancellationRequested) { RestoreFromTray(); Reveal(null, EventArgs.Empty); } });
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(false); completion.TrySetResult(false); }));
            if (IsActive) Reveal(null, EventArgs.Empty);
            try { return !token.IsCancellationRequested && await completion.Task; }
            finally { Activated -= Reveal; if (dialog.IsVisible) dialog.Close(false); }
        });
    }
}
