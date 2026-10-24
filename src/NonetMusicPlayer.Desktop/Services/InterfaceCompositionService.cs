using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>将数据树组装为表现层。引用的是原控件，事件、绑定和虚拟化不复制；撤销时原位还原。</summary>
public sealed class InterfaceCompositionService(Control root, Func<InterfaceNode, Control?> factory, Func<string, bool>? matchesPage = null) : IDisposable
{
    private readonly List<IDisposable> _leases = [];
    private bool _refreshing;
    private List<IDisposable>? _pageDisposables;
    private string? _applyingRegion;
    private readonly Dictionary<InterfaceNode, Control> _artwork = [];
    public bool IsApplied => _leases.Count > 0;
    public static readonly string[] ProtectedTargets = ["WindowSurface", "TitleBarSurface", "TitleBarLayout", "TitleButtons", "CloseWindowButton", "MinimizeWindowButton", "MaximizeWindowButton", "FullLyricsHost"];
    private Control[] Controls() => root.GetVisualDescendants().OfType<Control>().Concat(root.GetLogicalDescendants().OfType<Control>()).Prepend(root).Distinct().Take(8192).ToArray();
    public void Apply(IReadOnlyDictionary<string, InterfaceNode> regions)
    {
        if (_refreshing || IsApplied) return;
        _refreshing = true;
        try
        {
            // 封面和模糊背景保留实例及渲染缓存；导航只重排，避免重新进入时出现后加载闪变。
            IEnumerable<InterfaceNode> Nodes(InterfaceNode n) => new[] { n }.Concat(n.Children.SelectMany(Nodes));
            var active = regions.Values.SelectMany(Nodes).Where(n => n.Kind == "artwork").ToHashSet();
            foreach (var stale in _artwork.Keys.Where(k => !active.Contains(k)).ToArray()) _artwork.Remove(stale);
            var controls = Controls(); var references = new HashSet<Control>();
            foreach (var (name, tree) in regions)
            {
                _applyingRegion = name;
                if (ProtectedTargets.Contains(name)) throw new InvalidDataException("Native window and recovery controls are protected.");
                var matches = controls.Where(c => c.Name == name).ToArray();
                // 页面可能尚未创建；命名区域在进入相应页面后才组装，不保留旧页面。
                if (matches.Length == 0) continue;
                if (matches.Length != 1 || matches[0].TemplatedParent is not null) throw new InvalidDataException("Ambiguous or template-managed region: " + name);
                var target = matches[0];
                if (target is not (Panel or Decorator or ContentControl) || ReferenceEquals(target, root)) throw new InvalidDataException("A region must own a child collection.");
                var constructed = Build(tree, controls, references, target);
                ReplaceChildren(target, constructed);
            }
        }
        catch (Exception error) { error.Data["Nonet.InterfaceRegion"] = _applyingRegion; Suspend(); throw; }
        finally { _refreshing = false; _applyingRegion = null; }
    }
    public Control BuildPage(InterfaceNode node)
    {
        // 独立方案页不得借用原界面控件，避免页面退出后持有其他区域的实例。
        if (HasRef(node)) throw new InvalidDataException("Presentation pages use generated controls, not borrowed references.");
        _pageDisposables = [];
        try { return new OwnedPage(Build(node, [], [], null), _pageDisposables); }
        catch { foreach (var disposable in _pageDisposables.AsEnumerable().Reverse()) disposable.Dispose(); throw; }
        finally { _pageDisposables = null; }
    }
    private static bool HasRef(InterfaceNode n) => n.Kind == "ref" || n.Children.Any(HasRef);
    private Control Build(InterfaceNode node, Control[] controls, HashSet<Control> references, Control? target)
    {
        if (node.WhenPage.Length > 0 && matchesPage?.Invoke(node.WhenPage) != true) return new Border();
        Control control;
        var borrowed = node.Kind == "ref";
        if (borrowed)
        {
            var matches = controls.Where(c => c.Name == node.Target).ToArray();
            if (matches.Length == 0 && node.Optional) return new Border();
            var caption = node.Target is "TitleBarSurface" or "FullLyricsHost";
            if (matches.Length != 1 || ProtectedTargets.Contains(node.Target) && !caption) throw new InvalidDataException("Missing, ambiguous or protected reference: " + node.Target);
            if (caption && node.Properties.Select(p => p.Key).Concat(node.NarrowProperties.Select(p => p.Key)).Any(k => k is not ("row" or "column" or "rowSpan" or "columnSpan" or "zIndex" or "margin" or "padding" or "clipToBounds"))) throw new InvalidDataException("Native caption and lyric host may be positioned, not hidden or disabled.");
            control = matches[0];
            if (!references.Add(control) || ReferenceEquals(control, target) || target?.GetLogicalAncestors().Contains(control) == true || control.TemplatedParent is not null) throw new InvalidDataException("Cyclic or duplicate interface reference.");
            Detach(control);
        }
        else if (node.Kind == "artwork" && _pageDisposables is null)
        {
            if (!_artwork.TryGetValue(node, out control!)) _artwork[node] = control = factory(node) ?? throw new InvalidDataException("Artwork unavailable.");
            Release(control);
        }
        else control = node.Kind switch
        {
            "grid" => new Grid(), "stack" => new StackPanel(), "border" => new Border(),
            "scroll" => new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden },
            "canvas" => new Canvas(), "spacer" => new Border(),
            _ => factory(node) ?? throw new InvalidDataException("Unsupported presentation component: " + node.Kind)
        };
        if (!borrowed && control is IDisposable disposable)
        {
            if (_pageDisposables is not null) _pageDisposables.Add(disposable);
            else _leases.Add(new ElementLayoutService.ActionLease(disposable.Dispose));
        }
        foreach (var key in node.Classes) if (!control.Classes.Contains(key)) { control.Classes.Add(key); if (borrowed) _leases.Add(new ElementLayoutService.ActionLease(() => control.Classes.Remove(key))); }
        var properties = (JsonObject)node.Properties.DeepClone();
        var width = root.Bounds.Width > 0 ? root.Bounds.Width : root.Width;
        var height = root.Bounds.Height > 0 ? root.Bounds.Height : root.Height;
        if (width < 900 || height < 620) foreach (var (key, value) in node.NarrowProperties) properties[key] = value?.DeepClone();
        foreach (var (key, value) in properties)
        {
            if (key is "background" or "foreground" or "cornerRadius" or "blur")
            {
                AvaloniaProperty property = key switch { "background" when control is Border => Border.BackgroundProperty, "background" when control is Panel => Panel.BackgroundProperty,
                    "foreground" when control is TextBlock => TextBlock.ForegroundProperty, "foreground" when control is TemplatedControl => TemplatedControl.ForegroundProperty,
                    "cornerRadius" when control is Border => Border.CornerRadiusProperty, "blur" => Visual.EffectProperty, _ => throw new InvalidDataException("Unsupported presentation property.") };
                object parsed = key == "cornerRadius" ? Radius(value!.GetValue<string>()) : key == "blur" ? new BlurEffect { Radius = Blur(value!) } : Brush(value!.GetValue<string>());
                if (!borrowed && key == "blur" && control.Effect is BlurEffect prior && parsed is BlurEffect next && prior.Radius == next.Radius) parsed = prior;
                if (borrowed) _leases.Add(control.Bind(property, new Constant(parsed), BindingPriority.Animation)); else control.SetValue(property, parsed);
            }
            else
            {
                var (property, parsed) = ElementLayoutService.Resolve(control, key, value);
                if (property is null && control is Grid grid)
                {
                    if (key == "rows") { var original = grid.RowDefinitions; grid.RowDefinitions = (RowDefinitions)parsed; if (borrowed) _leases.Add(new ElementLayoutService.ActionLease(() => grid.RowDefinitions = original)); }
                    else { var original = grid.ColumnDefinitions; grid.ColumnDefinitions = (ColumnDefinitions)parsed; if (borrowed) _leases.Add(new ElementLayoutService.ActionLease(() => grid.ColumnDefinitions = original)); }
                }
                else if (borrowed) _leases.Add(control.Bind(property!, new Constant(parsed), BindingPriority.Animation));
                else control.SetValue(property!, parsed);
            }
        }
        foreach (var child in node.Children)
        {
            var content = Build(child, controls, references, target);
            if (control is Panel panel) panel.Children.Add(content);
            else if (control is Decorator decorator) decorator.Child = content;
            else if (control is ContentControl container) container.Content = content;
            else throw new InvalidDataException("This component cannot contain children.");
        }
        return control;
    }
    private static IBrush Brush(string text)
    {
        if (!text.StartsWith('@')) return new SolidColorBrush(Color.Parse(text));
        if (!StyleSheetContract.ResourceNames.Contains(text[1..]) || Application.Current!.Resources[text[1..]] is not IBrush brush) throw new InvalidDataException("Invalid presentation resource.");
        return brush;
    }
    private static CornerRadius Radius(string text)
    {
        var radius = CornerRadius.Parse(text);
        if (new[] { radius.TopLeft, radius.TopRight, radius.BottomLeft, radius.BottomRight }.Any(n => !double.IsFinite(n) || n is < 0 or > 96)) throw new InvalidDataException("Invalid interface corner radius.");
        return radius;
    }
    private static double Blur(JsonNode node)
    {
        var value = node.GetValue<double>();
        return double.IsFinite(value) && value is >= 0 and <= 40 ? value : throw new InvalidDataException("Invalid interface blur.");
    }
    private void Detach(Control control)
    {
        if (control.Parent is Panel panel)
        {
            var index = panel.Children.IndexOf(control); panel.Children.Remove(control);
            _leases.Add(new ElementLayoutService.ActionLease(() => { Release(control); panel.Children.Insert(Math.Min(index, panel.Children.Count), control); }));
        }
        else if (control.Parent is Decorator decorator) { decorator.Child = null; _leases.Add(new ElementLayoutService.ActionLease(() => { Release(control); decorator.Child = control; })); }
        else if (control.Parent is ContentControl content) { content.Content = null; _leases.Add(new ElementLayoutService.ActionLease(() => { Release(control); content.Content = control; })); }
        else throw new InvalidDataException("Reference does not have a detachable parent.");
    }
    private static void Release(Control control)
    {
        if (control.Parent is Panel p) p.Children.Remove(control); else if (control.Parent is Decorator d) d.Child = null; else if (control.Parent is ContentControl c) c.Content = null;
    }
    private void ReplaceChildren(Control target, Control replacement)
    {
        if (target is Panel p)
        {
            var saved = p.Children.ToArray(); p.Children.Clear(); p.Children.Add(replacement);
            _leases.Add(new ElementLayoutService.ActionLease(() => { p.Children.Clear(); foreach (var c in saved) p.Children.Add(c); }));
        }
        else if (target is Decorator d)
        {
            var saved = d.Child; d.Child = null; d.Child = replacement;
            _leases.Add(new ElementLayoutService.ActionLease(() => { d.Child = null; d.Child = saved; }));
        }
        else if (target is ContentControl c)
        {
            var saved = c.Content; c.Content = null; c.Content = replacement;
            _leases.Add(new ElementLayoutService.ActionLease(() => { c.Content = null; c.Content = saved; }));
        }
    }
    public void Suspend() { foreach (var lease in _leases.AsEnumerable().Reverse()) lease.Dispose(); _leases.Clear(); }
    public void Dispose() { Suspend(); _artwork.Clear(); }
    private sealed class OwnedPage : UserControl, IDisposable
    {
        private readonly List<IDisposable> _disposables;
        private bool _disposed;
        public OwnedPage(Control control, List<IDisposable> disposables) { Content = control; _disposables = disposables; }
        public void Dispose() { if (_disposed) return; _disposed = true; foreach (var item in _disposables.AsEnumerable().Reverse()) item.Dispose(); Content = null; }
    }
    private sealed class Constant(object value) : IObservable<object> { public IDisposable Subscribe(IObserver<object> observer) { observer.OnNext(value); return new ElementLayoutService.ActionLease(() => { }); } }
}
