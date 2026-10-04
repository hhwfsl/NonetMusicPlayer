using System.Globalization;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Runtime;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayerCli;

/// <summary>纯键盘 TUI：页面操作和终端命令共用业务路由，命令编辑仅位于终端栏目。</summary>
internal sealed partial class ConsoleTuiView(HeadlessPlayerSession session, PlayerCommandRouter router)
{
    internal static IReadOnlyList<string> Tabs { get; } = ["Music", "Playlists", "Recent", "Statistics", "Plugins", "Terminal", "Help", "Settings"];
    internal static IReadOnlyList<CommandDefinition> Actions { get; } = PlayerCommandRouter.Definitions.Where(d => !d.DesktopOnly).ToArray();
    private int _page, _selected, _caret, _historyOffset, _resultOffset, _actionIndex;
    private TuiSnapshot? _view;
    private string? _playlist, _lastPaint, _notice;
    private TuiForm? _form;
    private bool _palette, _executing, _exit;
    private string[]? _result;
    private TaskCompletionSource<bool>? _confirmation;
    private string _confirmationText = "";
    private PlayerTerminalSession? _terminal;
    private static string T(string key) => LocalizationCatalog.Get("Tui." + key);
    internal string Page => Tabs[_page];
    internal TuiForm? Form => _form;
    internal void SetView(TuiSnapshot view) => _view = view;
    internal void SelectPage(string name) { _page = Tabs.ToList().IndexOf(name); if (_page < 0) _page = 0; _selected = 0; _playlist = null; }

    public async Task RunAsync(CancellationToken token)
    {
        using var terminal = new PlayerTerminalSession(router, new(session.State.Settings.TerminalScrollbackLines, MinimumLevel: TerminalOptions.ParseLevel(session.State.Settings.TerminalMinimumLogLevel)));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token); _terminal = terminal;
        var pending = new List<Task>(); Task<TuiSnapshot>? sample = null; var nextFrame = DateTimeOffset.MinValue; var nextSample = DateTimeOffset.MinValue;
        void Configure(object? sender, CommandResult result) => terminal.Configure(new(session.State.Settings.TerminalScrollbackLines, MinimumLevel: TerminalOptions.ParseLevel(session.State.Settings.TerminalMinimumLogLevel)));
        router.ResultPublished += Configure; Console.Write("\u001b[?1049h\u001b[?25l");
        try
        {
            while (!token.IsCancellationRequested && !session.ExitRequested && !_exit)
            {
                // 长事务与快照均不阻塞绘制和确认界面；输入忙碌时保留操作系统的原生输入队列。
                if (sample is null && DateTimeOffset.UtcNow >= nextSample) sample = session.CaptureTuiAsync(stop.Token);
                if (sample?.IsCompleted == true) { _view = await sample; sample = null; nextSample = DateTimeOffset.UtcNow.AddMilliseconds(250); }
                foreach (var done in pending.Where(p => p.IsCompleted).ToArray()) { await done; pending.Remove(done); }
                if (DateTimeOffset.UtcNow >= nextFrame) { Draw(terminal.Snapshot); nextFrame = DateTimeOffset.UtcNow.AddMilliseconds(100); }
                if (!Console.KeyAvailable || _confirmation is null && (_executing || !terminal.Snapshot.AcceptsInput)) { await Task.Delay(15, stop.Token); continue; }
                var task = HandleKey(Console.ReadKey(true), terminal, stop.Token); if (task is not null) pending.Add(task);
                nextFrame = DateTimeOffset.MinValue;
            }
        }
        finally
        {
            _confirmation?.TrySetResult(false); stop.Cancel();
            try { await Task.WhenAll(pending); if (sample is not null) await sample; } catch (OperationCanceledException) { }
            router.ResultPublished -= Configure; _terminal = null;
            Console.Write("\u001b[0m\u001b[?25h\u001b[?1049l");
        }
    }

    /// <summary>与控制台读取分离，方便以相同按键路径测试页面导航、历史和危险操作确认。</summary>
    internal Task? HandleKey(ConsoleKeyInfo key, PlayerTerminalSession terminal, CancellationToken token = default)
    {
        _terminal = terminal;
        if (_confirmation is not null)
        {
            if (key.Key == ConsoleKey.Y) _confirmation.TrySetResult(true);
            else if (key.Key is ConsoleKey.N or ConsoleKey.Escape or ConsoleKey.Enter) _confirmation.TrySetResult(false);
            return null;
        }
        if (_result is not null)
        {
            if (key.Key is ConsoleKey.Escape or ConsoleKey.Enter) { _result = null; _resultOffset = 0; }
            else if (key.Key is ConsoleKey.DownArrow or ConsoleKey.PageDown) _resultOffset = Math.Min(Math.Max(0, _result.Length - 1), _resultOffset + (key.Key == ConsoleKey.PageDown ? 8 : 1));
            else if (key.Key is ConsoleKey.UpArrow or ConsoleKey.PageUp) _resultOffset = Math.Max(0, _resultOffset - (key.Key == ConsoleKey.PageUp ? 8 : 1));
            return null;
        }
        if (_form is not null) return EditForm(key, token);
        if (_palette)
        {
            if (key.Key == ConsoleKey.Escape) _palette = false;
            else if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown)
                _actionIndex = Math.Clamp(_actionIndex + (key.Key is ConsoleKey.UpArrow or ConsoleKey.PageUp ? -1 : 1) * (key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown ? 8 : 1), 0, Actions.Count - 1);
            else if (key.Key == ConsoleKey.Enter) { _palette = false; return BeginAction(Actions[_actionIndex], token); }
            return null;
        }
        if (key.Key == ConsoleKey.Tab)
        {
            _page = (_page + (key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? Tabs.Count - 1 : 1)) % Tabs.Count;
            _selected = 0; _playlist = null; return null;
        }
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.K) { _palette = true; _actionIndex = 0; return null; }
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.D)
        {
            if (terminal.Snapshot.Prompt != PlayerTerminalSession.CommandPrompt) { terminal.SetDraft("n"); return terminal.SubmitAsync(token); }
            _exit = true; return null;
        }
        if (Page == "Terminal") return EditTerminal(key, terminal, token);
        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            _selected = Math.Clamp(_selected + (key.Key is ConsoleKey.UpArrow or ConsoleKey.PageUp ? -1 : 1) * (key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown ? 8 : 1), 0, Math.Max(0, Items().Count - 1)); return null;
        }
        if (key.Key == ConsoleKey.Home) { _selected = 0; return null; }
        if (key.Key == ConsoleKey.End) { _selected = Math.Max(0, Items().Count - 1); return null; }
        if (key.Key == ConsoleKey.Escape && _playlist is not null) { _playlist = null; _selected = 0; return null; }
        if (key.Key == ConsoleKey.Spacebar) return Execute(_view?.Playing == true ? "player pause" : "player resume", token);
        if (key.Key == ConsoleKey.N) return Execute("player next", token);
        if (key.Key == ConsoleKey.P) return Execute("player previous", token);
        if (key.Key is ConsoleKey.Add or ConsoleKey.OemPlus) return Execute("player volume " + Math.Min(100, (_view?.Volume ?? 80) + 5).ToString(CultureInfo.InvariantCulture), token);
        if (key.Key is ConsoleKey.Subtract or ConsoleKey.OemMinus) return Execute("player volume " + Math.Max(0, (_view?.Volume ?? 80) - 5).ToString(CultureInfo.InvariantCulture), token);
        if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow) return Execute("player seek " + Math.Clamp((_view?.Position ?? 0) + (key.Key == ConsoleKey.LeftArrow ? -5 : 5), 0, _view?.Duration ?? 0).ToString(CultureInfo.InvariantCulture), token);
        if (key.Key == ConsoleKey.M) return Execute("player mode " + (_view?.Mode switch { "RepeatAll" => "repeat-one", "RepeatOne" => "shuffle", _ => "repeat-all" }), token);
        var item = Items().ElementAtOrDefault(_selected);
        if (Page == "Playlists" && _playlist is null)
        {
            if (key.Key == ConsoleKey.Enter && item is not null) { _playlist = item.Id; _selected = 0; return null; }
            if (key.Key == ConsoleKey.C) return BeginNamedAction("playlist.create", token);
            if (key.Key == ConsoleKey.R && item is not null) return BeginNamedAction("playlist.rename", token);
            if (key.Key == ConsoleKey.Delete && item is not null) return Execute("playlist delete " + TuiForm.Quote(item.Id), token);
            if (key.Key == ConsoleKey.Q && item is not null) return Execute("queue play " + TuiForm.Quote(item.Id), token);
        }
        else if (Page is "Music" or "Recent" or "Playlists")
        {
            if (key.Key == ConsoleKey.Enter && item is not null) return Execute(_playlist is not null ? "queue play " + TuiForm.Quote(_playlist) + " " + TuiForm.Quote(item.Id) : "player play " + TuiForm.Quote(item.Id), token);
            if (key.Key == ConsoleKey.F && item is not null) return Execute("favorite " + (_view?.Favorites.Contains(item.Id) == true ? "remove " : "add ") + TuiForm.Quote(item.Id), token);
            if (key.Key == ConsoleKey.I && item is not null) return Execute("music info " + TuiForm.Quote(item.Id), token, true);
            if (key.Key == ConsoleKey.A) return BeginNamedAction("playlist.add", token);
            if (key.Key == ConsoleKey.Delete && item is not null)
                return Execute(Page == "Recent" ? "history remove " + TuiForm.Quote(item.Id) : _playlist is not null ? "playlist remove " + TuiForm.Quote(_playlist) + " " + TuiForm.Quote(item.Id) : "music remove " + TuiForm.Quote(item.Id), token);
            if (key.Key == ConsoleKey.Q && _playlist is not null) return Execute("queue play " + TuiForm.Quote(_playlist), token);
        }
        else if (Page == "Plugins")
        {
            if (key.Key == ConsoleKey.I) return BeginNamedAction("plugins.install", token);
            if (key.Key == ConsoleKey.Enter && item is not null) return Execute("plugins " + (item.Detail.EndsWith("ON", StringComparison.Ordinal) ? "disable " : "enable ") + TuiForm.Quote(item.Id), token);
            if (key.Key == ConsoleKey.Delete && item is not null) return BeginNamedAction("plugins.uninstall", token);
            if (key.Key == ConsoleKey.C && item is not null) return ConfigurePlugin(item.Id, token);
        }
        else if (Page == "Settings")
        {
            if (key.Key == ConsoleKey.Enter && item is not null && _view is not null)
            {
                if (item.Id == "deviceName") return SelectDevice(token);
                OpenSetting(item.Id, _view.Settings[item.Id]);
            }
            if (key.Key == ConsoleKey.R) return Execute("settings reset", token);
            if (key.Key == ConsoleKey.B) return BeginNamedAction("data.backup", token);
            if (key.Key == ConsoleKey.O) return BeginNamedAction("data.restore", token);
        }
        else if (Page == "Statistics" && key.Key == ConsoleKey.Enter) return BeginNamedAction("statistics", token);
        else if (Page == "Help" && key.Key == ConsoleKey.Enter) { _palette = true; _actionIndex = 0; }
        return null;
    }
    private Task? EditTerminal(ConsoleKeyInfo key, PlayerTerminalSession terminal, CancellationToken token)
    {
        var draft = terminal.Snapshot.Draft; _caret = Math.Clamp(_caret, 0, draft.Length);
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.L) { _historyOffset = 0; return terminal.ClearAsync(token); }
        if (key.Key is ConsoleKey.PageUp or ConsoleKey.PageDown) { _historyOffset = Math.Max(0, _historyOffset + (key.Key == ConsoleKey.PageUp ? 8 : -8)); return null; }
        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow) { terminal.Recall(key.Key == ConsoleKey.UpArrow ? -1 : 1); _caret = terminal.Snapshot.Draft.Length; return null; }
        if (key.Key == ConsoleKey.Enter) { _caret = 0; _historyOffset = 0; return terminal.SubmitAsync(token); }
        if (key.Key == ConsoleKey.Escape) { terminal.SetDraft(""); _caret = 0; return null; }
        terminal.SetDraft(EditText(key, draft, ref _caret)); return null;
    }
    internal static string EditText(ConsoleKeyInfo key, string text, ref int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var positions = StringInfo.ParseCombiningCharacters(text); var before = caret;
        switch (key.Key)
        {
            case ConsoleKey.Backspace: if (caret > 0) { caret = positions.LastOrDefault(i => i < before); return text.Remove(caret, before - caret); } break;
            case ConsoleKey.Delete: if (caret < text.Length) return text.Remove(caret, positions.FirstOrDefault(i => i > before, text.Length) - caret); break;
            case ConsoleKey.LeftArrow: caret = positions.LastOrDefault(i => i < before); break;
            case ConsoleKey.RightArrow: caret = positions.FirstOrDefault(i => i > before, text.Length); break;
            case ConsoleKey.Home: caret = 0; break;
            case ConsoleKey.End: caret = text.Length; break;
            default: if (!char.IsControl(key.KeyChar) && !key.Modifiers.HasFlag(ConsoleModifiers.Control) && text.Length < 16384) { text = text.Insert(caret, key.KeyChar.ToString()); caret++; } break;
        }
        return text;
    }
}
