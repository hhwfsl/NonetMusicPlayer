using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed class PluginPetWindow : Window, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly TextBlock _message;
    private readonly PluginPetControl _pet;
    private readonly IReadOnlyList<PluginFlow> _flows;
    private readonly DispatcherTimer _flowTimer;
    private readonly Button _close;
    private readonly Border _frame;
    private readonly List<(Button Button, string Label)> _actionButtons = [];
    private int _seconds;
    public bool IsDisposed { get; private set; }
    public PluginPetControl Pet => _pet;
    public string Message => _message.Text ?? "";
    public PluginPetWindow(PluginManager manager, PluginManifest manifest, MainViewModel vm, PluginPageWidget widget, IReadOnlyList<PluginFlow> flows)
    {
        _vm = vm; _flows = flows;
        Width = 288; SizeToContent = SizeToContent.Height; MaxHeight = 600; CanResize = false;
        WindowDecorations = WindowDecorations.None; ShowInTaskbar = false; Title = widget.Pet!.Name; DataContext = vm;
        _pet = new(widget.Pet); _message = Ui.RawText(widget.Text, 12, true); _message.MaxHeight = 60; _message.ClipToBounds = true;
        _close = Ui.Button("", Close); _close.Content = new VectorIcon { Kind = IconKind.Close, Width = 18, Height = 18, Brush = Ui.Brush("TextSecondaryBrush") }; _close.MinHeight = 36; _close.MinWidth = 36;
        ToolTip.SetTip(_close, L10n.T("Common.ClosePet")); Avalonia.Automation.AutomationProperties.SetName(_close, L10n.T("Common.ClosePet"));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; header.Children.Add(Ui.RawText(widget.Pet.Name, 16)); Grid.SetColumn(_close, 1); header.Children.Add(_close);
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button) BeginMoveDrag(e); };
        var actions = new WrapPanel();
        foreach (var action in widget.Actions ?? [])
        {
            var button = Ui.Button(action.Label, () => PluginHostActions.Execute(manager, manifest, vm, action.Action)); button.Margin = new Thickness(0, 0, 6, 6); button.MinHeight = 40;
            button.Tag = action.Action.Kind; _actionButtons.Add((button, action.Label)); actions.Children.Add(button);
        }
        _frame = new Border { Background = Ui.Brush("SurfaceBrush"), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Child = Ui.Stack(header, _pet, _message, actions) }; Content = _frame;
        _vm.PropertyChanged += VmChanged;
        _vm.SettingsChanged += SettingsChanged; L10n.LanguageChanged += LanguageChanged;
        _flowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _flowTimer.Tick += FlowTick;
        if (flows.Any(f => f.Event == "interval")) _flowTimer.Start();
        Closed += (_, _) => Dispose();
    }
    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentTrack)) return;
        foreach (var flow in _flows.Where(f => f.Event == "track-changed")) ShowMessage(flow.Action.Value);
    }
    private void FlowTick(object? sender, EventArgs e)
    {
        _seconds++;
        foreach (var flow in _flows.Where(f => f.Event == "interval" && _seconds % f.IntervalSeconds == 0)) ShowMessage(flow.Action.Value);
    }
    private void ShowMessage(string text) => _message.Text = text.Replace("{title}", _vm.CurrentTitle, StringComparison.Ordinal).Replace("{artist}", _vm.CurrentArtist, StringComparison.Ordinal);
    private void SettingsChanged(object? sender, EventArgs e)
    {
        _frame.Background = Ui.Brush("SurfaceBrush"); _frame.BorderBrush = Ui.Brush("DividerBrush");
        if (_close.Content is VectorIcon icon) icon.Brush = Ui.Brush("TextSecondaryBrush");
        foreach (var text in _frame.GetVisualDescendants().OfType<TextBlock>()) text.Foreground = Ui.Brush(text == _message ? "TextSecondaryBrush" : "TextPrimaryBrush");
    }
    private void LanguageChanged(object? sender, EventArgs e)
    {
        ToolTip.SetTip(_close, L10n.T("Common.ClosePet")); Avalonia.Automation.AutomationProperties.SetName(_close, L10n.T("Common.ClosePet"));
        foreach (var item in _actionButtons) item.Button.Content = L10n.T(item.Label);
    }
    public void Dispose()
    {
        if (IsDisposed) return; IsDisposed = true; _vm.PropertyChanged -= VmChanged; _vm.SettingsChanged -= SettingsChanged; L10n.LanguageChanged -= LanguageChanged;
        _flowTimer.Stop(); _flowTimer.Tick -= FlowTick; _pet.Dispose();
    }
}
