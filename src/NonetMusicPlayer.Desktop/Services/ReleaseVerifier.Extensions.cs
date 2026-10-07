using NonetMusicPlayer.Desktop.Plugins;
namespace NonetMusicPlayer.Desktop.Services;
internal static partial class ReleaseVerifier
{
    /// <summary>在真实精简发行包中验证已冻结的 v2 ABI，不运行图形界面或用户数据。</summary>
    public static int RunExtension(string package, string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        try
        {
            using var manager = new PluginManager(new AppStorage(Path.Combine(root, "data")));
            var plugin = manager.Install(package);
            if (plugin.Runtime == "managed") plugin.ManagedExecutionConsent = true; // 仅隔离夹具命令显式批准。
            manager.SetEnabled(plugin, true);
            var session = manager.Extension(plugin); session.StartAsync().GetAwaiter().GetResult();
            session.InvokeAsync("increment", new()).GetAwaiter().GetResult();
            if (session.Frame.State["count"]!.GetValue<int>() != 1) throw new InvalidDataException("Frozen extension ABI failed.");
            manager.SetEnabled(plugin, false);
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nActualPublishedHost=True\nFrozenV2=True\nRuntime=" + plugin.Runtime + "\n");
            return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + error.GetType().Name + ": " + error.Message); return 1; }
    }
}
