using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private Border? _dropPreview;
    private TextBlock? _dropPreviewText;
    private void InitializeDropPreview()
    {
        AddHandler(DragDrop.DropEvent, (_, _) => HideDropPreview(), RoutingStrategies.Tunnel, true);
        AddHandler(DragDrop.DragLeaveEvent, (_, e) => { if (!new Rect(ClientSize).Contains(e.GetPosition(this))) HideDropPreview(); }, RoutingStrategies.Bubble, true);
        Deactivated += (_, _) => HideDropPreview();
    }
    /// <summary>提示与实际投放目标使用同一原控件身份，不依赖左右位置或界面方案的容器结构。</summary>
    private void UpdateDropPreview(DragEventArgs e)
    {
        if (_vm is null) return;
        var ancestors = Ancestors(e.Source).OfType<Control>().ToArray();
        var player = ancestors.Contains(PlayerSurface);
        var navigation = ancestors.OfType<Button>().FirstOrDefault(b => b.Tag is string id && id.StartsWith("playlist:", StringComparison.Ordinal));
        var playlist = navigation?.Tag is string tag ? _vm.Playlists.FirstOrDefault(p => "playlist:" + p.Id == tag) : _vm.CurrentPlaylist;
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        var payload = e.DataTransfer.TryGetText() ?? "";
        string? message = null;
        var name = paths.Length == 1 ? Path.GetFileName(paths[0]) : L10n.Format("Home.DropItems", paths.Length);
        if (player && payload.StartsWith("nonet-tracks:", StringComparison.Ordinal) && DroppedTracks(payload) is { Length: > 0 } dropped) message = L10n.Format("Home.DropPlay", dropped.Length == 1 ? dropped[0].Title : L10n.Format("Home.DropItems", dropped.Length));
        else if (player && paths.Any(p => MusicLibraryScanner.SupportedExtensions.Contains(Path.GetExtension(p)))) message = L10n.Format("Home.DropPlay", name);
        else if (!player && paths.Length == 1 && Path.GetExtension(paths[0]).ToLowerInvariant() is ".lrc" or ".txt") message = L10n.Format("Home.DropLyrics", name);
        else if (payload.StartsWith("nonet-playlist:", StringComparison.Ordinal) && navigation is not null) message = L10n.T("Home.DropPlaylistReorder");
        else if (!player && playlist is not null)
        {
            if (payload.StartsWith("nonet-tracks:", StringComparison.Ordinal))
                message = navigation is null && ancestors.Any(c => c.Name == "TracksList") ? L10n.T("Home.DropReorder") : L10n.Format("Home.DropAdd", PlaylistName(playlist), L10n.Format("Home.DropItems", payload.Split('\n').Length));
            else if (paths.Length > 0) message = L10n.Format("Home.DropAdd", PlaylistName(playlist), name);
        }
        if (message is null) { HideDropPreview(); return; }
        if (_dropPreview is null)
        {
            _dropPreviewText = Ui.RawText("", 14); _dropPreviewText.TextWrapping = TextWrapping.Wrap; _dropPreviewText.MaxLines = 2;
            var body = new Grid { ColumnDefinitions = new("24,*"), ColumnSpacing = 12 }; body.Children.Add(new VectorIcon { Kind = IconKind.FileAdd, Brush = Ui.Brush("AccentBrush") }); Grid.SetColumn(_dropPreviewText, 1); body.Children.Add(_dropPreviewText);
            _dropPreview = new Border { Name = "DropPreviewSurface", Child = body, ClipToBounds = true,
                IsHitTestVisible = false, ZIndex = 2000, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new(24, 0, 24, 120), MaxWidth = 620, Padding = new(18, 12), CornerRadius = new(12), Background = Ui.Brush("NavSelectedBrush"), BorderThickness = new(0) };
            ((Grid)Content!).Children.Add(_dropPreview);
        }
        _dropPreviewText!.Text = message; _dropPreview.IsVisible = true;
    }
    private void HideDropPreview() { if (_dropPreview is not null) _dropPreview.IsVisible = false; }
}
