using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>离线原生手册查看器，不依赖浏览器或 Markdown 文件关联。</summary>
public sealed class UserManualWindow : Window
{
    public UserManualWindow()
    {
        Title = L10n.T("Common.UserManual"); Width = 840; Height = 680; MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var name = "UserManual." + L10n.Language + ".md";
        var candidates = new[] { Path.Combine(AppContext.BaseDirectory, name), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs", name)), Path.Combine(Environment.CurrentDirectory, "docs", name) };
        var path = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException(L10n.T("Common.TheUserManualCouldNotBeFound"));
        var stack = new StackPanel { Spacing = 10, Margin = new(24), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var level = line.TakeWhile(c => c == '#').Count();
            var text = line.TrimStart('#').Trim().Replace("**", "").Replace("`", "");
            stack.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = level switch { 1 => 25, 2 => 20, 3 => 16, _ => 14 }, FontWeight = level > 0 ? FontWeight.SemiBold : FontWeight.Normal, Margin = new(0, level > 0 ? 12 : 0, 0, 0) });
        }
        Content = new DockPanel { Children = { Ui.Scroll(stack) } };
    }
}
