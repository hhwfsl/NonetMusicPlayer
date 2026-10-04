using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>控件局部的完整文本提示，不遍历全局视觉树，也不持有已离开的页面。</summary>
public sealed class FullTextToolTips : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<FullTextToolTips, Control, bool>("Enabled");
    private static readonly AttachedProperty<Hint?> StateProperty = AvaloniaProperty.RegisterAttached<FullTextToolTips, Control, Hint?>("State");
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    static FullTextToolTips() => EnabledProperty.Changed.AddClassHandler<Control>((control, _) =>
    {
        if (GetEnabled(control)) { if (control.GetValue(StateProperty) is null) control.SetValue(StateProperty, new Hint(control)); }
        else { control.GetValue(StateProperty)?.Dispose(); control.ClearValue(StateProperty); }
    });

    private sealed class Hint : IDisposable
    {
        private readonly Control _control;
        private object? _description;
        private bool _updating;
        public Hint(Control control)
        {
            _control = control; _description = ToolTip.GetTip(control);
            control.PropertyChanged += Changed; control.PointerEntered += Entered; control.AttachedToVisualTree += Attached;
            Refresh();
        }
        private void Entered(object? sender, PointerEventArgs e) => Refresh();
        private void Attached(object? sender, VisualTreeAttachmentEventArgs e) => Refresh();
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updating) return;
            if (e.Property == ToolTip.TipProperty) { _description = ToolTip.GetTip(_control); Refresh(); return; }
            if (e.Property.Name is not ("Content" or "Header" or "Text" or "SelectedItem" or "Value" or "IsChecked" or "Name" or "Bounds" or "PasswordChar" or "IsReadOnly")) return;
            Refresh();
            // 即时语言切换可能原位修改组合按钮标题。
            if (_control is TextBlock)
                _control.GetVisualAncestors().OfType<Control>().FirstOrDefault(IsInteractive)?.GetValue(StateProperty)?.Refresh();
        }
        private void Refresh()
        {
            if (_updating) return;
            // 保留手工提供的丰富提示内容，不用自动提示覆盖它。
            if (_description is not null and not string) return;
            var caption = Caption(_control, string.IsNullOrWhiteSpace(_description as string));
            var description = _description as string;
            var tip = Combine(caption, description);
            if (Equals(ToolTip.GetTip(_control), tip)) return;
            _updating = true;
            try { _control.SetCurrentValue(ToolTip.TipProperty, tip); }
            finally { _updating = false; }
        }
        public void Dispose()
        {
            _control.PropertyChanged -= Changed; _control.PointerEntered -= Entered; _control.AttachedToVisualTree -= Attached;
            _control.SetCurrentValue(ToolTip.TipProperty, _description);
        }
    }
    private static bool IsInteractive(Control control) => control is Button or MenuItem or ComboBox or ComboBoxItem or ToggleSwitch or TextBox or NumericUpDown or Slider;
    private static string? Combine(string? name, string? detail)
    {
        name = name?.Trim(); detail = detail?.Trim();
        if (string.IsNullOrEmpty(name)) return string.IsNullOrEmpty(detail) ? null : detail;
        if (string.IsNullOrEmpty(detail) || name.Contains(detail, StringComparison.Ordinal)) return name;
        return detail.Contains(name, StringComparison.Ordinal) ? detail : name + "\n" + detail;
    }
    private static string? Caption(Control control, bool allowIconFallback)
    {
        if (control is TextBlock text)
        {
            // 按钮或选择控件负责整个命中区域的提示；子文本不能遮住动作说明或重复弹出提示。
            if (text.GetVisualAncestors().OfType<Control>().Any(c => IsInteractive(c) || c is ToolTip || c is Views.LyricsView)) return null;
            return text.TextTrimming != Avalonia.Media.TextTrimming.None && text.Bounds.Width > 0 && text.TextLayout.TextLines.Any(l => l.HasCollapsed) ? text.Text : null;
        }
        var name = AutomationProperties.GetName(control);
        if (control is TextBox input)
        {
            // 不把输入中的密码、令牌或搜索文本复制到提示中。
            var value = input.IsReadOnly && input.PasswordChar == '\0' ? input.Text : input.PlaceholderText;
            return Combine(name, value);
        }
        object? content = control switch
        {
            MenuItem item => item.Header,
            ComboBox choice => choice.SelectedItem,
            ToggleSwitch toggle => toggle.IsChecked == true ? toggle.OnContent : toggle.OffContent,
            ContentControl container => container.Content,
            _ => null
        };
        var label = content is VectorIcon && !string.IsNullOrWhiteSpace(name) ? null : ContentText(content, allowIconFallback);
        if (control is ComboBox or ComboBoxItem)
        {
            // 模板可能将存储值转换为文案；提示使用实际显示文本，不直接显示原始配置键或改写用户元数据。
            var displayed = control.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text) && (control is ComboBoxItem || !t.GetVisualAncestors().OfType<ComboBoxItem>().Any()))
                .Select(t => t.Text!).Distinct().ToArray();
            if (displayed.Length > 0) label = string.Join("\n", displayed);
        }
        if (control is Button && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(label))
            name = control.Name switch { "PART_IncreaseButton" => L10n.T("Common.Increase"), "PART_DecreaseButton" => L10n.T("Common.Decrease"), _ => name };
        if (control is NumericUpDown number) label = number.Value?.ToString(L10n.Culture);
        if (control is Slider slider) label = slider.Value.ToString("0.##", L10n.Culture);
        if (control is NumericUpDown or Slider && string.IsNullOrWhiteSpace(name))
            name = control.GetVisualAncestors().OfType<Control>().TakeWhile(c => c is not TopLevel).Select(AutomationProperties.GetName).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        return Combine(name, label);
    }
    private static string? ContentText(object? content, bool allowIconFallback)
    {
        if (content is string text) return text;
        if (content is TextBlock label) return label.Text;
        if (content is ContentControl container) return ContentText(container.Content, allowIconFallback);
        if (content is Control control)
        {
            var labels = control.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && t.Text?.Any(char.IsLetterOrDigit) == true).Select(t => t.Text!).Distinct().ToArray();
            if (labels.Length > 0) return string.Join("\n", labels);
            if (control is VectorIcon icon && allowIconFallback) return L10n.T(icon.Kind switch
            {
                IconKind.Play => L10n.T("Playback.Play"), IconKind.Pause => L10n.T("Common.Pause"), IconKind.Previous => L10n.T("Common.Previous"), IconKind.Next => L10n.T("Common.Next"),
                IconKind.More => L10n.T("Common.More9F07D2"), IconKind.Settings => L10n.T("Common.Settings"), IconKind.Search => L10n.T("Common.Search"), IconKind.Close => L10n.T("Common.Close"),
                IconKind.Plus => L10n.T("Common.Add"), IconKind.Trash => L10n.T("Common.Delete"), IconKind.Edit => "编辑", IconKind.FileAdd => L10n.T("Library.AddMusic"), IconKind.Folder => L10n.T("Common.AddFolder"),
                IconKind.Lyrics => L10n.T("Lyrics.Lyrics"), IconKind.Lock => L10n.T("Lyrics.LockDesktopLyrics"), IconKind.Unlock => L10n.T("Lyrics.UnlockDesktopLyrics"), IconKind.Info => L10n.T("Common.TrackInformation"),
                IconKind.MoveUp => L10n.T("Playlists.MoveUpInPlaylist"), IconKind.MoveDown => L10n.T("Playlists.MoveDownInPlaylist"), _ => ""
            });
            return AutomationProperties.GetName(control);
        }
        return content?.ToString();
    }
}
