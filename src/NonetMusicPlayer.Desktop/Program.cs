using Avalonia;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop;

internal static class Program
{
    internal static SingleInstanceService? Instance { get; private set; }
    internal static CrashHandlerService? CrashHandler { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Error("Application", "Unhandled fatal exception", e.ExceptionObject as Exception); AppLog.Flush();
            CrashHandler?.ReportFatal(e.ExceptionObject as Exception);
        };
        try
        {
            // 更新助手不初始化 Avalonia，也不竞争主程序的单实例锁。
            if (args.Length == 2 && args[0] == "--apply-update") return UpdateInstaller.RunHelper(args[1]);
            if (args.Length == 2 && args[0] == "--verify-listening-history") return ReleaseVerifier.RunListening(args[1]);
            if (args.Length == 3 && args[0] == "--verify-release") return Services.ReleaseVerifier.Run(args[1], args[2]);
            if (args.Length == 4 && args[0] == "--verify-provider") return Services.ReleaseVerifier.RunProvider(args[1], args[2], args[3]);
            if (args.Length == 4 && args[0] == "--verify-music-source") return Services.ReleaseVerifier.RunMusicSource(args[1], args[2], args[3]);
            if (args.Length == 3 && args[0] == "--verify-extension-plugin") return Services.ReleaseVerifier.RunExtension(args[1], args[2]);
            if (args.Length == 4 && args[0] == "--verify-file-transform") return Services.ReleaseVerifier.RunFileTransform(args[1], args[2], args[3]);
            if (args.Length == 3 && args[0] == "--verify-runtime-fault") return Services.ReleaseVerifier.RunRuntimeFault(args[1], args[2]);
            if (args.Length == 4 && args[0] == "--verify-extension-image") return Services.ReleaseVerifier.RunExtensionImage(args[1], args[2], args[3]);
            if (args.Length == 4 && args[0] == "--verify-style-plugin") return Services.ReleaseVerifier.RunStyles(args[1], args[2], args[3]);
            if (args.Length == 3 && args[0] == "--verify-agent-plugin") return Services.ReleaseVerifier.RunAgent(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--verify-catalog-navigation") return Services.ReleaseVerifier.RunCatalogNavigation(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--verify-plugin-configuration") return Services.ReleaseVerifier.RunPluginConfiguration(args[1], args[2]);
            if (args.Length == 4 && args[0] == "--verify-live-plugin-configuration") return Services.ReleaseVerifier.RunPluginConfiguration(args[1], args[3], args[2]);
            if (args.Length == 3 && args[0] == "--verify-lyrics-plugin") return Services.ReleaseVerifier.RunLyrics(args[1], args[2]);
            using var instance = new SingleInstanceService();
            if (!instance.IsPrimary)
            {
                if (instance.ForwardAsync(args).GetAwaiter().GetResult()) return 0;
                if (OperatingSystem.IsWindows()) FatalMessage(L10n.T("Application.InstanceNotResponding"), "Nonet", 0x30);
                return 2;
            }
            Instance = instance;
            AppLog.Initialize(AppStorage.ResolveRoot());
            var iconDataRoot = AppStorage.ResolveRoot();
            CrashHandler = CrashHandlerService.Start(iconDataRoot);
            _ = Task.Run(() => WindowsIconRefreshService.RefreshIfChanged(iconDataRoot));
            AppLog.Info("Application", "Starting " + typeof(Program).Assembly.GetName().Version);
            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            if (exitCode == 0) CrashHandler.CompleteNormalExit();
            else CrashHandler.ReportFatal(new InvalidOperationException("Application exited with code " + exitCode));
            return exitCode;
        }
        catch (Exception error)
        {
            AppLog.Error("Application", "Startup or application failed", error); AppLog.Flush();
            CrashHandler?.ReportFatal(error);
            if (OperatingSystem.IsWindows() && CrashHandler?.IsAvailable != true) FatalMessage(L10n.T("Application.StartupFailed"), "Nonet", 0x10);
            else Console.Error.WriteLine(L10n.T("Application.StartupFailed"));
            return 1;
        }
        finally { CrashHandler?.Dispose(); CrashHandler = null; AppLog.Shutdown(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int NativeMessageBox(nint hwnd, string text, string caption, uint type);
    private static void FatalMessage(string message, string title, uint type) { try { NativeMessageBox(0, message, title, type); } catch { } }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        // Windows 的二维界面使用 Skia 软件绘制，避免 ANGLE/D3D 上下文开销，原生控件保持不变。
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .With(new SkiaOptions { MaxGpuResourceSizeBytes = 16 * 1024 * 1024 })
        .WithInterFont()
        .LogToTrace();
}

