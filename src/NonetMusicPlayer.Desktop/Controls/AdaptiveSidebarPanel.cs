using Avalonia;
using Avalonia.Controls;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>短窗口将底部入口并入同一滚动区域，避免固定底栏挤掉整个音乐导航。</summary>
public sealed class AdaptiveSidebarPanel : Grid
{
    protected override Type StyleKeyOverride => typeof(Grid);
    private Control? _footer;
    private bool _scrollAll;
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.FirstOrDefault() is ScrollViewer { Content: Panel content })
        {
            _footer ??= Children.Skip(1).FirstOrDefault();
            var scrollAll = availableSize.Height < 300;
            if (_footer is not null && _scrollAll != scrollAll)
            {
                _scrollAll = scrollAll;
                if (scrollAll && ReferenceEquals(_footer.Parent, this))
                { Children.Remove(_footer); content.Children.Add(_footer); RowDefinitions = new("*"); }
                else if (!scrollAll && ReferenceEquals(_footer.Parent, content))
                { content.Children.Remove(_footer); Grid.SetRow(_footer, 1); Children.Add(_footer); RowDefinitions = new("*,Auto"); }
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
