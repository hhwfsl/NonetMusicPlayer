using NonetMusicPlayer.Desktop.Plugins;
namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>正式精简程序中的故障夹具，不触及用户数据，也不运行致命崩溃测试。</summary>
    public static int RunRuntimeFault(string package, string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        try
        {
            var storage = new AppStorage(Path.Combine(root, "data")); AppLog.Initialize(storage.Root);
            using var manager = new PluginManager(storage);
            var plugin = manager.Install(package); plugin.ManagedExecutionConsent = true; manager.SetEnabled(plugin, true);
            var session = manager.Extension(plugin); session.StartAsync().GetAwaiter().GetResult();
            try { session.InvokeAsync("throw", new()).GetAwaiter().GetResult(); throw new InvalidDataException("Expected fixture failure."); }
            catch (InvalidOperationException) { }
            if (plugin.Enabled || !session.IsDisposed) throw new InvalidDataException("Actual host did not disable fault.");
            var lyrics = LyricsService.Parse("[nonet:original]\n[1000,1000](1000,500,0)原(1500,500,0)文\n[nonet:translation]\n[00:01.080]Translation").Single();
            if (lyrics.Translation != "Translation" || lyrics.Words.Count != 2) throw new InvalidDataException("Actual host lyrics failed.");
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nActualPublishedHost=True\nManagedFaultDisabled=True\nNamedPluginLog=True\nNetEaseYrcTranslation=True\n"); return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + AppLog.Redact(error.ToString())); return 1; }
        finally { AppLog.Flush(); AppLog.Shutdown(); }
    }
}
