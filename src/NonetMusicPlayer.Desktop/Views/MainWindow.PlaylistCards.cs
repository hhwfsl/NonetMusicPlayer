using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private Control BuildPlaylistCard(Playlist playlist)
    {
        // 只替换内容，原按钮继续保留导航、右键菜单与拖动排序处理。
        var button = BuildPlaylistNavigationButton(playlist);
        var cover = PlaylistArtwork(playlist, 150); cover.Width = double.NaN;
        return PlaylistCard(button, cover, PlaylistName(playlist), L10n.Format("Common.Tracks", playlist.TrackIds.Count));
    }
    private Control BuildRemotePlaylistCard((string Page, string Name, string Source) remote)
    {
        var button = BuildRemotePlaylistNavigationButton(remote);
        var cover = new Border { Height = 150, CornerRadius = new(12), Background = Brushes.Transparent,
            Child = new VectorIcon { Kind = IconKind.Playlist, Width = 58, Height = 58, Brush = Ui.Brush("TextSecondaryBrush") } };
        return PlaylistCard(button, cover, remote.Name, remote.Source);
    }
    private static Button PlaylistCard(Button button, Control cover, string title, string note)
    {
        var label = Ui.RawText(title, 15); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
        var content = Ui.Stack(cover, label, Ui.RawText(note, 12, true)); content.Spacing = 8;
        button.Name = "InterfacePlaylistCard"; button.Classes.Remove("nav"); button.Classes.Add("quiet");
        button.Classes.Add("playlist-card"); button.Content = content;
        button.Margin = new(0, 0, 12, 0); button.Padding = new(10);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return button;
    }
}
