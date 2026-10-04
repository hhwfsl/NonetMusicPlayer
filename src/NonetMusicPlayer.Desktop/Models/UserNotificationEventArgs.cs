namespace NonetMusicPlayer.Desktop.Models;

public sealed class UserNotificationEventArgs(string title, string message, bool isWarning = false) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public bool IsWarning { get; } = isWarning;
}
