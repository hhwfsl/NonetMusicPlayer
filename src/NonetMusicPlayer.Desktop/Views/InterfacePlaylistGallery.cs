using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>按现有分类卡片的视口规则显示歌单；订阅仅属于当前表现页。</summary>
internal sealed class InterfacePlaylistGallery : UserControl, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly Func<Playlist, Control> _local;
    private readonly Func<(string Page, string Name, string Source), Control> _remote;
    private VirtualizedCardGrid? _grid;
    private Playlist[] _localItems = [];
    private (string Page, string Name, string Source)[] _remoteItems = [];
    private bool _disposed;
    private string _query = "";
    public InterfacePlaylistGallery(MainViewModel vm, Func<Playlist, Control> local, Func<(string Page, string Name, string Source), Control> remote)
    {
        Name = "InterfacePlaylistGallery"; _vm = vm; _local = local; _remote = remote;
        vm.Playlists.CollectionChanged += Changed; vm.LanChanged += LanChanged; Refresh();
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void LanChanged(object? sender, EventArgs e) => Refresh();
    public void SetFilter(string query)
    {
        query = query.Trim(); if (_query == query) return;
        _query = query; Refresh(); if (_grid is not null) _grid.Offset = default;
    }
    private void Refresh()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Refresh); return; }
        var local = _vm.Playlists.Where(p => MainWindow.CatalogMatches(p.IsSystem ? Services.L10n.T("Playlists.LikedSongs") : p.Name, _query)).ToArray(); var remote = _vm.LanPlaylistNavigation.Where(p => MainWindow.CatalogMatches(p.Name, _query)).ToArray();
        if (_grid is not null && local.SequenceEqual(_localItems) && remote.SequenceEqual(_remoteItems)) return;
        _localItems = local; _remoteItems = remote;
        var offset = _grid?.Offset ?? default;
        _grid?.Dispose();
        _grid = new(local.Length + remote.Length, index => index < local.Length ? _local(local[index]) : _remote(remote[index - local.Length]));
        Content = local.Length + remote.Length == 0 ? Ui.Card("MusicSearch.NoCatalogResults", Ui.Text("MusicSearch.TryAnotherName", 13, true)) : _grid;
        var current = _grid;
        Dispatcher.UIThread.Post(() => { if (!_disposed && ReferenceEquals(_grid, current)) current.Offset = offset; }, DispatcherPriority.Loaded);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _vm.Playlists.CollectionChanged -= Changed; _vm.LanChanged -= LanChanged;
        _grid?.Dispose(); Content = null;
    }
}
