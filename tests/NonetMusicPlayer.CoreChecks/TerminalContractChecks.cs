using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Lyrics;

internal static class TerminalContractChecks
{
    internal static async Task RunAsync(string output)
    {
        var root = Path.Combine(output, "terminal-logs-" + Guid.NewGuid().ToString("N")); AppLog.Initialize(root);
        var router = new PlayerCommandRouter(new Backend());
        var help = await router.ExecuteAsync("nonet help");
        Check(help.Success && !help.Message.Contains("window") && !help.Message.Contains("layout"), "Headless help filters unsupported host commands");
        Check(help.Message.Split('\n').All(line => line.StartsWith("nonet ") && line.Split(' ').Take(3).All(word => !word.Contains('.'))), "Help uses only public space-separated syntax");
        Check((await router.ExecuteAsync("nonet help playlist add")).Message.StartsWith("nonet playlist add"), "Category/action help");
        Check((await router.ExecuteAsync("help player pause")).Message == (await router.ExecuteAsync("nonet help player pause")).Message, "Optional nonet prefix inside a session");
        Check(CommandLineParser.Parse("player pause").Name == CommandLineParser.Parse("nonet player pause").Name, "Unprefixed category has same operation");
        Check(!(await router.ExecuteAsync("nonet --player pause")).Success, "Dashed category syntax no longer accepted");
        Check(CommandLineParser.Parse("playlist delete x --yes").Confirmed && CommandLineParser.Parse("playlist delete x --no").Declined, "Long confirmation options retained");
        Check(!(await router.ExecuteAsync("playlist.list")).Success && !(await router.ExecuteAsync("nonet playlist.list")).Success, "Legacy dotted input rejected");
        var no = await router.ExecuteAsync("nonet playlist delete example -n", _ => throw new InvalidOperationException("Must not prompt"));
        Check(!no.Success && !no.RequiresConfirmation, "Explicit -n cancels without prompting");
        Check(CommandLineParser.Parse("nonet playlist create -- -y").Arguments.Single() == "-y", "End-of-options protects literal names");
        Check(!(await router.ExecuteAsync("nonet playlist delete x -n -y")).Success, "Contradictory confirmation rejected");
        AppLog.Flush();
        using var terminal = new PlayerTerminalSession(router, new(10)); await terminal.ClearAsync();
        for (var i = 0; i < 35; i++) AppLog.Info("Scrollback", "history-" + i);
        AppLog.Flush();
        Check(terminal.Snapshot.Transcript.Count(c => c == '\n') == 10 && !terminal.Snapshot.Transcript.Contains("history-24\n") && terminal.Snapshot.Transcript.Contains("history-34"), "Line-based bounded scrollback");
        terminal.Configure(new(10000, 19, TerminalLogLevel.Warning));
        AppLog.Info("Filter", "hidden-information"); AppLog.Info("Filter", "hidden-inline [ERROR] marker"); AppLog.Warning("Filter", "visible-warning"); AppLog.Error("Filter", "visible-error"); AppLog.Flush();
        Check(!terminal.Snapshot.Transcript.Contains("hidden-information") && !terminal.Snapshot.Transcript.Contains("[INFO]") && terminal.Snapshot.Transcript.Contains("visible-warning"), "Display level filters existing and new records");
        Check(!terminal.Snapshot.Transcript.Contains("hidden-inline"), "Inline text cannot elevate log severity");
        terminal.Configure(new(50000, 100)); Check(terminal.Options.ScrollbackLines == 10000 && terminal.Options.FontSize == 32, "Safe upper limits");
        Check(terminal.Snapshot.Transcript.Contains("hidden-information"), "Lowered filter reveals retained records");
        terminal.SetDraft("nonet music list"); await terminal.SubmitAsync(); AppLog.Flush();
        Check(terminal.Snapshot.Transcript.Contains("オレンジ") && terminal.Snapshot.Transcript.Contains("日常") && !terminal.Snapshot.Transcript.Contains("\\u30"), "Unicode in human-readable structured output");
        using var logStream = new FileStream(Path.Combine(root, "Logs", $"{DateTime.Now:yyyy-MM-dd}_{AppLog.Version}.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var logReader = new StreamReader(logStream); var disk = logReader.ReadToEnd();
        Check(disk.Contains("hidden-information") && disk.Contains("visible-warning") && !disk.Contains("nonet $"), "Display logs originate on disk; command echo is not logged");
        foreach (var line in terminal.Snapshot.Transcript.Split('\n').Where(l => l.Contains(" [INFO] ") || l.Contains(" [WARN] ") || l.Contains(" [ERROR] ")))
            Check(disk.Contains(line.TrimEnd('\r')), "Every displayed log is a persisted record");
        var timing = new LyricsTimingSession("[ar:artist]\n\n[00:01.01]第一行\n[00:03:25]第二行\n第三行\n");
        Check(timing.Lines.Count == 3 && timing.Times[0] == 0, "Empty/metadata lines excluded and first timestamp zero");
        timing.Mark(1.23456); timing.Mark(2.9996);
        Check(timing.ToLrc() == "[00:00.000]第一行\n[00:01.235]第二行\n[00:03.000]第三行\n", "Milliseconds, rounding carry and original order");
        timing.Undo(); Check(!timing.Complete && timing.NextLine == 2, "Undo final mark");
        try { timing.Mark(.5); throw new Exception("Accepted backwards timestamp"); } catch (InvalidOperationException) { }
        timing.Mark(3.25); Check(timing.Complete, "Redo final mark");
        Console.WriteLine("PASS TERMINAL CONTRACT: nonet syntax, host-filtered help, confirmations, Unicode, 1000/10000 line limits, persisted log stream/filter and timed lyric model.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Backend : IPlayerCommandBackend
    {
        public bool HasWindow => false;
        public Task<CommandResult> ExecuteAsync(PlayerCommand command, CancellationToken token) => Task.FromResult(CommandResults.Completed(command.Name,
            command.Name == "music.list" ? new JsonArray(new JsonObject { ["title"] = "オレンジ", ["playlist"] = "日常" }) : null));
    }
}
