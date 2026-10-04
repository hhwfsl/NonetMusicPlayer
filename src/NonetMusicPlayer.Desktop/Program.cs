using Avalonia;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop;

internal static class Program
{
    internal static SingleInstanceService? Instance { get; private set; }
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Error("Application", "Unhandled fatal exception", e.ExceptionObject as Exception); AppLog.Flush();
        };
        try
        {
            // 更新助手不初始化 Avalonia，也不竞争主程序的单实例锁。
            if (args.Length == 2 && args[0] == "--apply-update") return UpdateInstaller.RunHelper(args[1]);
            if (args.Length == 3 && args[0] == "--verify-release") return Services.ReleaseVerifier.Run(args[1], args[2]);
            if (args.Length == 4 && args[0] == "--verify-provider") return Services.ReleaseVerifier.RunProvider(args[1], args[2], args[3]);
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
            _ = Task.Run(() => WindowsIconRefreshService.RefreshIfChanged(iconDataRoot));
            AppLog.Info("Application", "Starting " + typeof(Program).Assembly.GetName().Version);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            AppLog.Error("Application", "Startup or application failed", error); AppLog.Flush();
            if (OperatingSystem.IsWindows()) FatalMessage(L10n.T("Application.StartupFailed"), "Nonet", 0x10);
            else Console.Error.WriteLine(L10n.T("Application.StartupFailed"));
            return 1;
        }
        finally { AppLog.Shutdown(); }
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

