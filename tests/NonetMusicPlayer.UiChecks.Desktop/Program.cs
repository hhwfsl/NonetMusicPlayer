internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--icon-resources-only")) { IconResourceChecks.Run(args[0], args[1]); return 0; }
            if (args.Contains("--native-beta9") || args.Contains("--native-beta10") || args.Contains("--native-beta11") || args.Contains("--native-beta12") || args.Contains("--native-beta13") || args.Contains("--native-beta14")) { NativeWindowLifecycleChecks.Run(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/native-beta14"); return 0; }
            if (args.Contains("--audio-only")) AudioChecks.Run(Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/audio-checks"));
            else ApplicationIntegrationChecks.Run(args);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
