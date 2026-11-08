using Avalonia.Input;
using Avalonia.Interactivity;
using NonetMusicPlayer.Desktop.Controls;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private TrackReorderPreview? _trackReorder;
    private string? _trackReorderPage;
    private string[] _trackReorderSource = [], _trackReorderIds = [];
    private bool _trackReorderActive;

    private void BeginTrackReorderPreview(string[] ids)
    {
        CancelTrackReorderPreview(); _trackReorderActive = false;
        if (_vm?.CurrentPlaylist is not { } playlist) return;
        _trackReorderActive = true;
        _trackReorderPage = _vm.Page; _trackReorderSource = playlist.TrackIds.ToArray(); _trackReorderIds = ids;
        _trackReorder = new TrackReorderPreview(TracksList, _vm.VisibleTracks, ids, _dragStart);
    }
    private void UpdateTrackReorderPreview(DragEventArgs e)
    {
        if (_trackReorder is null) return;
        if (_vm is null || _vm.Page != _trackReorderPage || _vm.CurrentPlaylist is not { } playlist || !playlist.TrackIds.SequenceEqual(_trackReorderSource) || !_trackReorder.IsValid)
        { CancelTrackReorderPreview(); return; }
        // 只预览同一来源的内部排序；跨歌单添加、文件导入和播放器投放保持原有语义。
        if (Ancestors(e.Source).Contains(TracksList) && IsTrackReorderPayload(e)) _trackReorder.Preview(e.GetPosition(TracksList));
        else _trackReorder.Restore();
    }
    private bool IsTrackReorderPayload(DragEventArgs e) => e.DataTransfer.TryGetText() is { } text
        && text.StartsWith("nonet-tracks:", StringComparison.Ordinal)
        && text["nonet-tracks:".Length..].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SequenceEqual(_trackReorderIds);

    private bool CommitTrackReorderPreview(DragEventArgs e)
    {
        if (!_trackReorderActive || !IsTrackReorderPayload(e) || !Ancestors(e.Source).Contains(TracksList)) return false;
        UpdateTrackReorderPreview(e);
        if (_trackReorder is not { IsValid: true, TargetId: { } target } preview || _vm?.CurrentPlaylist is not { } playlist)
        { CancelTrackReorderPreview(); return true; }
        var before = preview.Before; var ids = _trackReorderIds;
        // 恢复展示集合后再进入原有撤销/持久化路径，避免把预览当作原始顺序。
        CancelTrackReorderPreview(); _vm.ReorderPlaylistTracks(playlist, ids, target, before); return true;
    }
    private void CancelTrackReorderPreview() { _trackReorder?.Dispose(); _trackReorder = null; }
}
