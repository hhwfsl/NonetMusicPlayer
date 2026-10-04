using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    public PluginDownloadNotification ShowPluginDownload(Action cancel) => new(this, NotificationHost, cancel);
    private readonly Queue<UserNotificationEventArgs> _notifications = new();
    private readonly DispatcherTimer _notificationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _notificationSeconds;
    private void InitializeNotifications()
    {
        _notificationTimer.Tick += (_, _) =>
        {
            if (NotificationBar.IsPointerOver) return;
            if (--_notificationSeconds <= 0) NextNotification();
        };
    }
    private void UserNotification(object? sender, UserNotificationEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_notifications.Count >= 20) _notifications.Dequeue();
            _notifications.Enqueue(e);
            if (!NotificationBar.IsVisible) NextNotification();
        });
    }
    private void NextNotification()
    {
        _notificationTimer.Stop();
        if (!_notifications.TryDequeue(out var next)) { NotificationBar.IsVisible = false; return; }
        NotificationTitle.Text = L10n.T(next.Title);
        NotificationMessage.Text = L10n.T(next.Message);
        NotificationIcon.Kind = IconKind.Info;
        var brush = new SolidColorBrush(Color.Parse(next.IsWarning ? "#DCA647" : "#F0526C"));
        NotificationIcon.Brush = brush; NotificationBar.BorderBrush = brush;
        _notificationSeconds = next.IsWarning ? 12 : 20;
        NotificationBar.IsVisible = true; _notificationTimer.Start();
        Avalonia.Automation.AutomationProperties.SetName(NotificationBar, next.Title + "。" + next.Message);
    }
    private void DismissNotification_Click(object? sender, RoutedEventArgs e) => NextNotification();
}
