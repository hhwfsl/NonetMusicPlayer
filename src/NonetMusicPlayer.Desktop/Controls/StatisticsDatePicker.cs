using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>适配主题色的紧凑月历，保留完整日期点击区域并直接选择年份。</summary>
public sealed class StatisticsDatePicker : UserControl
{
    // 使用独立日历，而不是不完整的 CalendarDatePicker 模板；内置选择器失焦时依赖其必需部件。
    public static readonly StyledProperty<DateTime?> SelectedDateProperty =
        AvaloniaProperty.Register<StatisticsDatePicker, DateTime?>(nameof(SelectedDate), coerce: (_, value) => value is { } date ? ClampDate(date) : null);
    public static int MinimumYear => Math.Max(1, DateTime.Today.Year - 100);
    public static int MaximumYear => Math.Min(9999, DateTime.Today.Year + 100);
    private static DateTime ClampDate(DateTime date) => date.Date < new DateTime(MinimumYear, 1, 1) ? new DateTime(MinimumYear, 1, 1)
        : date.Date > new DateTime(MaximumYear, 12, 31) ? new DateTime(MaximumYear, 12, 31) : date.Date;
    public DateTime? SelectedDate { get => GetValue(SelectedDateProperty); set => SetValue(SelectedDateProperty, value is { } date ? ClampDate(date) : null); }
    public event EventHandler? SelectedDateChanged;
    private readonly Button _button;
    private readonly TextBlock _label = new();
    private readonly StackPanel _month = new() { Spacing = 12 };
    private readonly Flyout _flyout;
    private DateTime _displayMonth;
    public Control CalendarContent => _month;
    public StatisticsDatePicker()
    {
        _button = new Button { Name = "StatisticsDateButton", Height = 44, MinHeight = 44, Padding = new(14, 0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new("*,24"), ColumnSpacing = 14 };
        _label.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(_label);
        var icon = new VectorIcon { Kind = IconKind.Calendar, Width = 20, Height = 20 }; Grid.SetColumn(icon, 1); grid.Children.Add(icon); _button.Content = grid;
        Content = _button;
        _flyout = new Flyout { Content = new Border { Name = "StatisticsCalendar", Width = 310, Padding = new(16), CornerRadius = new(16), Background = Ui.Brush("SurfaceBrush"), Child = _month }, Placement = PlacementMode.BottomEdgeAlignedRight };
        _flyout.FlyoutPresenterClasses.Add("statistics-calendar");
        _button.Click += (_, _) => { var selected = ClampDate(SelectedDate ?? DateTime.Today); _displayMonth = new DateTime(selected.Year, selected.Month, 1); RenderMonth(); _flyout.ShowAt(_button); };
        _label.Text = DateTime.Today.ToString("d", L10n.Culture);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SelectedDateProperty) return;
        _label.Text = (SelectedDate ?? DateTime.Today).ToString("d", L10n.Culture);
        SelectedDateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void RenderMonth()
    {
        _month.Children.Clear();
        Button Move(string glyph, int direction)
        {
            var button = Ui.Button(glyph, () => { _displayMonth = _displayMonth.AddMonths(direction); RenderMonth(); });
            button.Classes.Add("quiet"); button.Width = button.Height = button.MinWidth = button.MinHeight = 32; button.Padding = new(0);
            button.Name = direction < 0 ? "StatisticsPreviousMonth" : "StatisticsNextMonth";
            var label = L10n.T(direction < 0 ? L10n.T("Common.PreviousMonth") : L10n.T("Common.NextMonth"));
            ToolTip.SetTip(button, label); Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.IsEnabled = direction < 0 ? _displayMonth.Year > MinimumYear || _displayMonth.Month > 1 : _displayMonth.Year < MaximumYear || _displayMonth.Month < 12;
            return button;
        }
        // 年份列表不可编辑且虚拟化，打开时将已选年份带入视口。
        var year = new ComboBox { Name = "StatisticsYear", ItemsSource = Enumerable.Range(MinimumYear, MaximumYear - MinimumYear + 1).ToArray(), SelectedIndex = _displayMonth.Year - MinimumYear, Width = 108, MaxDropDownHeight = 280, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(year, L10n.T("Common.SelectYear"));
        Avalonia.Automation.AutomationProperties.SetName(year, L10n.T("Common.Year"));
        year.SelectionChanged += (_, _) => { if (year.SelectedItem is not int value) return; _displayMonth = new DateTime(value, _displayMonth.Month, 1); RenderMonth(); };
        var months = Enumerable.Range(1, 12).Select(number => new ComboBoxItem { Content = L10n.Culture.DateTimeFormat.GetAbbreviatedMonthName(number) }).ToArray();
        var month = new ComboBox { Name = "StatisticsMonth", ItemsSource = months, SelectedIndex = _displayMonth.Month - 1, Width = 94, HorizontalContentAlignment = HorizontalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(month, L10n.T("Common.Month"));
        month.SelectionChanged += (_, _) => { if (month.SelectedIndex < 0) return; _displayMonth = new DateTime(_displayMonth.Year, month.SelectedIndex + 1, 1); RenderMonth(); };
        var selectors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { year, month } };
        var header = new Grid { ColumnDefinitions = new("32,*,32") }; header.Children.Add(Move("‹", -1)); Grid.SetColumn(selectors, 1); header.Children.Add(selectors); var next = Move("›", 1); Grid.SetColumn(next, 2); header.Children.Add(next); _month.Children.Add(header);
        var days = new Grid { ColumnDefinitions = new("*,*,*,*,*,*,*"), RowDefinitions = new("28,36,36,36,36,36,36"), ColumnSpacing = 3, RowSpacing = 3 };
        for (var column = 0; column < 7; column++)
        {
            var weekday = new TextBlock { Text = L10n.Culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((column + 1) % 7)), FontSize = 11, Foreground = Ui.Brush("TextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(weekday, column); days.Children.Add(weekday);
        }
        var first = _displayMonth.AddDays(-(((int)_displayMonth.DayOfWeek + 6) % 7));
        for (var index = 0; index < 42; index++)
        {
            if (index > (DateTime.MaxValue.Date - first).Days) continue;
            var day = first.AddDays(index); var selected = day == (SelectedDate ?? DateTime.Today).Date;
            var button = Ui.Button(day.Day.ToString(CultureInfo.CurrentCulture), () => { SelectedDate = day; _flyout.Hide(); });
            button.Name = "StatisticsDay_" + day.ToString("yyyyMMdd"); button.MinWidth = button.MinHeight = 0; button.Height = 36; button.Padding = new(0); button.CornerRadius = new(10);
            button.Classes.Add(selected ? "primary" : "quiet"); button.Opacity = day.Month == _displayMonth.Month ? 1 : .4;
            button.IsEnabled = day.Year >= MinimumYear && day.Year <= MaximumYear;
            if (!selected && day == DateTime.Today) { button.BorderBrush = Ui.Brush("AccentTextBrush"); button.BorderThickness = new(1); }
            Avalonia.Automation.AutomationProperties.SetName(button, day.ToString("D", L10n.Culture));
            Grid.SetColumn(button, index % 7); Grid.SetRow(button, index / 7 + 1); days.Children.Add(button);
        }
        _month.Children.Add(days);
        var today = Ui.Button(L10n.T("Common.Today"), () => { SelectedDate = DateTime.Today; _flyout.Hide(); }); today.Name = "StatisticsToday"; today.Classes.Add("quiet"); _month.Children.Add(today);
    }
}
