using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Runtime;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayerCli;

/// <summary>纯命令行入口；TTY 默认使用 TUI，管道、无 ANSI 终端和批处理使用逐行输出。</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false); Console.InputEncoding = Encoding.UTF8;
        var directory = Path.Combine(AppContext.BaseDirectory, "Data"); var commands = new List<string>(); var json = false; var noTui = false; var stay = false; var repl = false; var forceTui = false;
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.MissingOption"));
                switch (args[i])
                {
                    case "--data": directory = Path.GetFullPath(Value()); break;
                    case "--command": commands.Add(Value()); break;
                    case "--json": json = true; noTui = true; break;
                    case "--no-tui": noTui = true; break;
                    case "--tui": noTui = false; forceTui = true; break;
                    case "--repl": repl = true; break;
                    case "--stay": stay = true; break;
                    case "--help": case "-h": PrintHelp(); return 0;
                    case "help" when !json && commands.Count == 0 && i == args.Length - 1: PrintHelp(); return 0;
                    default:
                        // 进程入口直接接受公开命令；各参数重新加引号，不改变路径和 Unicode 内容。
                        commands.Add("nonet " + string.Join(" ", args.Skip(i).Select(QuoteArgument))); i = args.Length; break;
                }
            }
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            await using var session = new HeadlessPlayerSession(directory);
            var router = new PlayerCommandRouter(session); session.ResultPublished += (_, result) => router.Publish(result);
            var exitCode = 0;
            var interactive = !json && !noTui && !Console.IsInputRedirected && (forceTui || !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("TERM") != "dumb" && ConsoleCapabilities.EnableAnsi());
            if (interactive && forceTui) ConsoleCapabilities.EnableAnsi();
            if (!interactive || commands.Count > 0)
            {
                void PrintLog(LogEntry entry)
                {
                    if (!json && entry.Level >= TerminalOptions.ParseLevel(session.State.Settings.TerminalMinimumLogLevel)) Console.WriteLine(entry.Text);
                }
                void PrintResult(object? sender, CommandResult result)
                {
                    if (json) Console.WriteLine(JsonSerializer.Serialize(result, CoreJson.Rpc));
                    else if (result.Operation == "help") Console.WriteLine(result.Message);
                    else if (result.Data is not null) Console.WriteLine(result.Data.ToJsonString(CoreJson.Readable));
                }
                AppLog.EntryWritten += PrintLog;
                router.ResultPublished += PrintResult;
                try
                {
                foreach (var command in commands)
                {
                    var result = await router.ExecuteAsync(command, Console.IsInputRedirected ? null : Confirm, cancellation.Token);
                    if (!result.Success) exitCode = 1; if (session.ExitRequested) break;
                }
                if (interactive && stay && !session.ExitRequested)
                {
                    router.ResultPublished -= PrintResult;
                    AppLog.EntryWritten -= PrintLog;
                    if (repl) await new ConsolePlayerView(session, router).RunAsync(cancellation.Token); else await new ConsoleTuiView(session, router).RunAsync(cancellation.Token);
                }
                else if (commands.Count == 0 || stay)
                while (!session.ExitRequested && !cancellation.IsCancellationRequested)
                {
                    if (!Console.IsInputRedirected) Console.Write(PlayerTerminalSession.CommandPrompt);
                    var command = await Console.In.ReadLineAsync(cancellation.Token); if (command is null) break; if (string.IsNullOrWhiteSpace(command)) continue;
                    var result = await router.ExecuteAsync(command, Console.IsInputRedirected ? null : Confirm, cancellation.Token); if (!result.Success) exitCode = 1;
                }
                }
                finally { AppLog.Flush(); AppLog.EntryWritten -= PrintLog; router.ResultPublished -= PrintResult; }
            }
            else if (repl) await new ConsolePlayerView(session, router).RunAsync(cancellation.Token); else await new ConsoleTuiView(session, router).RunAsync(cancellation.Token);
            AppLog.Flush(); return exitCode;
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception error) { Console.Error.WriteLine(AppLog.Redact(error.Message)); return 1; }
    }
    private static Task<bool> Confirm(CommandDefinition definition)
    {
        Console.Write(LocalizationCatalog.Format("Terminal.ConfirmPrompt", CommandSyntax.PublicName(definition.Name))); return Task.FromResult(Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true);
    }
    private static void PrintHelp()
    {
        Console.WriteLine(LocalizationCatalog.Get("Terminal.CliHelp"));
        foreach (var command in PlayerCommandRouter.Definitions.Where(d => !d.DesktopOnly)) Console.WriteLine(command.PublicUsage);
    }
    private static string QuoteArgument(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
