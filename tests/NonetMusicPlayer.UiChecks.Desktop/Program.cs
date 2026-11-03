using Avalonia;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--native-window-size"))
            {
                AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UsePlatformDetect()
                    .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                    .WithInterFont().SetupWithoutStarting();
                var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/native-window-size");
                Directory.CreateDirectory(output);
                WindowSizePreferenceChecks.Run(output, native: true, styleFile: args.FirstOrDefault(a => a.StartsWith("--style="))?[8..]);
                return 0;
            }
            if (args.Contains("--native-touch-responsive"))
            {
                AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UsePlatformDetect()
                    .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                    .WithInterFont().SetupWithoutStarting();
                var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/native-touch-responsive");
                Directory.CreateDirectory(output);
                TouchResponsiveChecks.Run(output, args.FirstOrDefault(a => a.StartsWith("--style="))?[8..], native: true);
                return 0;
            }
            if (args.Contains("--icon-resources-only")) { IconResourceChecks.Run(args[0], args[1]); return 0; }
            if (args.Contains("--native-beta9") || args.Contains("--native-beta10") || args.Contains("--native-beta11") || args.Contains("--native-beta12") || args.Contains("--native-beta13") || args.Contains("--native-beta14")) { NativeWindowLifecycleChecks.Run(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/native-beta14"); return 0; }
            if (args.Contains("--audio-only")) AudioChecks.Run(Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/audio-checks"));
            else ApplicationIntegrationChecks.Run(args);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
