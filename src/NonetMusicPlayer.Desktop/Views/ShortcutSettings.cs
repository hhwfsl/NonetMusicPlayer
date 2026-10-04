using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using System.Runtime.CompilerServices;

namespace NonetMusicPlayer.Desktop.Views;

internal static class ShortcutSettings
{
    private sealed class DraftState
    {
        public Dictionary<string, string> Saved = [];
        public Dictionary<string, string> Draft = [];
    }
    private static readonly ConditionalWeakTable<MainViewModel, DraftState> Drafts = new();
    public static Border Create(MainViewModel vm)
    {
        var state = Drafts.GetValue(vm, owner => new DraftState { Saved = new(owner.Settings.KeyBindings), Draft = new(owner.Settings.KeyBindings) });
        if (state.Saved.Count != vm.Settings.KeyBindings.Count || state.Saved.Any(pair => !vm.Settings.KeyBindings.TryGetValue(pair.Key, out var value) || pair.Value != value)) { state.Saved = new(vm.Settings.KeyBindings); state.Draft = new(vm.Settings.KeyBindings); }
        var draft = state.Draft;
        var controls = new Dictionary<string, ShortcutCaptureButton>();
        var body = Ui.Card(L10n.T("Settings.KeyboardShortcuts"), Ui.Text(L10n.T("Settings.SelectAKeyToRecordAShortcutEscCancels"), 12, true));
        var stack = (StackPanel)body.Child!;
        var grid = new ResponsiveUniformGrid { MinimumCellWidth = 300, MaximumColumns = 2 };
        foreach (var action in ShortcutService.Actions)
        {
            var capture = new ShortcutCaptureButton(ShortcutService.Get(draft, action), value => draft[action.Id] = value) { MinWidth = 130, HorizontalAlignment = HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(capture, L10n.T(action.Name) + " " + L10n.T("Settings.KeyboardShortcuts")); ToolTip.SetTip(capture, L10n.T("Common.Default") + ": " + action.DefaultGesture);
            controls[action.Id] = capture;
            var clear = Ui.Button(L10n.T("Common.Clear"), () => { draft[action.Id] = ""; capture.SetGesture(""); }); clear.Classes.Add("quiet");
            Avalonia.Automation.AutomationProperties.SetName(clear, L10n.T("Settings.DisableShortcut") + ": " + L10n.T(action.Name));
            var editor = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 }; editor.Children.Add(capture); Grid.SetColumn(clear, 1); editor.Children.Add(clear);
            var row = Ui.Stack(Ui.Text(action.Name), editor); row.Spacing = 6;
            grid.Children.Add(new Border { Padding = new Thickness(0, 4, 16, 12), Child = row, Tag = action.Name + " " + action.Id });
        }
        stack.Children.Add(grid);
        stack.Children.Add(Ui.Actions(Ui.Button(L10n.T("Settings.SaveShortcuts"), () => { ShortcutService.Validate(draft); vm.Settings.KeyBindings = new(draft); state.Saved = new(draft); vm.ApplySettings(); vm.StatusText = L10n.T("Settings.ShortcutsSavedDAE541" ); }, true),
            Ui.Button(L10n.T("Common.RestoreDefaultShortcuts"), () => { draft = state.Draft = ShortcutService.DefaultBindings; foreach (var action in ShortcutService.Actions) controls[action.Id].SetGesture(draft[action.Id]); })));
        return body;
    }
}
