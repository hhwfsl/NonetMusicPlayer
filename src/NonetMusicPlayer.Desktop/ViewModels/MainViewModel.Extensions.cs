using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    internal void UpdateExtensionMetadata(Models.TrackItem track, JsonObject values)
    {
        var title = track.Title; var artist = track.Artist; var album = track.Album;
        _undo.Push(() => { track.Title = title; track.Artist = artist; track.Album = album; });
        if (values["title"] is { } newTitle) track.Title = newTitle.GetValue<string>();
        if (values["artist"] is { } newArtist) track.Artist = newArtist.GetValue<string>();
        if (values["album"] is { } newAlbum) track.Album = newAlbum.GetValue<string>();
        ApplyFilter(); Save(); ViewChanged?.Invoke(this, EventArgs.Empty); CompleteOperation("metadata.update");
    }
    private void ExtensionAudioBypassed(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_disposed) ReportWarning(L10n.T("Extensions.AudioBypassed")); });
    private readonly Dictionary<string, NonetMusicPlayer.Core.Plugins.ExtensionPcmLease> _extensionPcm = [];
    internal void SetExtensionAudio(string id, NonetMusicPlayer.Core.Plugins.ExtensionPcmLease? processor, NonetMusicPlayer.Core.Plugins.ExtensionPcmLease? expected = null)
    {
        // 较早会话的延迟析构不得移除同 ID 新会话刚注册的处理器。
        if (processor is null && expected is not null && _extensionPcm.GetValueOrDefault(id) != expected) return;
        if (processor is null) _extensionPcm.Remove(id); else _extensionPcm[id] = processor;
        if (_audio is NonetMusicPlayer.Core.Audio.IExtensionAudioHost host) host.SetExtensionProcessors(_extensionPcm.OrderBy(p => p.Key).Select(p => p.Value).ToArray());
    }
    /// <summary>未订阅钩子时沿用同步导航；订阅时等待有限决策，不改变恢复页的可达性。</summary>
    private int _extensionNavigationRequest;
    private async Task NavigateWithHooksAsync(string page, string? title, int request)
    {
        var decision = await Plugins.EvaluateHooksAsync("navigation.before", new() { ["page"] = page });
        if (_disposed || request != _extensionNavigationRequest || decision.Cancel) return;
        var target = decision.Data["page"]?.GetValue<string>() ?? page;
        if (target is not ("library" or "songs" or "history" or "albums" or "artists" or "lyrics" or "statistics" or "settings" or "plugins" or "terminal")
            && !target.StartsWith("playlist:", StringComparison.Ordinal) && !target.StartsWith("plugin:", StringComparison.Ordinal) && !target.StartsWith("album:", StringComparison.Ordinal) && !target.StartsWith("artist:", StringComparison.Ordinal)) target = page;
        NavigateCore(target, title);
    }
}
