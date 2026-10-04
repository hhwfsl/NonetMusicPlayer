using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Runtime;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayerCli;

/// <summary>按实际 TUI 按键路径检查操作覆盖、历史、表单、无命令页面和确认策略。</summary>
internal static class TuiInteractionChecks
{
    public static async Task RunAsync(string root)
    {
        await using var session = new HeadlessPlayerSession(Path.Combine(root, "tui-" + Guid.NewGuid().ToString("N")));
        var router = new PlayerCommandRouter(session); using var terminal = new PlayerTerminalSession(router);
        var tui = new ConsoleTuiView(session, router); tui.SetView(await session.CaptureTuiAsync(default));
        void Press(ConsoleKeyInfo key) => Require(tui.HandleKey(key, terminal) is null, "Navigation key must complete synchronously");
        var tabs = ConsoleTuiView.Tabs;
        Require(tabs[tabs.ToList().IndexOf("Plugins") + 1] == "Terminal" && tabs[tabs.ToList().IndexOf("Terminal") + 1] == "Help" && tabs.Contains("Settings"), "Terminal order and settings tab");
        Require(ConsoleTuiView.Actions.Select(a => a.Name).ToHashSet().SetEquals(PlayerCommandRouter.Definitions.Where(d => !d.DesktopOnly).Select(d => d.Name)), "Every CLI operation is exposed to keyboard action forms");
        foreach (var action in ConsoleTuiView.Actions)
        {
            var form = TuiForm.FromCommand(action, name => name == "json-value" ? "1" : "value", _ => []);
            Require(CommandLineParser.Parse(form.Build()).Name == action.Name, "Keyboard form preserves public command syntax: " + action.Name);
        }
        Press(Key(ConsoleKey.F7)); Require(tui.Page == "Music", "Function keys do not select pages");
        foreach (var unused in Enumerable.Range(0, 7)) Press(Key(ConsoleKey.Tab));
        Require(tui.Page == "Settings", "Tab reaches settings");
        Press(Key(ConsoleKey.Enter)); Require(tui.Form is { Title.Length: > 0 } && tui.Form.Fields[0].Value == "80", "Settings selected with keyboard and opened as a typed form");
        tui.Form!.Fields[0].Value = "42"; await tui.HandleKey(Key(ConsoleKey.Enter), terminal)!;
        Require(session.State.Settings.Volume == 42, "Settings TUI uses same business state");
        Press(Key(ConsoleKey.Escape));
        tui.SelectPage("Music"); terminal.SetDraft("unfinished"); Press(Key(ConsoleKey.Z, 'z'));
        Require(terminal.Snapshot.Draft == "unfinished", "Other pages cannot edit command draft");
        Require(!tui.RenderFrame(terminal.Snapshot, 100, 30).Contains("nonet $ "), "Command prompt is absent outside terminal");
        tui.SelectPage("Terminal"); terminal.SetDraft("player volume 44"); await terminal.SubmitAsync();
        Press(Key(ConsoleKey.UpArrow)); Require(terminal.Snapshot.Draft == "player volume 44", "Up arrow recalls history without Alt");
        Press(Key(ConsoleKey.DownArrow)); Require(terminal.Snapshot.Draft == "", "Down arrow restores draft");
        Require(tui.RenderFrame(terminal.Snapshot, 80, 24).Contains("nonet $ ") && !tui.RenderFrame(terminal.Snapshot, 80, 24).Contains("F1"), "Terminal owns its input and no function-key navigation is displayed");
        var clear = tui.HandleKey(new('\0', ConsoleKey.L, false, false, true), terminal); await clear!;
        Require(terminal.Snapshot.Transcript.Length == 0, "Ctrl+L clears terminal");
        await router.ExecuteAsync("playlist create '待确认歌单'"); tui.SetView(await session.CaptureTuiAsync(default));
        tui.SelectPage("Playlists"); Press(Key(ConsoleKey.DownArrow));
        var delete = tui.HandleKey(Key(ConsoleKey.Delete), terminal)!;
        Require(!delete.IsCompleted && session.State.Playlists.Count == 2 && tui.RenderFrame(terminal.Snapshot, 80, 24).Contains("playlist delete"), "TUI destructive action is blocked on confirmation");
        Press(Key(ConsoleKey.Enter)); await delete; Require(session.State.Playlists.Count == 2, "Enter defaults to cancellation");
        Press(Key(ConsoleKey.Escape)); delete = tui.HandleKey(Key(ConsoleKey.Delete), terminal)!;
        Press(Key(ConsoleKey.Y, 'y')); await delete; Require(session.State.Playlists.Count == 1, "Y authorizes the selected deletion");
        var name = "-y"; var definition = PlayerCommandRouter.Definitions.Single(d => d.Name == "playlist.create");
        var safe = TuiForm.FromCommand(definition, _ => name, _ => []); var parsed = CommandLineParser.Parse(safe.Build());
        Require(parsed.Arguments.Single() == name && !parsed.Confirmed, "Parameter text cannot become a confirmation flag");
        var caret = "A𠮷é".Length; var edited = ConsoleTuiView.EditText(Key(ConsoleKey.Backspace), "A𠮷é", ref caret);
        Require(edited == "A𠮷", "Editing preserves Unicode text elements");
        tui.SelectPage("Terminal"); File.WriteAllText(Path.Combine(root, "tui-terminal-preview.txt"), tui.RenderFrame(terminal.Snapshot, 100, 30));
        tui.SelectPage("Settings"); tui.SetView(await session.CaptureTuiAsync(default));
        File.WriteAllText(Path.Combine(root, "tui-settings-preview.txt"), tui.RenderFrame(terminal.Snapshot, 100, 30));
        Console.WriteLine("PASS TUI: isolated terminal, history, settings, all keyboard actions, forms and confirmation");
    }
    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0') => new(character, key, false, false, false);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
