using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed class CoverCropDialog : Window
{
    public CoverCropDialog(Window owner, AppStorage storage, CropImage image, bool background = false)
    {
        InputCommitService.Install(this);
        DataContext = owner.DataContext; Width = Math.Clamp(owner.Bounds.Width - 32, 440, 1040); Height = Math.Clamp(owner.Bounds.Height - 32, 420, 680);
        WindowDecorations = WindowDecorations.None; WindowStartupLocation = WindowStartupLocation.CenterOwner; CanResize = false;
        var ratio = background ? Math.Clamp(owner.Bounds.Width / Math.Max(1, owner.Bounds.Height), .2, 5) : 1;
        var crop = new CoverCropControl(image, ratio) { Name = "CoverCropPreview", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        NumericUpDown Number(string name, decimal maximum, decimal value) => new() { Name = name, Minimum = 0, Maximum = maximum, Value = value, Increment = 1, FormatString = "0", MinHeight = 32, TextAlignment = Avalonia.Media.TextAlignment.Center };
        var x = Number("CropX", image.Original.Width, (decimal)crop.Crop.X); var y = Number("CropY", image.Original.Height, (decimal)crop.Crop.Y); var side = Number("CropSize", image.Original.Width, (decimal)crop.Crop.Width); side.Minimum = 1;
        var cropHeight = Ui.RawText($"{crop.Crop.Height:0} px", 12, true);
        var zoom = new Slider { Name = "CropZoom", Minimum = 1, Maximum = 8, Value = 1, MinHeight = 32 };
        var updating = false;
        void Sync() { updating = true; try { x.Value = (decimal)crop.Crop.X; y.Value = (decimal)crop.Crop.Y; side.Value = (decimal)crop.Crop.Width; cropHeight.Text = $"{crop.Crop.Height:0} px"; zoom.Value = Math.Clamp(crop.ZoomValue, 1, 8); } finally { updating = false; } }
        void Exact() { if (!updating) crop.SetCrop((double)(x.Value ?? 0), (double)(y.Value ?? 0), (double)(side.Value ?? 1)); }
        InputCommitService.Bind(x, _ => Exact()); InputCommitService.Bind(y, _ => Exact()); InputCommitService.Bind(side, _ => Exact()); crop.CropChanged += (_, _) => Sync();
        zoom.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty && !updating) crop.Zoom(zoom.Value); };
        Control Field(string text, Control input) { var grid = new Grid { ColumnDefinitions = new("75,*"), ColumnSpacing = 6 }; var label = Ui.Text(text, 12, true); label.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(label); Grid.SetColumn(input, 1); grid.Children.Add(input); return grid; }
        var fields = Ui.Stack(Field(L10n.T("Common.XPixels"), x), Field(L10n.T("Common.YPixels"), y), Field(background ? L10n.T("Common.WidthPx") : L10n.T("Common.SidePixels"), side)); fields.Spacing = 6;
        if (background) fields.Children.Add(Field(L10n.T("Common.HeightPx"), cropHeight));
        var parameters = Ui.Stack(); parameters.Spacing = 6;
        if (background) parameters.Children.Add(Ui.Choice([L10n.T("Common.WindowAspectRatio"), L10n.T("Common.OriginalAspectRatio")], L10n.T("Common.WindowAspectRatio"), value => crop.SetAspectRatio(value == L10n.T("Common.WindowAspectRatio") ? ratio : (double)image.Original.Width / image.Original.Height)));
        parameters.Children.Add(Ui.Text(L10n.T("Common.Zoom"), 12, true)); parameters.Children.Add(zoom); parameters.Children.Add(fields);
        parameters.Children.Add(Ui.Text(L10n.T("Common.DragTheImageToChooseTheCropZoomWith"), 11, true));
        var title = Ui.Text(background ? L10n.T("Settings.CropBackgroundImage") : L10n.T("Common.CropCover"), 20); title.FontWeight = Avalonia.Media.FontWeight.SemiBold; title.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var save = Ui.Button(L10n.T("Common.Save"), () => Close(CoverCropService.Export(storage, image, crop.Crop, background)), true); save.Name = "SaveCrop";
        var actions = Ui.Actions(Ui.Button(L10n.T("Common.Cancel"), () => Close()), save); actions.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new Grid { Name = "CropParameters", RowDefinitions = new("*,Auto"), Margin = new(18, 0, 0, 0) }; right.Children.Add(parameters); Grid.SetRow(actions, 1); right.Children.Add(actions);
        var body = new Grid { ColumnDefinitions = new("*,220"), ColumnSpacing = 4 }; body.Children.Add(crop); Grid.SetColumn(right, 1); body.Children.Add(right);
        var layout = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 16 }; layout.Children.Add(title); Grid.SetRow(body, 1); layout.Children.Add(body);
        Content = new Border { Padding = new Thickness(20), Background = Ui.Brush("SurfaceBrush"), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new(1), CornerRadius = new(14), Child = layout };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    public static async Task<string?> Show(Window owner, AppStorage storage, string path)
    {
        using var image = await Task.Run(() => CoverCropService.Load(path));
        return await new CoverCropDialog(owner, storage, image).ShowDialog<string?>(owner);
    }
    public static async Task<string?> ShowBackground(Window owner, AppStorage storage, string path)
    {
        using var image = await Task.Run(() => CoverCropService.Load(path));
        return await new CoverCropDialog(owner, storage, image, true).ShowDialog<string?>(owner);
    }
}
