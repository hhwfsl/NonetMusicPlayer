using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>在应用内承载配置规范表单，不创建独立的系统窗口。</summary>
public sealed class PluginConfigView : UserControl
{
    private readonly MainWindow _owner;
    private readonly MainViewModel _vm;
    private readonly PluginManifest _plugin;
    private readonly JsonObject _schema, _values;
    private readonly Action<bool> _close;
    public JsonObject Draft => _values;
    public PluginConfigView(MainWindow owner, MainViewModel vm, PluginManifest plugin, Action<bool> close)
    {
        _owner = owner; _vm = vm; _plugin = plugin; _close = close;
        _schema = vm.Plugins.ReadConfigurationSchema(plugin); _values = vm.Plugins.ConfigurationValues(plugin);
        Name = "PluginConfiguration"; Focusable = true;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var heading = Ui.Text(L10n.T("Plugins.PluginConfiguration"), 24); heading.FontWeight = FontWeight.SemiBold;
        var header = Ui.Stack(heading, Ui.RawText(plugin.Name + " · " + plugin.Version, 14, true)); header.Margin = new(26, 22, 26, 16);
        // 表单结构只由插件开发者的 Schema 定义，用户只能填写值，不能增删或改名配置字段。
        var form = _schema.Count == 0 ? NoConfiguration() : Fields(_schema, _values, 0); form.Margin = new(26, 0, 26, 0);
        var save = Ui.Button(L10n.T("Common.SaveAndClose"), Save, true); save.Name = "SavePluginConfiguration";
        save.IsVisible = _schema.Count > 0;
        var dismiss = Ui.Button(L10n.T("Common.Close"), () => _close(false)); dismiss.Name = "ClosePluginConfiguration"; ToolTip.SetTip(dismiss, L10n.T(_schema.Count == 0 ? "Common.Close" : "Common.DiscardTheseChanges"));
        var actions = Ui.Actions(save, dismiss); actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new(26, 16, 26, 22);
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto") }; layout.Children.Add(header); Grid.SetRow(form, 1);
        var scroll = Ui.Scroll(form); Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Grid.SetRow(actions, 2); layout.Children.Add(actions);
        Content = new Border { CornerRadius = new(16), Background = Ui.Brush("SurfaceBrush"), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new(1), Child = layout };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { _close(false); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
    public void Save()
    {
        if (_schema.Count == 0) { _close(false); return; }
        _owner.FocusManager?.Focus(null);
        PluginConfigSchema.Validate(_schema, _values);
        if (_vm.CurrentTrack?.ProviderId == _plugin.Id) _vm.StopPlaybackCommand.Execute(null);
        _vm.Plugins.Configure(_plugin, _values.ToJsonString()); _vm.StatusText = L10n.T("Plugins.PluginConfigurationSaved"); _close(true);
    }
    private StackPanel Fields(JsonObject fields, JsonObject values, int depth)
    {
        var panel = Ui.Stack(); panel.Spacing = 0;
        foreach (var (key, node) in fields)
        {
            var field = (JsonObject)node!;
            if (!values.ContainsKey(key)) values[key] = PluginConfigSchema.Default(field);
            var title = field["description"]?.GetValue<string>() ?? key;
            var hint = field["hint"]?.GetValue<string>() ?? "";
            var editor = Value(field, values[key], v => values[key] = v, depth + 1); editor.Name = "PluginConfig_" + key;
            var host = new ContentControl { Content = editor };
            var reset = Ui.Button("", () => { values[key] = PluginConfigSchema.Default(field); var replacement = Value(field, values[key], v => values[key] = v, depth + 1); replacement.Name = "PluginConfig_" + key; host.Content = replacement; });
            reset.Content = new VectorIcon { Kind = IconKind.History, Width = 18, Height = 18, Brush = Ui.Brush("AccentTextBrush") }; reset.Classes.Add("quiet"); ToolTip.SetTip(reset, L10n.T("Common.ResetToDefault"));
            var input = new Grid { ColumnDefinitions = new("*,40"), ColumnSpacing = 8 }; input.Children.Add(host); Grid.SetColumn(reset, 1); input.Children.Add(reset);
            var type = PluginConfigSchema.Type(field);
            Control row = type is "object" or "list" ? Ui.Stack(Ui.RawText(title, 16), Ui.RawText(hint, 12, true), input) : Ui.Row(title, hint, input, ".95*,1.05*");
            panel.Children.Add(new Border { Padding = new(0, 14), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new(0, 0, 0, 1), Child = row });
        }
        return panel;
    }
    private Control Value(JsonObject field, JsonNode? current, Action<JsonNode?> set, int depth)
    {
        if (depth > 8) throw new InvalidDataException(L10n.T("Plugins.PluginConfigurationNestingIsTooDeep"));
        if (field["enum"] is JsonArray options)
        {
            var values = options.Select(item => item is JsonObject option ? option["value"] : item).ToArray();
            var labels = options.Select(item => item is JsonObject option ? option["label"]!.GetValue<string>() : item is JsonValue value && value.TryGetValue<string>(out var text) ? text : item!.ToJsonString()).ToArray();
            var combo = new ComboBox { ItemsSource = labels, SelectedIndex = Array.FindIndex(values, v => JsonNode.DeepEquals(v, current)), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 42 };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set(values[combo.SelectedIndex]?.DeepClone()); }; return combo;
        }
        var type = PluginConfigSchema.Type(field);
        if (type == "bool") { var toggle = Ui.Toggle(current?.GetValue<bool>() ?? false, enabled => set(JsonValue.Create(enabled))); toggle.HorizontalAlignment = HorizontalAlignment.Left; return toggle; }
        if (type == "object")
        {
            var values = current as JsonObject ?? new JsonObject(); if (current is not JsonObject) set(values);
            return field["items"] is JsonObject items && items.Count > 0 ? Fields(items, values, depth) : NoConfiguration();
        }
        if (type == "list")
        {
            var values = current as JsonArray ?? new JsonArray(); if (current is not JsonArray) set(values); var panel = Ui.Stack();
            void Rebuild()
            {
                panel.Children.Clear();
                for (var i = 0; i < values.Count; i++)
                {
                    var index = i; var row = new Grid { ColumnDefinitions = new("*,40"), ColumnSpacing = 8 };
                    row.Children.Add(Value(field["items"] as JsonObject ?? InferField(values[index]), values[index], v => values[index] = v, depth + 1));
                    var remove = Ui.Button("", () => { values.RemoveAt(index); Rebuild(); }); remove.Content = Trash(); ToolTip.SetTip(remove, L10n.T("Common.Remove")); Grid.SetColumn(remove, 1); row.Children.Add(remove); panel.Children.Add(row);
                }
                panel.Children.Add(Ui.Button(L10n.T("Common.AddAnother"), () => { if (values.Count >= 200) throw new InvalidDataException(L10n.T("Common.ListsCannotExceedItems")); values.Add(PluginConfigSchema.Default(field["items"] as JsonObject ?? new JsonObject { ["type"] = "string" })); Rebuild(); }));
            }
            Rebuild(); return panel;
        }
        var text = current is JsonValue scalar && scalar.TryGetValue<string>(out var plain) ? plain : current?.ToJsonString() ?? "";
        var input = new TextBox { Text = text, MinHeight = 42, AcceptsReturn = type == "text", TextWrapping = type == "text" ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxHeight = 160, MaxLength = 16000, PasswordChar = field["ui:widget"]?.GetValue<string>() == "password" ? '●' : '\0' };
        InputCommitService.Bind(input, draft =>
        {
            JsonNode? parsed = type is "int" or "float" or "number" ? JsonNode.Parse(draft) : JsonValue.Create(draft);
            var wrapper = new JsonObject { ["field"] = field.DeepClone() }; var candidate = new JsonObject { ["field"] = parsed?.DeepClone() };
            PluginConfigSchema.Validate(wrapper, candidate); set(parsed);
        }); return input;
    }
    private static StackPanel NoConfiguration() => Ui.Stack(new TextBlock { Name = "PluginNoConfiguration", Text = L10n.T("Plugins.NoConfiguration"), Foreground = Ui.Brush("TextMutedBrush"), FontSize = 14 });
    private static JsonObject InferField(JsonNode? value) => new() { ["type"] = value is JsonObject ? "object" : value is JsonArray ? "list" : value is JsonValue scalar && scalar.TryGetValue<bool>(out _) ? "bool" : value is null || value is JsonValue text && text.TryGetValue<string>(out _) ? "string" : "number" };
    private static VectorIcon Trash() => new() { Kind = IconKind.Trash, Width = 18, Height = 18, Brush = Ui.Brush("TextPrimaryBrush") };
}
