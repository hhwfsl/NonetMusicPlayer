using System.Security.Cryptography;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Plugins;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>在实际发布包中验证转换角色；隔离数据，仅转换显式给出的夹具，不读取用户音乐库。</summary>
    public static int RunFileTransform(string package, string input, string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        try
        {
            var source = Path.GetFullPath(input);
            using var original = File.OpenRead(source); var before = SHA256.HashData(original); original.Close();
            var storage = new AppStorage(Path.Combine(root, "data"));
            AppLog.Initialize(storage.Root);
            using var manager = new PluginManager(storage);
            var plugin = manager.Install(Path.GetFullPath(package));
            if (plugin.Runtime == "managed") plugin.ManagedExecutionConsent = true;
            manager.SetEnabled(plugin, true);
            if (!manager.InputFileExtensions.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Transform registration failed.");
            var warnings = new List<string>();
            var converted = manager.PrepareLocalFilesAsync([source], "import", warnings.Add).GetAwaiter().GetResult();
            if (!plugin.Enabled) throw new InvalidDataException("Transformer was fault-disabled.");
            foreach (var path in converted)
            {
                FileTransformContract.ValidateOutput(source, path);
                if (new MusicLibraryScanner(storage).ReadTrack(path).DurationSeconds <= 0) throw new InvalidDataException("Transformed audio cannot be scanned.");
            }
            using var after = File.OpenRead(source);
            if (!before.SequenceEqual(SHA256.HashData(after))) throw new InvalidDataException("Transformer modified its input.");
            manager.SetEnabled(plugin, false);
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), $"PASS\nActualPublishedHost=True\nRegistration=True\nOutputCount={converted.Length}\nWarnings={warnings.Count}\nSourcePreserved=True\nLifecycle=True\n");
            return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + AppLog.Redact(error.ToString())); return 1; }
        finally { AppLog.Flush(); AppLog.Shutdown(); }
    }
}
