using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NonetMusicPlayer.Desktop.Controls;

public enum IconKind
{
    Music, Library, Heart, Playlist, Folder, Search, Plus, Queue, Refresh,
    Previous, Next, Play, Pause, Volume, Mute, RepeatAll, RepeatOne, Shuffle, More, Lyrics,
    Settings, Plugins, Help, Info, History, Artist, Album, FileAdd, Edit, Trash, SelectAll, Close, Minimize, Maximize, Statistics, Game, Lock, Unlock, Calendar, DesktopLyricsShow, DesktopLyricsHide, MoveUp, MoveDown, Home, Terminal, Back, GitHub, BackToTop, ChevronUp, ChevronDown
}

/// <summary>图标共享 24 像素绘制坐标，避免字体图标的基线偏移。</summary>
public sealed class VectorIcon : Control
{
    public static readonly StyledProperty<IconKind> KindProperty =
        AvaloniaProperty.Register<VectorIcon, IconKind>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<VectorIcon, IBrush?>(nameof(Brush), Brushes.White);
    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<VectorIcon, bool>(nameof(Filled));

    private static readonly IReadOnlyDictionary<IconKind, Geometry> Geometries =
        new Dictionary<IconKind, Geometry>
        {
            [IconKind.Music] = Geometry.Parse("M9 18V5L20 3V16 M9 8L20 6 M9 18C9 20 7 21 5 21C3 21 2 20 2 18.5C2 17 4 16 6 16C8 16 9 16.5 9 18 M20 16C20 18 18 19 16 19C14 19 13 18 13 16.5C13 15 15 14 17 14C19 14 20 14.5 20 16"),
            [IconKind.Library] = Geometry.Parse("M3 4H7V20H3Z M10 4H14V20H10Z M17 5L20 4L23 19L20 20Z"),
            [IconKind.Heart] = Geometry.Parse("M12 20L4 12C-2 5 7 0 12 7C17 0 26 5 20 12Z"),
            [IconKind.Playlist] = Geometry.Parse("M3 5H17 M3 10H17 M3 15H11 M17 14V21 M13.5 17.5H20.5"),
            [IconKind.Folder] = Geometry.Parse("M3 6H9L11 8H21V20H3Z M3 10H21"),
            [IconKind.Search] = Geometry.Parse("M16 10A6 6 0 1 1 4 10A6 6 0 1 1 16 10 M14.5 14.5L21 21"),
            [IconKind.Plus] = Geometry.Parse("M12 5V19 M5 12H19"),
            [IconKind.Queue] = Geometry.Parse("M3 5H21 M3 11H21 M3 17H13 M17 15L22 18L17 21Z"),
            [IconKind.Previous] = Geometry.Parse("M4 5H6V19H4Z M19 5L8 12L19 19Z"),
            [IconKind.Back] = Geometry.Parse("M14 5L7 12L14 19 M7 12H21"),
            [IconKind.BackToTop] = Geometry.Parse("M5 3H19 M12 21V8 M6 14L12 8L18 14"),
            [IconKind.ChevronUp] = Geometry.Parse("M5 15L12 8L19 15"),
            [IconKind.ChevronDown] = Geometry.Parse("M5 9L12 16L19 9"),
            [IconKind.Next] = Geometry.Parse("M18 5H20V19H18Z M5 5L16 12L5 19Z"),
            [IconKind.MoveUp] = Geometry.Parse("M5 16L12 7L19 16Z"),
            [IconKind.MoveDown] = Geometry.Parse("M5 8L12 17L19 8Z"),
            [IconKind.Play] = Geometry.Parse("M7 4L20 12L7 20Z"),
            [IconKind.Pause] = Geometry.Parse("M6 4H10V20H6Z M14 4H18V20H14Z"),
            [IconKind.Volume] = Geometry.Parse("M3 9H7L12 5V19L7 15H3Z M16 8C19 10 19 14 16 16 M19 5C24 9 24 15 19 19"),
            [IconKind.Mute] = Geometry.Parse("M3 9H7L12 5V19L7 15H3Z M16 9L22 15 M22 9L16 15"),
            [IconKind.RepeatAll] = Geometry.Parse("M4 10V8C4 5 7 4 10 4H20 M17 1L20 4L17 7 M20 14V16C20 19 17 20 14 20H4 M7 17L4 20L7 23"),
            [IconKind.RepeatOne] = Geometry.Parse("M18 5H6C1 5 1 19 6 19H18C23 19 23 5 18 5 M16 2L19 5L16 8 M10 10L12 8V16 M10 16H14"),
            [IconKind.Shuffle] = Geometry.Parse("M3 6H5C10 6 14 18 19 18H21 M18 15L21 18L18 21 M3 18H5C10 18 14 6 19 6H21 M18 3L21 6L18 9"),
            [IconKind.More] = Geometry.Parse("M4 12H5 M11.5 12H12.5 M19 12H20"),
            [IconKind.Lyrics] = Geometry.Parse("M4 4H20V18H14L10 22V18H4Z M8 8H16 M8 12H14"),
            [IconKind.DesktopLyricsShow] = Geometry.Parse("M3 4H21V17H3Z M8 21H16 M12 17V21 M7 8H17 M7 12H14"),
            [IconKind.DesktopLyricsHide] = Geometry.Parse("M3 4H21V17H3Z M8 21H16 M12 17V21 M7 8H17 M7 12H14 M2 2L22 22"),
            [IconKind.Settings] = GearGeometry(),
            [IconKind.Lock] = Geometry.Parse("M5 10H19V21H5Z M8 10V7C8 1 16 1 16 7V10 M12 14V17"),
            [IconKind.Unlock] = Geometry.Parse("M5 10H19V21H5Z M8 10V7C8 1 16 1 16 7 M12 14V17"),
            [IconKind.Calendar] = Geometry.Parse("M4 5H20V21H4Z M8 2V7 M16 2V7 M4 10H20 M8 14H9 M12 14H13 M16 14H17 M8 18H9 M12 18H13"),
            [IconKind.Statistics] = Geometry.Parse("M3 3V21H22 M7 16V11H10V16 M13 16V7H16V16 M19 16V4H22V16"),
            [IconKind.Game] = Geometry.Parse("M7 7H17C23 7 24 20 20 20L16 16H8L4 20C0 20 1 7 7 7 M5 12H11 M8 9V15 M17 11H17.1 M20 14H20.1"),
            [IconKind.Plugins] = Geometry.Parse("M4 4H10C8 0 16 0 14 4H20V10C24 8 24 16 20 14V20H14C16 16 8 16 10 20H4V14C0 16 0 8 4 10Z"),
            [IconKind.Help] = Geometry.Parse("M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12 M8 8C8 4 16 4 16 8C16 11 12 11 12 14 M12 18V18.1"),
            [IconKind.Info] = Geometry.Parse("M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12 M12 7V7.1 M12 11V17"),
            [IconKind.History] = Geometry.Parse("M3 8A9 9 0 1 1 3 16 M3 3V8H8 M12 6V12L16 15"),
            [IconKind.Refresh] = Geometry.Parse("M20 8A8 8 0 0 0 5 6L3 9 M3 4V9H8 M4 16A8 8 0 0 0 19 18L21 15 M16 15H21V20"),
            [IconKind.Artist] = Geometry.Parse("M16 7A4 4 0 1 1 8 7A4 4 0 1 1 16 7 M4 21V19C4 12 20 12 20 19V21"),
            [IconKind.Album] = Geometry.Parse("M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12 M15 12A3 3 0 1 1 9 12A3 3 0 1 1 15 12 M6 6L8 8 M18 18L16 16"),
            [IconKind.FileAdd] = Geometry.Parse("M4 2H14L20 8V13 M14 2V8H20 M4 2V22H12 M18 14V22 M14 18H22"),
            [IconKind.Edit] = Geometry.Parse("M4 17L16 5L20 9L8 21H4Z M14 7L18 11 M16 5L18 3L22 7L20 9"),
            [IconKind.Trash] = Geometry.Parse("M3 6H21 M8 6V3H16V6 M5 6L6 21H18L19 6 M10 10V17 M14 10V17"),
            [IconKind.SelectAll] = Geometry.Parse("M3 3H21V21H3Z M7 12L10 15L17 8"),
            [IconKind.Close] = Geometry.Parse("M6 6L18 18 M18 6L6 18"),
            [IconKind.Minimize] = Geometry.Parse("M5 12H19"),
            [IconKind.Maximize] = Geometry.Parse("M5 10V5H10 M14 19H19V14 M5 5L10 10 M19 19L14 14"),
            [IconKind.Home] = Geometry.Parse("M3 11L12 3L21 11 M5 10V21H19V10 M10 21V14H14V21"),
            [IconKind.Terminal] = Geometry.Parse("M3 4H21V20H3Z M6 8L10 12L6 16 M13 16H18"),
            [IconKind.GitHub] = OfficialGitHubMark()
        };

    static VectorIcon() => AffectsRender<VectorIcon>(KindProperty, BrushProperty, FilledProperty);
    private static Geometry GearGeometry()
    {
        var outline = new StreamGeometry();
        using (var drawing = outline.Open())
        {
            bool first = true;
            for (var tooth = 0; tooth < 8; tooth++)
                foreach (var (offset, radius) in new[] { (-19d, 7.8), (-12d, 10d), (12d, 10d), (19d, 7.8) })
                {
                    var angle = (tooth * 45 + offset) * Math.PI / 180; var point = new Point(12 + Math.Cos(angle) * radius, 12 + Math.Sin(angle) * radius);
                    if (first) { drawing.BeginFigure(point, false); first = false; } else drawing.LineTo(point);
                }
            drawing.EndFigure(true);
        }
        return new GeometryGroup { Children = { outline, new EllipseGeometry(new Rect(8.5, 8.5, 7, 7)) } };
    }
    private static Geometry OfficialGitHubMark()
    {
        // 直接读取 GitHub 官方 Octicons 矢量路径，来源和许可见 docs/licenses/Octicons-MIT.txt。
        using var asset = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Nonet/Assets/Icons/mark-github-16.svg"));
        var svg = System.Xml.Linq.XDocument.Load(asset);
        var path = svg.Root!.Elements().Single(e => e.Name.LocalName == "path").Attribute("d")!.Value;
        var geometry = Geometry.Parse(path); geometry.Transform = new ScaleTransform(1.5, 1.5); return geometry;
    }
    public IconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public bool Filled { get => GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 26d;
        if (scale <= 0 || Brush is null) return;
        var offsetX = (Bounds.Width - 24 * scale) / 2;
        var offsetY = (Bounds.Height - 24 * scale) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            var solid = Filled || Kind is IconKind.GitHub or IconKind.Previous or IconKind.Next or IconKind.Play or IconKind.Pause or IconKind.MoveUp or IconKind.MoveDown;
            context.DrawGeometry(solid ? Brush : null,
                solid ? null : new Pen(Brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                Geometries[Kind]);
        }
    }
}
