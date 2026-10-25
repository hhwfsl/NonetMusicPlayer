using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>按类型叠加风格，并以可释放绑定覆盖属性。仅附加/状态变化时匹配，静止时无帧循环。</summary>
public sealed class StyleLayerService : IDisposable
{
    private static readonly ConditionalWeakTable<Control, object> MotionOwners = new();
    internal static bool OwnsMotion(Control control) => MotionOwners.TryGetValue(control, out _);
    private readonly Control _root;
    private readonly Dictionary<string, List<StyleSheet>> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<Control, Entry> _controls = [];
    private readonly Dictionary<string, object?> _baseResources = [];
    private readonly List<Action> _restorePalette = [];
    private readonly Dictionary<SolidColorBrush, Color> _restoreBrushes = [];
    private readonly Dictionary<Channel, Tween> _animations = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly IDisposable _loaded;
    private bool _disposed, _refreshing;
    private List<StyleSheet> Sheets => _owners.Values.SelectMany(s => s).ToList();
    public ResolvedStyleMorph? Morph
    {
        get
        {
            var layers = Sheets.Select(s => s.Morph).OfType<StyleMorph>().ToArray(); if (layers.Length == 0) return null;
            return new(layers.Select(m => m.Enabled).LastOrDefault(v => v.HasValue) ?? true,
                layers.Select(m => m.DurationMs).LastOrDefault(v => v.HasValue) ?? 360,
                layers.Select(m => m.CornerRadius).LastOrDefault(v => v.HasValue) ?? 28,
                layers.Select(m => m.Arc).LastOrDefault(v => v.HasValue) ?? 28);
        }
    }
    public bool WindowMorphEnabled => Sheets.Select(s => s.Morph?.WindowEnabled).LastOrDefault(v => v.HasValue) ?? true;
    public int TrackedControlCount => _controls.Count;
    public event Action<string, Exception>? Faulted;
    public StyleLayerService(Control root)
    {
        _root = root; _timer.Tick += Tick;
        _loaded = Control.LoadedEvent.AddClassHandler<Control>((control, args) => { if (ReferenceEquals(args.Source, control) && !_disposed && _owners.Count > 0) Track(control); });
    }
    public JsonObject Catalog() => new() { ["success"] = true, ["capability"] = StyleSheetContract.Capability,
        ["types"] = Array(StyleSheetContract.ControlTypes), ["properties"] = Array(StyleSheetContract.Properties),
        ["resources"] = Array(StyleSheetContract.ResourceNames), ["order"] = "layers are bottom-to-top; later owners win per property",
        ["activeLayers"] = new JsonArray(_owners.SelectMany(o => o.Value.Select(s => (JsonNode)new JsonObject { ["owner"] = o.Key, ["id"] = s.Id, ["name"] = s.Name })).ToArray()) };
    private static JsonArray Array(IEnumerable<string> items) => new(items.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    /// <summary>先完整解析后替换；失败时保留原风格栈。空列表等价于只撤销该插件的覆盖。</summary>
    public void Apply(string owner, IReadOnlyList<StyleSheet> layers)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StyleLayerService));
        if (layers.Any(s => s is null) || layers.Count > 24 || layers.Select(s => s.Id).Distinct().Count() != layers.Count || _owners.Where(o => o.Key != owner).Sum(o => o.Value.Count) + layers.Count > 48) throw new InvalidDataException("Too many or duplicate style layers.");
        var copy = layers.Select(s => { StyleSheetContract.Validate(s); return JsonSerializer.Deserialize(JsonSerializer.Serialize(s, StyleSheetJson.Default.StyleSheet), StyleSheetJson.Default.StyleSheet)!; }).ToList();
        foreach (var sheet in copy)
        {
            foreach (var set in new[] { sheet.Resources, sheet.Light, sheet.Dark }) foreach (var (key, value) in set) ParseResource(key, value);
            foreach (var rule in sheet.Rules) foreach (var (key, value) in rule.Setters) ParseValue(key, value!, validateOnly: true);
        }
        var previous = _owners.GetValueOrDefault(owner); _owners.Remove(owner);
        if (copy.Count > 0) _owners.Add(owner, copy);
        try { Refresh(); }
        catch { _owners.Remove(owner); if (previous is not null) _owners.Add(owner, previous); Refresh(); throw; }
    }
    public void Remove(string owner) { if (_owners.Remove(owner)) Refresh(); }
    public void SuspendResources()
    {
        var resources = Application.Current!.Resources;
        foreach (var (key, value) in _baseResources) { if (value is null) resources.Remove(key); else resources[key] = value; }
        _baseResources.Clear();
        foreach (var (brush, color) in _restoreBrushes) brush.Color = color; _restoreBrushes.Clear();
        foreach (var restore in _restorePalette) restore(); _restorePalette.Clear();
    }
    public void Refresh()
        => RefreshCore(preserveEntries: false);
    /// <summary>设置更新仅重算资源和现有属性通道，不撤销控件样式或重播入场效果。</summary>
    internal void RefreshAppearance() => RefreshCore(preserveEntries: true);
    private void RefreshCore(bool preserveEntries)
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        try
        {
            SuspendResources();
            var resources = Application.Current!.Resources;
            foreach (var sheet in Sheets)
                foreach (var set in new[] { sheet.Resources, Application.Current.ActualThemeVariant == ThemeVariant.Dark ? sheet.Dark : sheet.Light })
                    foreach (var (key, value) in set)
                    {
                        if (!_baseResources.ContainsKey(key)) _baseResources[key] = resources.TryGetValue(key, out var original) ? original : null;
                        var parsed = ParseResource(key, value);
                        // 旧页面可能直接持有画刷；原位更新颜色才能同步这些控件，撤销时还原原颜色。
                        if (resources.TryGetValue(key, out var current) && current is SolidColorBrush brush && parsed is ISolidColorBrush replacement)
                        { _restoreBrushes.TryAdd(brush, brush.Color); brush.Color = replacement.Color; }
                        else resources[key] = parsed;
                    }
            if (_baseResources.ContainsKey("AccentBrush") && resources["AccentBrush"] is ISolidColorBrush accent)
                foreach (var palette in Application.Current.Styles.OfType<FluentTheme>().SelectMany(t => t.Palettes.Values))
                { var original = palette.Accent; _restorePalette.Add(() => palette.Accent = original); palette.Accent = accent.Color; }
            foreach (var control in _root.GetVisualDescendants().OfType<Control>()) control.InvalidateVisual();
            if (preserveEntries)
            {
                foreach (var entry in _controls.Values.ToArray()) entry.Update(initial: true);
            }
            else
            {
                foreach (var entry in _controls.Values.ToArray()) entry.Dispose();
                _controls.Clear(); _animations.Clear(); _timer.Stop();
            }
            if (_owners.Count == 0) return;
            foreach (var window in (Application.Current.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows.ToArray() ?? [])
                foreach (var control in window.GetVisualDescendants().OfType<Control>().Prepend(window).ToArray()) Track(control);
            foreach (var control in _root.GetVisualDescendants().OfType<Control>().Concat(_root.GetLogicalDescendants().OfType<Control>()).Prepend(_root).Distinct().ToArray()) Track(control);
            // 只模糊背景图本身，绝不对承载文字的面板施加 BlurEffect。
            if (!preserveEntries && _root.GetVisualDescendants().OfType<Image>().FirstOrDefault(i => i.Name == "AppBackgroundImage") is { } image && Sheets.Select(s => s.BackgroundBlur).LastOrDefault(b => b is not null) is { } blur)
            {
                if (!_controls.TryGetValue(image, out var entry)) _controls[image] = entry = new(this, image);
                entry.Extras.Add(image.Bind(Visual.EffectProperty, new Channel(new BlurEffect { Radius = blur }), BindingPriority.Animation));
            }
        }
        finally { _refreshing = false; }
    }
    private void Track(Control control)
    {
        if (_disposed || _controls.ContainsKey(control) || control is TopLevel || control.Name == "WindowSurface" || control.Name?.StartsWith("NonetMorph", StringComparison.Ordinal) == true) return;
        var rules = Sheets.SelectMany(s => s.Rules).Where(r => MatchesType(control, r, ignoreClasses: true)).ToList();
        if (rules.Count == 0) return;
        try { var entry = new Entry(this, control) { Rules = rules }; _controls.Add(control, entry); entry.Update(initial: true); }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error)
    {
        var owner = _owners.Keys.LastOrDefault();
        AppLog.Warning("Styles", "Style could not be applied", error);
        if (owner is not null) Dispatcher.UIThread.Post(() => { Remove(owner); Faulted?.Invoke(owner, error); });
    }
    private static bool MatchesType(Control control, StyleRule rule, bool ignoreClasses = false)
    {
        var subject = rule.Part.Length > 0 ? control.TemplatedParent as Control : control;
        if (!ignoreClasses && rule.Scope.Length > 0 && !control.GetVisualAncestors().OfType<Control>().Prepend(control).Any(c => c.Classes.Contains(rule.Scope))) return false;
        if (subject is null || rule.Part.Length > 0 && control.Name != rule.Part || !ignoreClasses && rule.Classes.Any(c => !subject.Classes.Contains(c))) return false;
        for (var type = subject.GetType(); type is not null; type = type.BaseType) if (type.Name == rule.Type) return true;
        return false;
    }
    private static bool MatchesState(Control control, StyleRule rule)
    {
        var subject = rule.Part.Length > 0 ? control.TemplatedParent as Control ?? control : control;
        return rule.States.All(s => s switch
        {
            "pointerover" => subject.IsPointerOver, "pressed" => subject is Button button && button.IsPressed,
            "checked" => subject is ToggleButton toggle && toggle.IsChecked == true || subject is ToggleSwitch sw && sw.IsChecked == true,
            "disabled" => !subject.IsEffectivelyEnabled, "focus" => subject.IsKeyboardFocusWithin, _ => false
        });
    }
    private static AvaloniaProperty? Property(Control control, string name)
        => control is VectorIcon && name == "foreground" ? VectorIcon.BrushProperty : AvaloniaPropertyRegistry.Instance.GetRegistered(control.GetType()).FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !p.IsDirect && !p.IsReadOnly);
    private static object ParseResource(string key, string text) => key == "ControlCornerRadius" ? Radius(text) : new SolidColorBrush(Color.Parse(text));
    private static CornerRadius Radius(string text)
    {
        var value = CornerRadius.Parse(text);
        if (new[] { value.TopLeft, value.TopRight, value.BottomLeft, value.BottomRight }.Any(v => !double.IsFinite(v) || v < 0 || v > 160)) throw new InvalidDataException("Corner radius out of range.");
        return value;
    }
    private static object ParseValue(string key, JsonNode value, bool validateOnly = false)
    {
        if (key is "background" or "foreground" or "borderBrush" or "fill" or "stroke")
        {
            var text = value.GetValue<string>();
            if (text.StartsWith('@'))
            {
                if (!StyleSheetContract.ResourceNames.Contains(text[1..])) throw new InvalidDataException("Unknown brush resource.");
                return validateOnly ? Brushes.Transparent : Application.Current!.FindResource(text[1..])!;
            }
            return new SolidColorBrush(Color.Parse(text));
        }
        if (key == "cornerRadius") return Radius(value.ToString());
        if (key is "padding" or "margin" or "borderThickness")
        {
            var thickness = Thickness.Parse(value.ToString());
            if (new[] { thickness.Left, thickness.Right, thickness.Top, thickness.Bottom }.Any(v => !double.IsFinite(v) || v < (key == "margin" ? -128 : 0) || v > 256)) throw new InvalidDataException("Thickness out of range.");
            return thickness;
        }
        if (key == "boxShadow")
        {
            var shadow = BoxShadows.Parse(value.GetValue<string>());
            if (shadow.Count > 2 || Enumerable.Range(0, shadow.Count).Select(i => shadow[i]).Any(s => !double.IsFinite(s.Blur) || s.Blur > 64 || Math.Abs(s.OffsetX) > 128 || Math.Abs(s.OffsetY) > 128 || Math.Abs(s.Spread) > 64)) throw new InvalidDataException("Shadow out of range.");
            return shadow;
        }
        if (key == "fontWeight") return Enum.TryParse<FontWeight>(value.ToString(), true, out var weight) && Enum.IsDefined(weight) ? weight : throw new InvalidDataException("Invalid font weight.");
        if (!double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < 0 || number > (key == "opacity" ? 1 : key == "fontSize" ? 96 : 512)) throw new InvalidDataException("Style number out of range.");
        return number;
    }
    private void Animate(Channel channel, object? target, int duration, string curve, Action? finished = null)
    {
        _animations.Remove(channel);
        if (!InterfaceMotion.IsEnabled || duration == 0 || Equals(channel.Value, target)) { channel.Set(target); finished?.Invoke(); return; }
        _animations[channel] = new(channel.Value, target, Environment.TickCount64, duration, curve, finished);
        _timer.Start();
    }
    private void Tick(object? sender, EventArgs args)
    {
        foreach (var (channel, tween) in _animations.ToArray())
        {
            var t = Math.Clamp((Environment.TickCount64 - tween.Started) / (double)tween.Duration, 0, 1);
            channel.Set(Interpolate(tween.From, tween.To, Ease(t, tween.Curve)));
            if (t >= 1) { _animations.Remove(channel); tween.Finished?.Invoke(); }
        }
        if (_animations.Count == 0) _timer.Stop();
    }
    public static double Ease(double t, string curve = "parabolic") => curve switch { "linear" => t, "cubic-out" => 1 - Math.Pow(1 - t, 3), _ => 1 - (1 - t) * (1 - t) };
    private static object? Interpolate(object? from, object? to, double t)
    {
        static double L(double a, double b, double p) => a + (b - a) * p;
        return (from, to) switch
        {
            (double a, double b) => L(a, b, t),
            (CornerRadius a, CornerRadius b) => new CornerRadius(L(a.TopLeft, b.TopLeft, t), L(a.TopRight, b.TopRight, t), L(a.BottomRight, b.BottomRight, t), L(a.BottomLeft, b.BottomLeft, t)),
            (Thickness a, Thickness b) => new Thickness(L(a.Left, b.Left, t), L(a.Top, b.Top, t), L(a.Right, b.Right, t), L(a.Bottom, b.Bottom, t)),
            (ISolidColorBrush a, ISolidColorBrush b) => new SolidColorBrush(Color.FromArgb((byte)L(a.Color.A, b.Color.A, t), (byte)L(a.Color.R, b.Color.R, t), (byte)L(a.Color.G, b.Color.G, t), (byte)L(a.Color.B, b.Color.B, t))),
            _ => to
        };
    }
    private sealed record Tween(object? From, object? To, long Started, int Duration, string Curve, Action? Finished);
    private sealed class Entry : IDisposable
    {
        private readonly StyleLayerService _service;
        private readonly Control _control;
        private readonly Dictionary<AvaloniaProperty, (Channel Channel, IDisposable Binding)> _values = [];
        private readonly Control? _stateOwner;
        private ScaleTransform? _scale;
        private TranslateTransform? _translate;
        private Channel? _scaleX, _scaleY, _offset;
        public List<StyleRule> Rules { get; set; } = [];
        public List<IDisposable> Extras { get; } = [];
        public Entry(StyleLayerService service, Control control)
        {
            _service = service; _control = control; _stateOwner = control.TemplatedParent as Control;
            control.PropertyChanged += Changed; control.Classes.CollectionChanged += ClassesChanged; control.DetachedFromVisualTree += Detached;
            if (_stateOwner is not null) { _stateOwner.PropertyChanged += Changed; _stateOwner.Classes.CollectionChanged += ClassesChanged; }
        }
        private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            // 同一事务中的界面重挂接保留动画通道，避免播放栏等控件短暂还原或重复入场。
            Dispatcher.UIThread.Post(() =>
            {
                if (!_control.IsAttachedToVisualTree() && _service._controls.GetValueOrDefault(_control) == this)
                { Dispose(); _service._controls.Remove(_control); }
            }, DispatcherPriority.Background);
        }
        private void ClassesChanged(object? sender, NotifyCollectionChangedEventArgs args) => Update();
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property.Name is "IsPointerOver" or "IsPressed" or "IsChecked" or "IsEnabled" or "IsEffectivelyEnabled" or "IsKeyboardFocusWithin") Update();
        }
        public void Update(bool initial = false)
        {
            try
            {
                var desired = new Dictionary<AvaloniaProperty, (object Value, StyleRule Rule)>(); StyleRule? motionRule = null;
                var motion = new StyleMotion();
                foreach (var rule in Rules.Where(r => MatchesType(_control, r) && MatchesState(_control, r)))
                {
                    foreach (var (key, value) in rule.Setters) if (Property(_control, key) is { } property) desired[property] = (ParseValue(key, value!), rule);
                    if (rule.Motion is { } definition)
                    {
                        motionRule = rule;
                        motion.PressScale = definition.PressScale ?? motion.PressScale; motion.HoverScale = definition.HoverScale ?? motion.HoverScale;
                        motion.EnterOffsetY = definition.EnterOffsetY ?? motion.EnterOffsetY; motion.EnterDurationMs = definition.EnterDurationMs ?? motion.EnterDurationMs;
                    }
                }
                foreach (var (property, (channel, binding)) in _values.ToArray())
                    if (!desired.ContainsKey(property)) { _service._animations.Remove(channel); binding.Dispose(); _values.Remove(property); }
                foreach (var (property, definition) in desired)
                {
                    if (!_values.TryGetValue(property, out var item)) { var channel = new Channel(_control.GetValue(property)); _values[property] = item = (channel, _control.Bind(property, channel, BindingPriority.Animation)); }
                    _service.Animate(item.Channel, definition.Value, initial ? 0 : definition.Rule.DurationMs, definition.Rule.Curve);
                }
                if (motionRule is not null && _control.TemplatedParent is null)
                {
                    if (_scale is null)
                    {
                        MotionOwners.GetValue(_control, _ => new object());
                        _scale = new(1, 1); _translate = new(0, initial ? motion.EnterOffsetY ?? 0 : 0);
                        var transform = new TransformGroup(); if (_control.RenderTransform is Transform original) transform.Children.Add(original);
                        transform.Children.Add(_scale); transform.Children.Add(_translate);
                        Extras.Add(_control.Bind(Visual.RenderTransformProperty, new Channel(transform), BindingPriority.Animation));
                        Extras.Add(_control.Bind(Visual.RenderTransformOriginProperty, new Channel(RelativePoint.Center), BindingPriority.Animation));
                        _scaleX = new(1d); _scaleY = new(1d); _offset = new(_translate.Y);
                        Extras.Add(_scale.Bind(ScaleTransform.ScaleXProperty, _scaleX, BindingPriority.Animation)); Extras.Add(_scale.Bind(ScaleTransform.ScaleYProperty, _scaleY, BindingPriority.Animation));
                        Extras.Add(_translate.Bind(TranslateTransform.YProperty, _offset, BindingPriority.Animation));
                        _service.Animate(_offset, 0d, motion.EnterDurationMs ?? 0, motionRule.Curve);
                    }
                    var target = _control is Button { IsPressed: true } ? motion.PressScale ?? 1 : _control.IsPointerOver ? motion.HoverScale ?? 1 : 1d;
                    _service.Animate(_scaleX!, target, motionRule.DurationMs, motionRule.Curve); _service.Animate(_scaleY!, target, motionRule.DurationMs, motionRule.Curve);
                }
                else if (_scale is not null)
                {
                    // 状态限定的动画规则不再匹配时必须回到中性姿态，不能停留在悬停缩放位置。
                    _service.Animate(_scaleX!, 1d, 160, "parabolic"); _service.Animate(_scaleY!, 1d, 160, "parabolic");
                }
            }
            catch (Exception error) { _service.Fail(error); }
        }
        public void Dispose()
        {
            _control.PropertyChanged -= Changed; _control.Classes.CollectionChanged -= ClassesChanged; _control.DetachedFromVisualTree -= Detached;
            if (_stateOwner is not null) { _stateOwner.PropertyChanged -= Changed; _stateOwner.Classes.CollectionChanged -= ClassesChanged; }
            foreach (var item in _values.Values) { _service._animations.Remove(item.Channel); item.Binding.Dispose(); } _values.Clear();
            foreach (var channel in new[] { _scaleX, _scaleY, _offset }.OfType<Channel>()) _service._animations.Remove(channel);
            foreach (var binding in Extras.AsEnumerable().Reverse()) binding.Dispose(); Extras.Clear();
            if (_scale is not null) MotionOwners.Remove(_control);
        }
    }
    private sealed class Channel(object? value) : IObservable<object>
    {
        private IObserver<object>? _observer;
        public object? Value { get; private set; } = value;
        public IDisposable Subscribe(IObserver<object> observer) { _observer = observer; observer.OnNext(Value!); return new ElementLayoutService.ActionLease(() => _observer = null); }
        public void Set(object? value) { Value = value; _observer?.OnNext(value!); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _loaded.Dispose(); _timer.Stop(); _animations.Clear();
        foreach (var entry in _controls.Values.ToArray()) entry.Dispose(); _controls.Clear(); _owners.Clear(); SuspendResources();
    }
}

/// <summary>叠加后补齐默认值的动画参数，避免上层省略字段清除下层动画。</summary>
public sealed record ResolvedStyleMorph(bool Enabled, int DurationMs, double CornerRadius, double Arc);
