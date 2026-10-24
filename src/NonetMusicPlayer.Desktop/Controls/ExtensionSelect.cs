using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>扩展页面的选择器：方向为显式可选能力，提示开关同时覆盖弹出项。</summary>
public sealed class ExtensionSelect : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);
    public string PopupDirection { get; init; } = "auto";
    public bool SuppressTooltips { get; init; }
    public ExtensionSelect()
    {
        ContainerPrepared += (_, args) =>
        {
            if (!SuppressTooltips) return;
            DisableTips(args.Container);
            args.Container.AttachedToVisualTree -= ContainerAttached;
            args.Container.AttachedToVisualTree += ContainerAttached;
        };
    }
    private static void ContainerAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control container) DisableTips(container);
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (PopupDirection != "auto" && e.NameScope.Find<Popup>("PART_Popup") is { } popup)
        {
            // 使用控件上沿作为锚点，不再把已选项覆盖在触发按钮上。
            popup.Placement = PopupDirection == "top" ? PlacementMode.Top : PlacementMode.Bottom;
            popup.VerticalOffset = 0;
        }
        if (!SuppressTooltips) return;
        ItemTemplate = new FuncDataTemplate<string>((value, _) =>
        {
            var text = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis };
            DisableTips(text); return text;
        });
        DisableTips(this);
    }
    private static void DisableTips(Control control)
    {
        foreach (var child in control.GetVisualDescendants().OfType<Control>().Prepend(control))
        {
            FullTextToolTips.SetEnabled(child, false); ToolTip.SetIsOpen(child, false); ToolTip.SetTip(child, null);
        }
    }
}
