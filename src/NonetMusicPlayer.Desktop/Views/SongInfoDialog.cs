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
    private readonly CancellationTokenSource _loading = new();
    private readonly StackPanel _details = Ui.Stack();
    private readonly SelectableTextBlock _title;
    private readonly SelectableTextBlock _artist;
    private readonly TextBlock _notice = Ui.Text("", 12, true);
    private readonly Button _reveal;
    private readonly string _source;
    public Task MetadataLoad { get; private set; } = Task.CompletedTask;

    public SongInfoDialog(MainWindow owner, TrackItem track)
    {
        Title = L10n.T("Common.TrackInformation412578"); WindowDecorations = WindowDecorations.None; CanResize = false;
        Width = Math.Clamp(owner.Bounds.Width - 32, 320, 620); MaxHeight = 700; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = owner.Icon;
        var heading = Ui.Text(L10n.T("Common.TrackInformation412578"), 20); heading.FontWeight = FontWeight.SemiBold;
        heading.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var cover = new Border { Name = "SongInfoCover", Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(15), ClipToBounds = true, Background = Ui.Brush("SurfaceRaisedBrush"), Child = new Grid { Children = { new VectorIcon { Kind = IconKind.Music, Width = 40, Height = 40 }, new Image { Source = track.Artwork, Stretch = Stretch.UniformToFill } } } };
        _title = Selectable(track.Title, 18); _title.FontWeight = FontWeight.SemiBold;
        _artist = Selectable(track.Artist, 14, true);
        var summary = Ui.Stack(_title, _artist); summary.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("116,*") }; header.Children.Add(cover); Grid.SetColumn(summary, 1); header.Children.Add(summary);
        MaxHeight = Math.Max(320, Math.Min(700, owner.Bounds.Height - 32));
        _source = track.ProviderId is null ? track.SourceText : (owner.DataContext as ViewModels.MainViewModel)?.Plugins.Installed.FirstOrDefault(p => p.Id == track.ProviderId)?.Name ?? track.SourceText;
        _reveal = Ui.Button(L10n.T("Common.OpenLocation"), () => { try { RevealFile(track.FilePath); } catch (Exception error) { if (owner.DataContext is ViewModels.MainViewModel vm) vm.ReportError(L10n.T("Library.UnableToLocateMusicFile"), error); } });
        _reveal.Name = "SongFilePath"; _reveal.IsEnabled = track.ProviderId is null && !string.IsNullOrWhiteSpace(track.FilePath);
        ToolTip.SetTip(_reveal, OperatingSystem.IsWindows() ? L10n.T("Common.RevealTrackInFileExplorer") : L10n.T("Common.OpenTrackFolder"));
        var close = Ui.Button(L10n.T("Common.Close"), () => Close(), true); close.Name = "CloseSongInfo";
        var actions = Ui.Actions(close); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var panel = Ui.Stack(header, _details, _notice); var scroll = Ui.Scroll(panel); scroll.Name = "SongInfoScroll";
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 18 };
        layout.Children.Add(heading); Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Grid.SetRow(actions, 2); layout.Children.Add(actions);
        // 长路径和大字号只滚动内容，关闭按钮始终留在弹窗底部。
        Content = new Border { Padding = new Thickness(24), BorderThickness = new Thickness(0), Background = Ui.Brush("SurfaceBrush"), Child = layout };
        Render(track, SongInformation.Cached(track));
        Opened += (_, _) => MetadataLoad = LoadMetadataAsync(track);
        Closed += (_, _) => { _loading.Cancel(); _loading.Dispose(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    private async Task LoadMetadataAsync(TrackItem track)
    {
        var token = _loading.Token;
        try
        {
            // 文件标签读取留在工作线程，网络盘或大文件不阻塞弹窗打开、滚动和关闭。
            var information = await Task.Run(() => SongInformation.Read(track), token);
            if (!token.IsCancellationRequested) Render(track, information);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void Render(TrackItem track, SongInformation info)
    {
        _title.Text = info.Title; _artist.Text = info.Artist;
        // 元数据加载后重建信息行，先从旧分组移除复用按钮，避免控件同时归属两个父容器。
        if (_reveal.Parent is Panel previous) previous.Children.Remove(_reveal);
        _details.Children.Clear();
        var song = Ui.Stack(); song.Spacing = 8;
        song.Children.Add(Field("Library.Albums", info.Album));
        if (!string.IsNullOrWhiteSpace(info.AlbumArtist) && !string.Equals(info.AlbumArtist, info.Artist, StringComparison.OrdinalIgnoreCase)) song.Children.Add(Field("SongInfo.AlbumArtist", info.AlbumArtist));
        var release = Join(info.Year > 0 ? info.Year.ToString(L10n.Culture) : "", info.Genre);
        if (release.Length > 0) song.Children.Add(Field("SongInfo.Release", release));
        var numbering = Join(Number("SongInfo.TrackNumber", info.Track, info.TrackCount), Number("SongInfo.DiscNumber", info.Disc, info.DiscCount));
        if (numbering.Length > 0) song.Children.Add(Field("SongInfo.Numbering", numbering));
        if (!string.IsNullOrWhiteSpace(info.Composer)) song.Children.Add(Field("SongInfo.Composer", info.Composer));
        _details.Children.Add(Section("SongInfo.MusicSection", song));
        var audio = Ui.Stack(); audio.Spacing = 8;
        var duration = double.IsFinite(info.Duration) && info.Duration > 0 && info.Duration < TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(info.Duration).ToString(info.Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "—";
        audio.Children.Add(Field("Common.Duration", duration));
        audio.Children.Add(Field("Common.Format", Join(track.FormatText, info.Codec)));
        var specs = Join(info.SampleRate > 0 ? (info.SampleRate / 1000d).ToString("0.###", L10n.Culture) + " kHz" : "",
            info.BitsPerSample > 0 ? info.BitsPerSample.ToString(L10n.Culture) + " bit" : "",
            info.Channels switch { 1 => L10n.T("SongInfo.Mono"), 2 => L10n.T("SongInfo.Stereo"), > 2 => L10n.Format("SongInfo.ChannelCount", info.Channels), _ => "" });
        if (specs.Length > 0) audio.Children.Add(Field("SongInfo.AudioSpecs", specs));
        if (info.Bitrate > 0) audio.Children.Add(Field("SongInfo.Bitrate", info.Bitrate.ToString("N0", L10n.Culture) + " kbps"));
        _details.Children.Add(Section("SongInfo.AudioSection", audio));
        var file = Ui.Stack(); file.Spacing = 8; file.Children.Add(Field("Common.Source", _source));
        if (info.FileSize > 0 || info.FileAvailable) file.Children.Add(Field("Common.FileSize", (info.FileSize / 1048576d).ToString("0.##", L10n.Culture) + " MiB"));
        if (info.Modified is { } modified) file.Children.Add(Field("SongInfo.Modified", modified.ToString("yyyy-MM-dd HH:mm", L10n.Culture)));
        if (track.ProviderId is null)
        {
            file.Children.Add(Ui.Text("Common.FilePath", 12, true));
            var path = Selectable(track.FilePath, 12, true); path.Name = "SongFilePathText"; file.Children.Add(path); file.Children.Add(_reveal);
        }
        _reveal.IsEnabled = track.ProviderId is null && (info.FileAvailable || string.IsNullOrEmpty(info.NoticeKey)) && !string.IsNullOrWhiteSpace(track.FilePath);
        _details.Children.Add(Section("SongInfo.FileSection", file));
        _notice.Text = L10n.T(info.NoticeKey); _notice.IsVisible = info.NoticeKey.Length > 0;
    }

    private static string Number(string key, uint value, uint count) => value > 0 ? L10n.Format(key, count > 0 ? $"{value} / {count}" : value.ToString(L10n.Culture)) : "";
    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase));
    private static Control Section(string key, Control content)
    {
        var label = Ui.Text(key, 12, true); label.FontWeight = FontWeight.SemiBold;
        var stack = Ui.Stack(label, content); stack.Spacing = 10; return stack;
    }
    private static Control Field(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("116,*"), ColumnSpacing = 12 };
        var caption = Ui.Text(label, 13, true); grid.Children.Add(caption);
        var text = Selectable(string.IsNullOrWhiteSpace(value) ? "—" : value); Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
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
