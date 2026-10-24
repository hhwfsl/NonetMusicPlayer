internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var output = args.FirstOrDefault() ?? "artifacts/plugin-ui-checks";
            if (args.Contains("--statistics-charts")) { StatisticsChartChecks.Run(output, Array.IndexOf(args, "--style-file") is var chartStyle && chartStyle >= 0 ? args[chartStyle + 1] : null); return 0; }
            if (args.Contains("--interface-lifecycle")) { InterfaceLifecycleChecks.Run(output); return 0; }
            if (args.Contains("--composition")) { InterfaceCompositionChecks.Run(output, Array.IndexOf(args, "--style-file") is var compositionFile && compositionFile >= 0 ? args[compositionFile + 1] : null); return 0; }
            if (args.Contains("--styles")) { StyleLayerChecks.Run(output, Array.IndexOf(args, "--style-package") is var styleIndex && styleIndex >= 0 ? args[styleIndex + 1] : null, Array.IndexOf(args, "--style-file") is var fileIndex && fileIndex >= 0 ? args[fileIndex + 1] : null); return 0; }
            if (args.Contains("--lan")) { LanHostChecks.Run(output); return 0; }
            if (args.Contains("--switching")) { PlaybackSwitchChecks.Run(output); return 0; }
            if (args.Contains("--rpc-cancellation")) { RpcCancellationChecks.Run(output).GetAwaiter().GetResult(); return 0; }
            if (args.Contains("--appearance")) { AppearanceChecks.Run(output); return 0; }
            if (args.Contains("--lyrics-transition")) { LyricsTransitionChecks.Run(output, Array.IndexOf(args, "--style-file") is var lyricStyle && lyricStyle >= 0 ? args[lyricStyle + 1] : null); return 0; }
            if (args.Contains("--element-layout")) { ElementLayoutChecks.Run(output); return 0; }
            if (args.Contains("--runtime-faults")) { RuntimeFaultChecks.Run(output); return 0; }
            if (args.Contains("--music-search")) { MusicSearchChecks.Run(output); return 0; }
            if (args.Contains("--background-folders")) { BackgroundAndFolderChecks.Run(output); return 0; }
            if (Array.IndexOf(args, "--inspect-extension") is var packageIndex && packageIndex >= 0) { ExtensionChecks.RenderPackage(args[packageIndex + 1], output, Array.IndexOf(args, "--invoke") is var actionIndex && actionIndex >= 0 ? args[actionIndex + 1] : null); return 0; }
            if (args.Contains("--page-less")) { PageLessExtensionChecks.Run(output); return 0; }
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
