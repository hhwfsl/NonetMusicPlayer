using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>透明弹层采用灰阶抗锯齿，避免亚像素文字边缘在 Popup 中显示黑色条纹。</summary>
public sealed class TooltipRendering : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<TooltipRendering, Control, bool>("Enabled");
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    static TooltipRendering() => EnabledProperty.Changed.AddClassHandler<Control>((control, _) =>
        TextOptions.SetTextRenderingMode(control, GetEnabled(control) ? TextRenderingMode.Antialias : TextRenderingMode.Unspecified));
}
