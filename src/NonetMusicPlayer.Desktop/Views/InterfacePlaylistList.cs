using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>方案只改变已有本机/互听歌单的展示。虚拟化条目按需创建，退出方案后解除全部订阅。</summary>
internal sealed class InterfacePlaylistList : ListBox, IDisposable
{
    protected override Type StyleKeyOverride => typeof(ListBox);
    private readonly MainViewModel _vm;
    private bool _disposed;
    public InterfacePlaylistList(MainViewModel vm, Func<Playlist, Control> local, Func<(string Page, string Name, string Source), Control> remote)
    {
        _vm = vm; Background = Brushes.Transparent; BorderThickness = new(0);
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        ItemTemplate = new FuncDataTemplate<Entry>((item, _) => item is null ? null : item.Local is not null ? local(item.Local) : remote(item.Remote));
        vm.Playlists.CollectionChanged += Changed; vm.LanChanged += LanChanged; Refresh();
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void LanChanged(object? sender, EventArgs e) => Refresh();
    private void Refresh()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Refresh); return; }
        ItemsSource = _vm.Playlists.Select(p => new Entry(p, default)).Concat(_vm.LanPlaylistNavigation.Select(p => new Entry(null, p))).ToArray();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _vm.Playlists.CollectionChanged -= Changed; _vm.LanChanged -= LanChanged; ItemsSource = null; }
    private sealed record Entry(Playlist? Local, (string Page, string Name, string Source) Remote);
}
