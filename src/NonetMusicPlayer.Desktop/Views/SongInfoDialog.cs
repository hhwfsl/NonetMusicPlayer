using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed class SongInfoDialog : Window
{
    public SongInfoDialog(MainWindow owner, TrackItem track)
    {
        Title = L10n.T("Common.TrackInformation412578"); WindowDecorations = WindowDecorations.None; CanResize = false;
        Width = 600; MaxHeight = 700; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = owner.Icon;
        var heading = Ui.Text(L10n.T("Common.TrackInformation412578"), 20); heading.FontWeight = FontWeight.SemiBold;
        heading.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var cover = new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(15), ClipToBounds = true, Background = Ui.Brush("SurfaceRaisedBrush"), Child = new Grid { Children = { new VectorIcon { Kind = IconKind.Music, Width = 40, Height = 40 }, new Image { Source = track.Artwork, Stretch = Stretch.UniformToFill } } } };
        var title = Selectable(track.Title, 18); title.FontWeight = FontWeight.SemiBold;
        var summary = Ui.Stack(title, Selectable(track.Artist, 14, true)); summary.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("116,*") }; header.Children.Add(cover); Grid.SetColumn(summary, 1); header.Children.Add(summary);
        MaxHeight = Math.Max(320, Math.Min(700, owner.Bounds.Height - 32));
        var pathText = Selectable(track.ProviderId is null ? track.FilePath : L10n.Format("Plugins.PluginSource", track.ProviderId), 12, true); pathText.Name = "SongFilePathText";
        var path = Ui.Button(L10n.T("Common.OpenLocation"), () => { try { RevealFile(track.FilePath); } catch (Exception error) { if (owner.DataContext is ViewModels.MainViewModel vm) vm.ReportError(L10n.T("Library.UnableToLocateMusicFile"), error); } });
        path.Name = "SongFilePath"; path.IsEnabled = track.ProviderId is null && !string.IsNullOrWhiteSpace(track.FilePath);
        ToolTip.SetTip(path, OperatingSystem.IsWindows() ? L10n.T("Common.RevealTrackInFileExplorer") : L10n.T("Common.OpenTrackFolder"));
        var close = Ui.Button(L10n.T("Common.Close"), () => Close(), true); close.Name = "CloseSongInfo";
        var actions = Ui.Actions(close); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var panel = Ui.Stack(heading, header, Field(L10n.T("Library.Albums"), track.Album), Field(L10n.T("Common.Duration"), track.DurationText), Field(L10n.T("Common.Format"), track.FormatText), Field(L10n.T("Common.FileSize"), track.FileSizeText), Ui.Text(L10n.T("Common.FilePath"), 12, true), pathText, path, actions);
        Content = new Border { Padding = new Thickness(28), BorderThickness = new Thickness(1), BorderBrush = Ui.Brush("DividerBrush"), Background = Ui.Brush("SurfaceBrush"), Child = Ui.Scroll(panel) };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    private static Control Field(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") }; grid.Children.Add(Ui.Text(label, 13, true));
        var text = Selectable(value); Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
    }
    private static SelectableTextBlock Selectable(string text, double size = 13, bool muted = false) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush(muted ? "TextSecondaryBrush" : "TextPrimaryBrush") };
    public static Task Show(MainWindow owner, TrackItem track) => new SongInfoDialog(owner, track).ShowDialog(owner);
    public static void RevealFile(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException(L10n.T("Library.TheMusicFileMayHaveMovedOrBeenDeleted"), path);
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
        else MainWindow.OpenPath(Path.GetDirectoryName(path)!);
    }
}
