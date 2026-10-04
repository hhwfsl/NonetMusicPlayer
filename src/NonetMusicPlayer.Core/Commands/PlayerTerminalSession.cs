using System.Text.Json;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Persistence;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>与呈现框架无关的终端会话；桌面和系统终端共享提示符、历史、确认及事务输出。</summary>
public sealed class PlayerTerminalSession : IDisposable
{
    public const string CommandPrompt = "nonet $ ";
    private const int TranscriptLimit = 8 * 1024 * 1024;
    private readonly PlayerCommandRouter _router;
    private readonly object _gate = new();
    private readonly List<string> _history = [];
    private readonly List<TerminalSegment> _segments = [];
    private string _transcript = "", _draft = "", _savedDraft = "";
    private int _historyIndex;
    private bool _busy, _disposed;
    private TaskCompletionSource<bool>? _confirmation;
    private string? _confirmationPrompt;
    private TerminalOptions _options;
    private int _storedLines, _storedCharacters;
    public TerminalOptions Options { get { lock (_gate) return _options; } }

    public event EventHandler? Changed;
    /// <summary>增量输出；null 表示清屏。宿主须在自身 UI/控制台线程呈现。</summary>
    public event Action<string?>? OutputPublished;

    public PlayerTerminalSession(PlayerCommandRouter router, TerminalOptions? options = null)
    {
        _router = router;
        _options = (options ?? new()).Normalize();
        foreach (var entry in AppLog.RecentEntries) AcceptLog(entry);
        router.ResultPublished += AcceptResult;
        AppLog.EntryWritten += AcceptLog;
    }

    public void Configure(TerminalOptions options)
    {
        lock (_gate) { _options = options.Normalize(); TrimTranscript(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public TerminalSnapshot Snapshot
    {
        get
        {
            lock (_gate) return new(_transcript, _confirmationPrompt ?? (_busy ? "" : CommandPrompt), _draft, !_busy || _confirmation is not null);
        }
    }

    /// <summary>只修改当前可编辑行，禁止粘贴多行自动执行，避免意外批量事务。</summary>
    public void SetDraft(string value)
    {
        lock (_gate)
        {
            if (_disposed || (_busy && _confirmation is null)) return;
            _draft = value.Replace('\r', ' ').Replace('\n', ' ');
            if (_draft.Length > 16384) _draft = _draft[..16384];
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Recall(int direction)
    {
        lock (_gate)
        {
            if (_disposed || _busy || _history.Count == 0) return;
            if (_historyIndex == _history.Count) _savedDraft = _draft;
            _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
            _draft = _historyIndex == _history.Count ? _savedDraft : _history[_historyIndex];
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SubmitAsync(CancellationToken cancellationToken = default)
    {
        string input, echoPrompt; TaskCompletionSource<bool>? confirmation;
        lock (_gate)
        {
            if (_disposed || (_busy && _confirmation is null)) return;
            input = _draft.Trim(); confirmation = _confirmation; echoPrompt = _confirmationPrompt ?? CommandPrompt; _draft = "";
            if (confirmation is not null) { _confirmation = null; _confirmationPrompt = null; }
            else
            {
                _busy = true;
                // 配置可能含令牌；不回显配置值，不加入方向键历史。
                if (input.Length > 0 && !IsPrivate(input))
                {
                    if (_history.Count == 0 || _history[^1] != input) _history.Add(input);
                    if (_history.Count > 100) _history.RemoveAt(0);
                }
                _historyIndex = _history.Count; _savedDraft = "";
            }
        }
        Append(echoPrompt + (IsPrivate(input) ? LocalizationCatalog.Get("Terminal.PrivateConfig") : input) + Environment.NewLine);
        if (confirmation is not null) { confirmation.TrySetResult(input.Equals("y", StringComparison.OrdinalIgnoreCase)); return; }
        try { if (input.Length > 0) await _router.ExecuteAsync(input, ConfirmAsync, cancellationToken); }
        finally
        {
            lock (_gate) _busy = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private Task<bool> ConfirmAsync(CommandDefinition definition)
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(false);
            _confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmationPrompt = LocalizationCatalog.Format("Terminal.ConfirmPrompt", CommandSyntax.PublicName(definition.Name));
            Changed?.Invoke(this, EventArgs.Empty);
            return _confirmation.Task;
        }
    }

    /// <summary>Ctrl+C 取消当前确认或清除草稿；不执行系统中断或强行终止后台写入。</summary>
    public void CancelInput()
    {
        TaskCompletionSource<bool>? confirmation;
        lock (_gate) { confirmation = _confirmation; _confirmation = null; _confirmationPrompt = null; _draft = ""; }
        Append("^C" + Environment.NewLine); confirmation?.TrySetResult(false);
    }

    public void WriteNotice(string message) => AppLog.Info("Terminal", message);
    public Task<CommandResult> ClearAsync(CancellationToken cancellationToken = default) => _router.ExecuteAsync("nonet clear", cancellationToken: cancellationToken);
    private void AcceptResult(object? sender, CommandResult result)
    {
        if (result.Operation == "clear" && result.Success)
        {
            lock (_gate) { _segments.Clear(); _transcript = ""; _storedLines = _storedCharacters = 0; }
            OutputPublished?.Invoke(null); Changed?.Invoke(this, EventArgs.Empty); return;
        }
        // 查询载荷不是日志，单独显示；事务摘要由 EntryWritten 在落盘后统一发布。
        if (result.Operation == "help") Append(result.Message + Environment.NewLine);
        else if (result.Data is not null) Append(FormatData(result.Data) + Environment.NewLine);
    }

    private void AcceptLog(LogEntry entry)
    {
        Append(entry.Text + Environment.NewLine, entry.Level);
    }

    public static string FormatResult(CommandResult result)
    {
        var text = result.Message;
        if (result.Data is null) return text;
        return text + Environment.NewLine + FormatData(result.Data);
    }

    private static string FormatData(System.Text.Json.Nodes.JsonNode value)
    {
        var data = value.ToJsonString(CoreJson.Readable);
        if (data.Length > 16384) data = data[..16384] + Environment.NewLine + LocalizationCatalog.Get("Terminal.StructuredOutput");
        return data;
    }

    private static bool IsPrivate(string input) => CommandSyntax.IsPrivate(input);
    private void Append(string value, TerminalLogLevel? level = null)
    {
        bool visible;
        lock (_gate)
        {
            if (_disposed) return;
            _segments.Add(new(value, level));
            _storedLines += value.Count(c => c == '\n'); _storedCharacters += value.Length;
            TrimTranscript();
            visible = level is null || level >= _options.MinimumLevel;
        }
        if (visible) { OutputPublished?.Invoke(value); Changed?.Invoke(this, EventArgs.Empty); }
    }

    private void TrimTranscript()
    {
        while (_segments.Count > 0 && (_storedLines > _options.ScrollbackLines || _storedCharacters > TranscriptLimit))
        {
            var segment = _segments[0]; var value = segment.Text;
            var excess = _storedLines - _options.ScrollbackLines;
            var lines = value.Count(c => c == '\n'); var remove = 0;
            if (lines <= excess || value.Length <= _storedCharacters - TranscriptLimit) remove = value.Length;
            else
            {
                for (var i = 0; i < value.Length; i++)
                    if (value[i] == '\n' && (--excess <= 0 || i + 1 >= _storedCharacters - TranscriptLimit)) { remove = i + 1; break; }
                if (remove == 0) remove = Math.Min(value.Length, Math.Max(1, _storedCharacters - TranscriptLimit));
            }
            _storedLines -= value[..remove].Count(c => c == '\n'); _storedCharacters -= remove;
            if (remove == value.Length) _segments.RemoveAt(0); else _segments[0] = segment with { Text = value[remove..] };
        }
        _transcript = string.Concat(_segments.Where(s => s.Level is null || s.Level >= _options.MinimumLevel).Select(s => s.Text));
    }
    private sealed record TerminalSegment(string Text, TerminalLogLevel? Level);

    public void Dispose()
    {
        _router.ResultPublished -= AcceptResult;
        AppLog.EntryWritten -= AcceptLog;
        lock (_gate) { _disposed = true; _confirmation?.TrySetResult(false); _confirmation = null; }
    }
}

/// <summary>输出前缀永远只读，可编辑位置从当前提示符之后开始。</summary>
public sealed record TerminalSnapshot(string Transcript, string Prompt, string Draft, bool AcceptsInput)
{
    public string Prefix => Transcript + Prompt;
    public string Text => Prefix + Draft;
}
