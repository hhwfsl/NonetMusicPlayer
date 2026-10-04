using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>只有进入批量模式后的勾选框可以选择歌曲，行点击和方向键不会改变选择。</summary>
public sealed class TrackListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);
    public static readonly StyledProperty<bool> BatchModeProperty = AvaloniaProperty.Register<TrackListBox, bool>(nameof(BatchMode));
    public bool BatchMode { get => GetValue(BatchModeProperty); set => SetValue(BatchModeProperty, value); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 禁止 ListBox 默认快捷键绕过复选框选择行；窗口处理播放快捷键，复选框自身键盘操作保留。
        if (e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.Space || e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        base.OnKeyDown(e);
    }
}
