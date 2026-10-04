using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Runtime;
using NonetMusicPlayer.Core.Diagnostics;

namespace NonetMusicPlayerCli;

/// <summary>滚动式控制台 REPL。只重画当前输入行，结果向系统终端的滚动历史追加，不固定底部输入栏。</summary>
internal sealed class ConsolePlayerView(HeadlessPlayerSession session, PlayerCommandRouter router)
{
    private readonly ConcurrentQueue<string?> _output = new();
    private int _caret, _drawnCaretRow, _drawnWidth;
    private bool _drawn;
    private string _drawnText = "";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TerminalOptions Options() => new(session.State.Settings.TerminalScrollbackLines, MinimumLevel: TerminalOptions.ParseLevel(session.State.Settings.TerminalMinimumLogLevel));
        using var terminal = new PlayerTerminalSession(router, Options());
        void Configure(object? sender, CommandResult result) { if (result.Success && result.Operation.StartsWith("settings.", StringComparison.Ordinal)) terminal.Configure(Options()); }
        router.ResultPublished += Configure;
        terminal.OutputPublished += QueueOutput;
        session.PluginDownloadProgress += Progress;
        var tasks = new List<Task>();
        try
        {
            Console.WriteLine("NonetMusicPlayerCli  ·  help  ·  clear  ·  exit");
            while (!cancellationToken.IsCancellationRequested)
            {
                FlushOutput();
                if (session.ExitRequested) break;
                var state = terminal.Snapshot;
                DrawInput(state);
                if (!Console.KeyAvailable) { await Task.Delay(20, cancellationToken); continue; }
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.PageUp) { await ReviewScrollbackAsync(terminal, cancellationToken); continue; }
                if (!state.AcceptsInput) continue;
                var draft = state.Draft;
                _caret = Math.Clamp(_caret, 0, draft.Length);
                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.L)
                { tasks.Add(terminal.ClearAsync(cancellationToken)); continue; }
                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.D && draft.Length == 0) break;
                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is ConsoleKey.A or ConsoleKey.E or ConsoleKey.U)
                {
                    if (key.Key == ConsoleKey.U) terminal.SetDraft(draft[_caret..]);
                    _caret = key.Key == ConsoleKey.E ? draft.Length : 0; continue;
                }
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        // 提交本行后会话会产生回显及结果，由唯一绘制循环串行输出。
                        _caret = 0; tasks.Add(terminal.SubmitAsync(cancellationToken)); break;
                    case ConsoleKey.Backspace:
                        if (_caret > 0) { var start = Previous(draft, _caret); terminal.SetDraft(draft.Remove(start, _caret - start)); _caret = start; } break;
                    case ConsoleKey.Delete:
                        if (_caret < draft.Length) terminal.SetDraft(draft.Remove(_caret, Next(draft, _caret) - _caret)); break;
                    case ConsoleKey.LeftArrow: _caret = Previous(draft, _caret); break;
                    case ConsoleKey.RightArrow: _caret = Next(draft, _caret); break;
                    case ConsoleKey.Home: _caret = 0; break;
                    case ConsoleKey.End: _caret = draft.Length; break;
                    case ConsoleKey.Escape: terminal.SetDraft(""); _caret = 0; break;
                    case ConsoleKey.UpArrow: case ConsoleKey.DownArrow:
                        terminal.Recall(key.Key == ConsoleKey.UpArrow ? -1 : 1); _caret = terminal.Snapshot.Draft.Length; break;
                    default:
                        if (!char.IsControl(key.KeyChar) && draft.Length < 16384) { terminal.SetDraft(draft.Insert(_caret, key.KeyChar.ToString())); _caret++; } break;
                }
                // 已结束的命令不永久保留 Task；故障在这里观察，而不成为未观察异常。
                foreach (var completed in tasks.Where(t => t.IsCompleted).ToArray()) { await completed; tasks.Remove(completed); }
            }
            await Task.WhenAll(tasks); AppLog.Flush(); FlushOutput(); EraseInput();
        }
        finally
        {
            terminal.Dispose();
            terminal.OutputPublished -= QueueOutput; session.PluginDownloadProgress -= Progress;
            router.ResultPublished -= Configure;
            // 保留退出前的输出供系统终端复制和回看，不清除整个屏幕。
            Console.Write("\u001b[0m");
        }
    }

    private void QueueOutput(string? value) => _output.Enqueue(value);
    private int _lastProgress = -1;
    private void Progress(object? sender, double percent)
    {
        var step = (int)percent; if (step == _lastProgress) return; _lastProgress = step;
        AppLog.Info("Plugins", LocalizationCatalog.Format("Terminal.DownloadProgress", percent));
    }
    /// <summary>应用自身提供有界回看；外层 CMD/终端模拟器的滚动缓存仍由其设置管理。</summary>
    private async Task ReviewScrollbackAsync(PlayerTerminalSession terminal, CancellationToken token)
    {
        EraseInput(); var lines = terminal.Snapshot.Transcript.Split('\n'); var rows = Math.Max(3, Console.WindowHeight - 2);
        var top = Math.Max(0, lines.Length - rows * 2);
        Console.Write("\u001b[?1049h");
        try
        {
            while (!token.IsCancellationRequested)
            {
                Console.Write("\u001b[2J\u001b[H");
                Console.WriteLine($"{top + 1}/{lines.Length}  ·  PgUp / PgDn  ·  Esc / End");
                foreach (var line in lines.Skip(top).Take(rows)) Console.WriteLine(string.Concat(line.Where(c => !char.IsControl(c) || c == '\t')));
                // 回看历史时也允许 Ctrl+C 及时取消；不阻塞在等待下一次按键上。
                while (!Console.KeyAvailable && !token.IsCancellationRequested) await Task.Delay(20, token);
                token.ThrowIfCancellationRequested();
                var key = Console.ReadKey(true);
                if (key.Key is ConsoleKey.End or ConsoleKey.Escape or ConsoleKey.Enter) break;
                top = key.Key switch { ConsoleKey.PageUp => Math.Max(0, top - rows), ConsoleKey.PageDown => Math.Min(Math.Max(0, lines.Length - rows), top + rows), ConsoleKey.UpArrow => Math.Max(0, top - 1), ConsoleKey.DownArrow => Math.Min(Math.Max(0, lines.Length - rows), top + 1), _ => top };
            }
        }
        finally { Console.Write("\u001b[?1049l"); _drawn = false; }
    }
    private void FlushOutput()
    {
        while (_output.TryDequeue(out var output))
        {
            EraseInput();
            if (output is null) Console.Write("\u001b[3J\u001b[2J\u001b[H");
            else Console.Write(string.Concat(output.Where(c => !char.IsControl(c) || c is '\r' or '\n' or '\t')));
        }
    }
    private void EraseInput()
    {
        if (!_drawn) return;
        Console.Write("\r");
        if (_drawnCaretRow > 0) Console.Write($"\u001b[{_drawnCaretRow}A");
        Console.Write("\u001b[0J"); _drawn = false; _drawnText = "";
    }
    private void DrawInput(TerminalSnapshot state)
    {
        // TERM=dumb 和重定向输入在入口使用逐行模式，不读取窗口尺寸或发送 ANSI。
        var width = Math.Max(8, Console.WindowWidth);
        var privateInput = CommandSyntax.IsPrivate(state.Draft);
        var visible = privateInput ? LocalizationCatalog.Get("Terminal.PrivateConfig") : state.Draft;
        var text = state.Prompt + visible;
        var caret = state.Prompt.Length + (privateInput ? visible.Length : Math.Min(_caret, state.Draft.Length));
        if (_drawn && _drawnText == text && _drawnWidth == width && _drawnCaret == caret) return;
        EraseInput();
        if (!state.AcceptsInput) return;
        // 分段显式换行，避免终端的延迟自动换行状态使清除上一行时偏移一行。
        var row = 0; var column = 0; var caretRow = 0; var caretColumn = 0; var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cells = CellWidth(rune);
            if (column + cells > width - 1) { Console.Write("\r\n"); row++; column = 0; }
            if (index == caret) { caretRow = row; caretColumn = column; }
            Console.Write(rune.ToString()); column += cells; index += rune.Utf16SequenceLength;
        }
        if (index == caret) { caretRow = row; caretColumn = column; }
        Console.Write("\r"); if (row > caretRow) Console.Write($"\u001b[{row - caretRow}A");
        if (caretColumn > 0) Console.Write($"\u001b[{caretColumn}C");
        _drawn = true; _drawnText = text; _drawnWidth = width; _drawnCaretRow = caretRow; _drawnCaret = caret;
    }
    private int _drawnCaret;
    // 文本元素粒度编辑，避免退格把代理对或组合字符拆成乱码。
    private static int Previous(string text, int index) => StringInfo.ParseCombiningCharacters(text).LastOrDefault(i => i < index);
    private static int Next(string text, int index) => StringInfo.ParseCombiningCharacters(text).FirstOrDefault(i => i > index, text.Length);
    private static int CellWidth(Rune rune) => rune.Value is >= 0x1100 and <= 0x115f or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3 or >= 0xf900 and <= 0xfaff or >= 0xfe10 and <= 0xfe6f or >= 0xff00 and <= 0xff60 or >= 0x1f300 and <= 0x1faff ? 2 : Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark ? 0 : 1;
}
