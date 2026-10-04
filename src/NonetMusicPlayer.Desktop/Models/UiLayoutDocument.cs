namespace NonetMusicPlayer.Desktop.Models;

/// <summary>纯声明式布局数据，不包含可执行 XAML、绑定表达式或代码。</summary>
public sealed class UiLayoutDocument
{
    public int SchemaVersion { get; init; } = 2;
    public IReadOnlyDictionary<string, LayoutRect> Workspace { get; init; } = new Dictionary<string, LayoutRect>();
    public required UiPlayerLayout Player { get; init; }
}

public sealed class UiPlayerLayout
{
    public required string[] Rows { get; init; }
    public required string[] Columns { get; init; }
    public required IReadOnlyList<UiLayoutItem> Items { get; init; }
}

public sealed class UiLayoutItem
{
    public string? Id { get; init; }
    public UiPlayerLayout? Grid { get; init; }
    public int Row { get; init; }
    public int Column { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColumnSpan { get; init; } = 1;
    /// <summary>左、上、右、下边距，单位为设备无关像素。</summary>
    public double[] Margin { get; init; } = [0, 0, 0, 0];
    public string HorizontalAlignment { get; init; } = "Stretch";
    public string VerticalAlignment { get; init; } = "Center";
    public double? FontSize { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
}

public sealed class UiLayoutException(string message) : FormatException(message);
