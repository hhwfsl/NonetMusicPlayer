using Avalonia.Controls;
using Avalonia.Input;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private void AppendTrackShareLink(ContextMenu menu, TrackItem? track)
    {
        var vm = _vm;
        if (track is null || vm?.Plugins.ShareLinkSource(track) is not { } source) return;
        var label = L10n.Format("MusicSearch.SharePlatformLink", source.Name);
        menu.Items.Add(Menu(label, () => CopyTrackLinkAsync(track), rawLabel: true));
    }

    // 菜单与终端共用实际复制路径，剪贴板写入完成后才显示成功。
    private async Task CopyTrackLinkAsync(TrackItem track, CancellationToken token = default)
    {
        if (_vm is not { } vm || Clipboard is not { } clipboard || vm.Plugins.ShareLinkSource(track) is not { } source)
            throw new InvalidOperationException(L10n.T("MusicSearch.ShareUnavailable"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        var url = await vm.Plugins.ReadShareLinkAsync(track, deadline.Token);
        deadline.Token.ThrowIfCancellationRequested();
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(url));
        await clipboard.SetDataAsync(transfer);
        vm.ReportSuccess(L10n.Format("MusicSearch.ShareLinkCopied", source.Name));
    }
}
