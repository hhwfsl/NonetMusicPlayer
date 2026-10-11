namespace NonetMusicPlayer.Desktop.Services;

/// <summary>清理旧版独立视频背景解码器；不处理音乐解码库、用户原媒体或其他文件。</summary>
public static class RetiredBackgroundCleanup
{
    public static bool Run(string? installationRoot = null)
    {
        var root = Path.GetFullPath(installationRoot ?? AppContext.BaseDirectory);
        var folder = Path.Combine(root, "VideoDecoder");
        try
        {
            if (!Directory.Exists(folder)) return true;
            DataDirectoryService.RejectLinkedAncestors(folder);
            foreach (var name in new[] { "ffmpeg.exe", "ffmpeg", "LICENSE.txt", "NOTICE.txt" })
            {
                var file = Path.Combine(folder, name);
                DataDirectoryService.RejectLinkedAncestors(file);
                if (File.Exists(file)) File.Delete(file);
            }
            // 未知文件可能由用户放置，因此只删除空目录，不递归删除。
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            AppLog.Info("Background", "Removed obsolete standalone background decoder files");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { AppLog.Warning("Background", "Unable to remove obsolete background decoder files", error); return false; }
    }
}
