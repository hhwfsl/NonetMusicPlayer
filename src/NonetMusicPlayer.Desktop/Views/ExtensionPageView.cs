using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>仅解释通用控件和状态路径，不识别插件 ID、聊天或其他业务。</summary>
public sealed class ExtensionPageView : UserControl, IDisposable, IPluginKeyboardScope
{
    private readonly ExtensionSession _session;
    private readonly PluginManager _manager;
    private readonly Dictionary<string, JsonNode?> _inputs;
    private readonly List<Action<JsonObject>> _bindings = [];
    private bool _disposed, _applyInputs;
    public ExtensionPageView(PluginManager manager, PluginManifest manifest, ExtensionNode? root = null)
    {
        _manager = manager; _session = manager.Extension(manifest); _inputs = _session.Inputs;
        Content = Build(root ?? manager.LoadExtensionPage(manifest).Root, null, _bindings);
        Margin = new(0, 0, 18, 0); _session.Changed += Refresh; manager.UiPluginUnavailable += Unavailable;
        AttachedToVisualTree += async (_, _) =>
        {
            try { await _session.StartAsync(); Refresh(_session.Frame); }
            catch (OperationCanceledException) { }
            catch (Exception error) { manager.ReportExtensionError(error); }
        };
        FullTextToolTips.SetEnabled(this, false);
    }
    private static JsonNode? Value(string path, JsonObject state, JsonNode? item)
    {
        var parts = path.Split('.');
        JsonNode? value = path.StartsWith("item.", StringComparison.Ordinal) ? item : state;
        foreach (var part in parts.Skip(path.StartsWith("item.", StringComparison.Ordinal) ? 1 : 0))
            value = value is JsonObject obj ? obj[part] : null;
        return value;
    }
    private static string String(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var text) ? text : node?.ToJsonString() ?? "";
    private async Task Invoke(ExtensionNode node, JsonNode? item, string? action = null)
    {
        if (_disposed) return;
        var values = new JsonObject();
        foreach (var pair in _inputs) values[pair.Key] = pair.Value?.DeepClone();
        values["parameter"] = node.Parameter.StartsWith("item.", StringComparison.Ordinal)
            ? Value(node.Parameter, _session.Frame.State, item)?.DeepClone() : JsonValue.Create(node.Parameter);
        try { _applyInputs = true; await _session.InvokeAsync(action ?? node.Action, values, userGesture: true); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_session.IsDisposed) _manager.ReportExtensionError(error); }
        finally { _applyInputs = false; }
    }
    private readonly List<IDisposable> _assets = [];
    private Control Build(ExtensionNode node, JsonNode? item, List<Action<JsonObject>> bindings)
    {
        Control control;
        switch (node.Type)
        {
            case "grid":
                var grid = new Grid { ColumnDefinitions = new(node.Columns), RowDefinitions = new(node.Rows),
                    ColumnSpacing = node.Spacing, RowSpacing = node.Spacing };
                foreach (var child in node.Children) grid.Children.Add(Build(child, item, bindings));
                control = grid; break;
            case "stack":
                var stack = new StackPanel { Spacing = node.Spacing, Orientation = node.Horizontal ? Orientation.Horizontal : Orientation.Vertical };
                foreach (var child in node.Children) stack.Children.Add(Build(child, item, bindings));
                control = stack; break;
            case "border":
                var border = new Border { CornerRadius = new(12), Padding = Thickness.Parse(node.Padding) };
                if (node.Background.Length > 0) border.Background = Ui.Brush(node.Background);
                if (node.Children.Count > 0) border.Child = Build(node.Children[0], item, bindings);
                control = border; break;
            case "scroll":
                var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                if (node.Children.Count > 0) scroll.Content = Build(node.Children[0], item, bindings);
                long lastScroll = -1;
                if (node.ScrollToEnd) bindings.Add(_ =>
                {
                    if (_session.Frame.Revision == lastScroll) return;
                    var follow = lastScroll < 0 || scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 50;
                    lastScroll = _session.Frame.Revision;
                    if (follow) Dispatcher.UIThread.Post(() => { if (!_disposed) scroll.ScrollToEnd(); }, DispatcherPriority.Loaded);
                });
                control = scroll; break;
            case "repeat":
                var repeat = new StackPanel { Spacing = node.Spacing };
                var childBindings = new List<Action<JsonObject>>(); var signature = "";
                bindings.Add(state =>
                {
                    var data = Value(node.Bind, state, item) as JsonArray;
                    var next = data?.ToJsonString() ?? "[]";
                    if (signature != next)
                    {
                        signature = next; repeat.Children.Clear(); childBindings.Clear();
                        foreach (var row in data?.Take(1000) ?? [])
                            if (node.Template is not null) repeat.Children.Add(Build(node.Template, row, childBindings));
                    }
                    foreach (var update in childBindings.ToArray()) update(state);
                }); control = repeat; break;
            case "image":
                var image = new Image { Stretch = Stretch.Uniform };
                if (node.Asset.Length > 0)
                {
                    var path = Path.Combine(_manager.ExtensionDirectory(_session.Manifest), node.Asset); PluginPathPolicy.RejectLinkedAncestors(path);
                    if (new FileInfo(path).Length > 10_000_000) throw new InvalidDataException("Extension image is too large.");
                    var bitmap = new Avalonia.Media.Imaging.Bitmap(path); _assets.Add(bitmap); image.Source = bitmap;
                }
                control = image; break;
            case "slider":
                var slider = new Slider { Minimum = node.Minimum, Maximum = node.Maximum };
                bindings.Add(state => { if (!slider.IsPointerOver && Value(node.Bind, state, item) is { } number) slider.Value = number.GetValue<double>(); });
                slider.PointerReleased += async (_, _) => { _inputs[node.Input] = JsonValue.Create(slider.Value); await Invoke(node, item); };
                slider.AddHandler(KeyUpEvent, async (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Home or Key.End) { _inputs[node.Input] = JsonValue.Create(slider.Value); await Invoke(node, item); } });
                control = slider; break;
            case "input":
                var input = new TextBox { AcceptsReturn = node.Multiline, TextWrapping = node.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    PlaceholderText = node.Text, MinHeight = 38, MaxHeight = node.Multiline ? 92 : 42 };
                var prior = String(Value(node.Bind, _session.Frame.State, item)); var updating = false;
                input.MaxLength = 24000;
                if (_inputs.TryGetValue(node.Input, out var retained)) input.Text = String(retained); else input.Text = prior;
                bindings.Add(state =>
                {
                    var text = String(Value(node.Bind, state, item));
                    if (text != prior || _applyInputs)
                    {
                        prior = text; updating = true; input.Text = text; _inputs[node.Input] = JsonValue.Create(text); updating = false;
                    }
                });
                input.TextChanged += (_, _) => { if (!updating) _inputs[node.Input] = JsonValue.Create(input.Text ?? ""); };
                if (node.EnterAction.Length > 0) input.AddHandler(KeyDownEvent, async (_, e) =>
                {
                    // 隧道路由在 TextBox 默认换行处理之前拦截；Shift+Enter 留给原生换行。
                    if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
                    e.Handled = true; await Invoke(node, item, node.EnterAction);
                }, RoutingStrategies.Tunnel, handledEventsToo: true);
                control = input; break;
            case "button":
                var label = Ui.RawText(node.Text); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
                // 先建立组合内容再挂载，避免同一标签同时归属按钮和内部布局。
                Control buttonContent = Enum.TryParse<IconKind>(node.Icon, out var icon)
                    ? Ui.Actions(new VectorIcon { Kind = icon, Width = 18, Height = 18 }, label) : label;
                var button = new Button { Content = buttonContent, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
                ToolTip.SetTip(button, node.Tooltip.Length > 0 ? node.Tooltip : node.Text);
                button.Click += async (_, _) => await Invoke(node, item);
                if (node.Bind.Length > 0) bindings.Add(state => { label.Text = String(Value(node.Bind, state, item)); ToolTip.SetTip(button, node.Tooltip.Length > 0 ? node.Tooltip : label.Text); });
                control = button; break;
            case "toggle":
                var toggle = new ToggleSwitch { Content = node.Text };
                var toggleUpdating = false;
                bindings.Add(state => { toggleUpdating = true; toggle.IsChecked = Value(node.Bind, state, item)?.GetValue<bool>() == true; toggleUpdating = false; });
                toggle.IsCheckedChanged += async (_, _) => { if (toggleUpdating) return; _inputs[node.Input] = JsonValue.Create(toggle.IsChecked == true); await Invoke(node, item); };
                control = toggle; break;
            case "select":
                var select = new ComboBox { MinWidth = 120 };
                var selectSignature = ""; var selectUpdating = false;
                bindings.Add(state =>
                {
                    var data = Value(node.Bind, state, item) as JsonArray; var signature = data?.ToJsonString() ?? "[]";
                    if (signature == selectSignature) return;
                    selectSignature = signature; selectUpdating = true; select.ItemsSource = data?.Select(String).ToArray();
                    if (_inputs.TryGetValue(node.Input, out var selected)) select.SelectedItem = String(selected);
                    else select.SelectedIndex = 0;
                    _inputs[node.Input] = JsonValue.Create(select.SelectedItem as string); selectUpdating = false;
                });
                select.SelectionChanged += async (_, _) => { if (selectUpdating) return; _inputs[node.Input] = JsonValue.Create(select.SelectedItem as string); if (node.Action.Length > 0) await Invoke(node, item); };
                control = select; break;
            default:
                TextBlock text = node.Type == "selectable-text" ? new SelectableTextBlock() : new TextBlock();
                text.Text = node.Text; text.TextWrapping = TextWrapping.Wrap; text.FontSize = node.FontSize; text.Foreground = Ui.Brush("TextPrimaryBrush");
                if (node.Bind.Length > 0) bindings.Add(state => text.Text = String(Value(node.Bind, state, item)));
                control = text; break;
        }
        control.Name = node.Id.Length > 0 ? node.Id : null;
        control.Margin = Thickness.Parse(node.Margin);
        control.HorizontalAlignment = node.Align switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, "center" => HorizontalAlignment.Center, _ => HorizontalAlignment.Stretch };
        if (node.Width > 0) control.Width = node.Width; if (node.Height > 0) control.Height = node.Height;
        Grid.SetRow(control, node.Row); Grid.SetColumn(control, node.Column);
        if (node.VisibleIf.Length > 0) bindings.Add(state => control.IsVisible = Value(node.VisibleIf, state, item)?.GetValue<bool>() == true);
        return control;
    }
    private void Refresh(ExtensionFrame frame)
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Refresh(frame)); return; }
        try { foreach (var binding in _bindings.ToArray()) binding(frame.State); }
        catch (Exception error) { Dispose(); _manager.ReportExtensionError(error); }
    }
    private void Unavailable(string id) { if (id == _session.Manifest.Id) Dispose(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose() { if (_disposed) return; _disposed = true; _session.Changed -= Refresh; _manager.UiPluginUnavailable -= Unavailable; _bindings.Clear(); foreach (var asset in _assets) asset.Dispose(); _assets.Clear(); }
}
