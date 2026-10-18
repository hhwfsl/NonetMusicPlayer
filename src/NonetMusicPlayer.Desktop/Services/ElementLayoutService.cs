using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>控件级布局的可撤销覆盖层。只在导航或插件变更时扫描，不在每帧和播放进度更新时遍历控件树。</summary>
public sealed class ElementLayoutService(Control root) : IDisposable
{
    private sealed record Identity(string Value);
    private readonly ConditionalWeakTable<Control, Identity> _identities = new();
    private readonly Dictionary<string, JsonArray> _plans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IDisposable>> _leases = new(StringComparer.Ordinal);
    private int _nextId;
    private string Id(Control control) => _identities.GetValue(control, _ => new("element:" + ++_nextId)).Value;
    private Control[] Controls() => root.GetVisualDescendants().OfType<Control>()
        .Concat(root.GetLogicalDescendants().OfType<Control>()).Prepend(root).Distinct().Take(4096).ToArray();
    private Control? Find(string id, Control[] controls) => controls.FirstOrDefault(c => Id(c) == id)
        ?? (controls.Count(c => c.Name == id) == 1 ? controls.Single(c => c.Name == id) : null);
    public Control? FindTarget(string id) => Find(id, Controls());

    /// <summary>只公布类型、名字和布局结构，不读取文本、绑定值、密码或配置。</summary>
    public JsonObject Catalog(int offset = 0, int limit = 100)
    {
        var controls = Controls(); offset = Math.Max(0, offset); limit = Math.Clamp(limit, 1, 200);
        return new() { ["success"] = true, ["total"] = controls.Length, ["offset"] = offset,
            ["properties"] = new JsonArray(PropertyNames.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["elements"] = new JsonArray(controls.Skip(offset).Take(limit).Select(c => (JsonNode)new JsonObject
            {
                ["id"] = Id(c), ["name"] = c.Name, ["type"] = c.GetType().Name,
                ["parent"] = c.Parent is Control parent ? Id(parent) : null,
                ["canReparent"] = c.Parent is Panel, ["acceptsChildren"] = c is Panel,
                ["width"] = c.Bounds.Width, ["height"] = c.Bounds.Height
            }).ToArray()) };
    }
    public static readonly string[] PropertyNames = ["width", "height", "minWidth", "minHeight", "maxWidth", "maxHeight", "margin", "padding",
        "horizontalAlignment", "verticalAlignment", "row", "column", "rowSpan", "columnSpan", "zIndex", "opacity", "visible",
        "fontSize", "orientation", "spacing", "rows", "columns", "left", "top", "dock"];

    public void Apply(string owner, JsonArray patches)
    {
        if (patches.Count is 0 or > ElementLayoutContract.MaximumPatches) throw new InvalidDataException("Expected 1–256 element patches.");
        var controls = Controls();
        // 完整校验先于任何写入；已有插件的覆盖保留到新方案成功，失败后重新应用旧方案。
        Validate(patches, controls, strict: true);
        var previous = _plans.GetValueOrDefault(owner);
        Suspend(); _plans[owner] = (JsonArray)patches.DeepClone();
        try { Refresh(strict: true); }
        catch { Suspend(); if (previous is null) _plans.Remove(owner); else _plans[owner] = previous; Refresh(); throw; }
    }
    private void Validate(JsonArray patches, Control[] controls, bool strict)
    {
        foreach (var node in patches)
        {
            var patch = node as JsonObject ?? throw new InvalidDataException("Expected an element patch.");
            var id = patch["target"]?.GetValue<string>() ?? "";
            if (id.Length is 0 or > 240) throw new InvalidDataException("Invalid target.");
            var control = Find(id, controls);
            if (control is null) { if (strict) throw new InvalidDataException("Element not found: " + id); continue; }
            if (ReferenceEquals(control, root)) throw new InvalidDataException("Use the shell's content rather than the native window.");
            foreach (var property in patch["properties"]?.AsObject() ?? []) Resolve(control, property.Key, property.Value);
            if (patch["parent"]?.GetValue<string>() is { Length: > 0 } parentId)
            {
                var parent = Find(parentId, controls);
                if (parent is null && !strict) continue;
                if (parent is not Panel || control.Parent is not Panel || ReferenceEquals(parent, control)
                    || ((Control)parent).GetLogicalAncestors().Contains(control)) throw new InvalidDataException("Reparenting needs non-cyclic Panel children.");
            }
        }
    }
    public void Refresh(bool strict = false)
    {
        if (_leases.Count > 0) return;
        var controls = Controls();
        var occupied = new HashSet<(Control, string)>();
        try
        {
            foreach (var (owner, patches) in _plans)
            {
                Validate(patches, controls, strict);
                var leases = new List<IDisposable>(); _leases.Add(owner, leases);
                foreach (var patch in patches.OfType<JsonObject>())
                {
                    var control = Find(patch["target"]!.GetValue<string>(), controls); if (control is null) continue;
                    foreach (var (name, value) in patch["properties"]?.AsObject() ?? [])
                    {
                        if (!occupied.Add((control, name))) throw new InvalidDataException("Element property is already customized: " + name);
                        var (property, parsed) = Resolve(control, name, value);
                        if (property is null && control is Grid grid)
                        {
                            // Grid 的行列定义是 CLR 属性，不存在可覆盖的 AvaloniaProperty。
                            if (name == "rows") { var savedRows = grid.RowDefinitions; grid.RowDefinitions = (RowDefinitions)parsed; leases.Add(new ActionLease(() => grid.RowDefinitions = savedRows)); }
                            else { var savedColumns = grid.ColumnDefinitions; grid.ColumnDefinitions = (ColumnDefinitions)parsed; leases.Add(new ActionLease(() => grid.ColumnDefinitions = savedColumns)); }
                            continue;
                        }
                        // Animation 优先级的可释放绑定不会销毁原 LocalValue、数据绑定或动态主题资源。
                        leases.Add(control.Bind(property!, new Constant(parsed), BindingPriority.Animation));
                    }
                    if (patch["parent"]?.GetValue<string>() is { Length: > 0 } parentId && Find(parentId, controls) is Panel parent && control.Parent is Panel original)
                    {
                        if (ReferenceEquals(parent, control) || parent.GetLogicalAncestors().Contains(control)) throw new InvalidDataException("Cyclic parent change.");
                        if (!occupied.Add((control, "parent"))) throw new InvalidDataException("Element parent is already customized.");
                        var index = original.Children.IndexOf(control); var targetIndex = patch["index"]?.GetValue<int>() ?? -1;
                        original.Children.Remove(control); parent.Children.Insert(targetIndex < 0 ? parent.Children.Count : Math.Clamp(targetIndex, 0, parent.Children.Count), control);
                        leases.Add(new ActionLease(() => { if (control.Parent is Panel current) current.Children.Remove(control); if (control.Parent is null) original.Children.Insert(Math.Min(index, original.Children.Count), control); }));
                    }
                }
            }
        }
        catch { Suspend(); throw; }
    }
    /// <summary>导航拆树前撤销覆盖；页面创建完成后再次按名字定位，离开页面不保留其控件实例。</summary>
    public void Suspend()
    {
        foreach (var leases in _leases.Values.Reverse()) foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose();
        _leases.Clear();
    }
    public void Remove(string owner) { Suspend(); _plans.Remove(owner); Refresh(); }
    public void Dispose() { Suspend(); _plans.Clear(); }
    private static (AvaloniaProperty? Property, object Value) Resolve(Control c, string name, JsonNode? value)
    {
        double Number(double minimum = 0, double maximum = 8192)
        {
            // JsonValue<int> 与 JsonValue<double> 都是合法 JSON 数字，不依赖其构造时的 CLR 类型。
            if (value is null || !double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw new InvalidDataException("Missing number.");
            if (!double.IsFinite(number) || number < minimum || number > maximum) throw new InvalidDataException("Layout number out of range.");
            return number;
        }
        int Integer(int minimum, int maximum) { var number = Number(minimum, maximum); if (number != Math.Truncate(number)) throw new InvalidDataException("Expected integer."); return (int)number; }
        string Text() => value?.GetValue<string>() ?? throw new InvalidDataException("Missing string.");
        Thickness ThicknessValue()
        {
            var text = Text(); var parts = text.Split(',');
            if (parts.Length is not (1 or 2 or 4) || parts.Any(s => !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || Math.Abs(n) > 512)) throw new InvalidDataException("Invalid thickness.");
            return Thickness.Parse(text);
        }
        T Choice<T>() where T : struct, Enum => Enum.TryParse<T>(Text(), true, out var result) && Enum.IsDefined(result) ? result : throw new InvalidDataException("Invalid layout choice.");
        string Tracks()
        {
            var text = Text(); var tracks = text.Split(',');
            if (tracks.Length is 0 or > 64 || tracks.Any(t => { var s = t.Trim(); if (s.Equals("Auto", StringComparison.OrdinalIgnoreCase) || s == "*") return false; if (s.EndsWith('*')) s = s[..^1]; return !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < 0 || n > 8192; })) throw new InvalidDataException("Invalid grid tracks.");
            return text;
        }
        return name switch
        {
            "width" => (Control.WidthProperty, value?.ToString() == "auto" ? double.NaN : Number()),
            "height" => (Control.HeightProperty, value?.ToString() == "auto" ? double.NaN : Number()),
            "minWidth" => (Control.MinWidthProperty, Number()), "minHeight" => (Control.MinHeightProperty, Number()),
            "maxWidth" => (Control.MaxWidthProperty, value is null ? double.PositiveInfinity : Number()), "maxHeight" => (Control.MaxHeightProperty, value is null ? double.PositiveInfinity : Number()),
            "margin" => (Control.MarginProperty, ThicknessValue()),
            "horizontalAlignment" => (Control.HorizontalAlignmentProperty, Choice<HorizontalAlignment>()), "verticalAlignment" => (Control.VerticalAlignmentProperty, Choice<VerticalAlignment>()),
            "row" => (Grid.RowProperty, Integer(0, 255)), "column" => (Grid.ColumnProperty, Integer(0, 255)), "rowSpan" => (Grid.RowSpanProperty, Integer(1, 256)), "columnSpan" => (Grid.ColumnSpanProperty, Integer(1, 256)),
            "zIndex" => (Visual.ZIndexProperty, Integer(-100, 100)), "opacity" => (Visual.OpacityProperty, Number(0, 1)), "visible" => (Visual.IsVisibleProperty, value!.GetValue<bool>()),
            "fontSize" when c is TextBlock => (TextBlock.FontSizeProperty, Number(8, 96)), "fontSize" when c is TemplatedControl => (TemplatedControl.FontSizeProperty, Number(8, 96)),
            "padding" when c is Border => (Border.PaddingProperty, ThicknessValue()), "padding" when c is TemplatedControl => (TemplatedControl.PaddingProperty, ThicknessValue()),
            "orientation" when c is StackPanel => (StackPanel.OrientationProperty, Choice<Orientation>()), "spacing" when c is StackPanel => (StackPanel.SpacingProperty, Number(0, 512)),
            "rows" when c is Grid => (null, new RowDefinitions(Tracks())), "columns" when c is Grid => (null, new ColumnDefinitions(Tracks())),
            "left" => (Canvas.LeftProperty, Number(-8192, 8192)), "top" => (Canvas.TopProperty, Number(-8192, 8192)), "dock" => (DockPanel.DockProperty, Choice<Dock>()),
            _ => throw new InvalidDataException("Unsupported property for " + c.GetType().Name + ": " + name)
        };
    }
    private sealed class Constant(object value) : IObservable<object>
    {
        public IDisposable Subscribe(IObserver<object> observer) { observer.OnNext(value); return new ActionLease(() => { }); }
    }
    public sealed class ActionLease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
