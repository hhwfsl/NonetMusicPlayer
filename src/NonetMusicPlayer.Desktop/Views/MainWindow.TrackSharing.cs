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
        menu.Items.Add(Menu(label, async () =>
        {
            // 剪贴板仅在点击后写入，等系统完成写入再提示成功。
            if (Clipboard is not { } clipboard) throw new InvalidOperationException(L10n.T("MusicSearch.ShareUnavailable"));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var url = await vm.Plugins.ReadShareLinkAsync(track, deadline.Token);
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(url));
            await clipboard.SetDataAsync(transfer);
            vm.ReportSuccess(L10n.Format("MusicSearch.ShareLinkCopied", source.Name));
        }, rawLabel: true));
    }
}
