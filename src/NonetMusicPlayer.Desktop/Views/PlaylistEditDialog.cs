using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed record PlaylistDetails(string Name, string Description, string? CoverPath);
public sealed class PlaylistEditDialog : Window
{
    private Bitmap? _preview;
    private PlaylistEditDialog(MainWindow owner, Playlist playlist)
    {
        InputCommitService.Install(this);
        Title = L10n.T("Playlists.EditPlaylist"); WindowDecorations = WindowDecorations.None; CanResize = false;
        Width = 560; MaxHeight = 720; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; Icon = owner.Icon;
        MaxHeight = Math.Max(320, Math.Min(720, owner.Bounds.Height - 32));
        var name = new TextBox { Text = playlist.IsSystem ? L10n.T("Playlists.LikedSongs") : playlist.Name, MaxLength = 80, IsReadOnly = playlist.IsSystem, MinHeight = 40 };
        var description = new TextBox { Text = playlist.Description, MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 180, PlaceholderText = L10n.T("Playlists.DescribeThisPlaylistUpToCharacters") };
        string? coverPath = playlist.CoverPath;
        var image = new Image { Source = playlist.Artwork, Stretch = Stretch.UniformToFill };
        var art = new Border { Width = 100, Height = 100, CornerRadius = new CornerRadius(15), ClipToBounds = true, Background = Ui.Brush("SurfaceRaisedBrush"), Child = new Grid { Children = { new VectorIcon { Kind = playlist.IsSystem ? IconKind.Heart : IconKind.Playlist, Width = 40, Height = 40 }, image } } };
        var errorText = Ui.Text("", 12); errorText.Foreground = new SolidColorBrush(Color.Parse("#F0526C")); errorText.IsVisible = false;
        var choose = Ui.AsyncButton(L10n.T("Common.ChangeArtwork"), async () =>
        {
            var files = await owner.OpenFilesAsync(L10n.T("Playlists.SelectPlaylistCover"), ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"], false);
            if (files.Length == 0) return;
            try
            {
                var cropped = await CoverCropDialog.Show(this, ((ViewModels.MainViewModel)owner.DataContext!).Storage, files[0]);
                if (cropped is null) return;
                using var stream = File.OpenRead(cropped); var bitmap = Bitmap.DecodeToWidth(stream, 320);
                image.Source = bitmap; _preview?.Dispose(); _preview = bitmap; coverPath = cropped; errorText.IsVisible = false;
            }
            catch (Exception error) { errorText.Text = L10n.T("Common.UnableToReadCoverSelectAValidImage"); errorText.IsVisible = true; if (owner.DataContext is ViewModels.MainViewModel vm) vm.ReportError(L10n.T("Common.UnableToReadCover"), error); }
        });
        var reset = Ui.Button(L10n.T("Common.UseDefaultCover"), () => { image.Source = null; _preview?.Dispose(); _preview = null; coverPath = null; });
        var coverActions = Ui.Stack(choose, reset); coverActions.VerticalAlignment = VerticalAlignment.Center;
        var coverRow = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") }; coverRow.Children.Add(art); Grid.SetColumn(coverActions, 1); coverRow.Children.Add(coverActions);
        var title = Ui.Text(L10n.T("Playlists.EditPlaylist"), 20); title.FontWeight = FontWeight.SemiBold;
        title.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var save = Ui.Button(L10n.T("Common.Save"), () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { errorText.Text = L10n.T("Playlists.EnterAPlaylistName"); errorText.IsVisible = true; name.Focus(); return; }
            Close(new PlaylistDetails(name.Text.Trim(), description.Text?.Trim() ?? "", coverPath));
        }, true);
        var actions = Ui.Actions(Ui.Button(L10n.T("Common.Cancel"), () => Close()), save); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var panel = Ui.Stack(title, coverRow, Ui.Text(playlist.IsSystem ? L10n.T("Playlists.NameFixedPlaylistCannotBeRenamedOrDeleted") : L10n.T("Playlists.PlaylistName"), 12, true), name, Ui.Text(L10n.T("Playlists.Description"), 12, true), description, errorText, actions);
        Content = new Border { Padding = new Thickness(28), Background = Ui.Brush("SurfaceBrush"), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new Thickness(1), Child = Ui.Scroll(panel) };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => { image.Source = null; _preview?.Dispose(); _preview = null; };
    }
    public static Task<PlaylistDetails?> Show(MainWindow owner, Playlist playlist) => new PlaylistEditDialog(owner, playlist).ShowDialog<PlaylistDetails?>(owner);
}
