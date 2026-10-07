internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var output = args.FirstOrDefault() ?? "artifacts/plugin-ui-checks";
            if (Array.IndexOf(args, "--inspect-extension") is var packageIndex && packageIndex >= 0) { ExtensionChecks.RenderPackage(args[packageIndex + 1], output, Array.IndexOf(args, "--invoke") is var actionIndex && actionIndex >= 0 ? args[actionIndex + 1] : null); return 0; }
            if (args.Contains("--composer")) { ComposerExtensionChecks.Run(output); return 0; }
            if (args.Contains("--universal")) { UniversalExtensionChecks.Run(output); return 0; }
            if (args.Contains("--workspace")) { WorkspaceExtensionChecks.Run(output); return 0; }
            if (args.Contains("--extensions")) { ExtensionChecks.Run(output); return 0; }
            if (args.Contains("--agent")) { AgentHostChecks.Run(output); return 0; }
            if (args.Contains("--update-layout")) { PluginUpdateLayoutChecks.Run(output); return 0; }
            if (args.Contains("--compatibility")) { PluginCompatibilityChecks.Run(output); return 0; }
            if (args.Contains("--root-review")) { RootReviewChecks.Run(output); return 0; }
            if (args.Contains("--plugin-migration")) { PluginMigrationChecks.Run(output); return 0; }
            PluginUiChecks.Run(output); HostPluginChecks.Run(output); PluginHostCapabilityChecks.Run(output); PluginCompatibilityChecks.Run(output); PluginUpdateLayoutChecks.Run(output); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
