internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var output = args.FirstOrDefault() ?? "artifacts/plugin-ui-checks";
            if (args.Contains("--root-review")) { RootReviewChecks.Run(output); return 0; }
            if (args.Contains("--plugin-migration")) { PluginMigrationChecks.Run(output); return 0; }
            PluginUiChecks.Run(output); HostPluginChecks.Run(output); PluginHostCapabilityChecks.Run(output); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
