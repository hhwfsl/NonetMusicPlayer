using System.Text;

namespace NonetMusicPlayer.Core.Persistence;

/// <summary>原子发布文本文件，失败时保留原文件；用于 CLI 状态和插件索引。</summary>
public static class AtomicFile
{
    public static void Write(string path, string text, bool backup = false)
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(text); output.Write(bytes); output.Flush(true);
            }
            if (backup && File.Exists(full)) File.Copy(full, full + ".bak", true);
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
