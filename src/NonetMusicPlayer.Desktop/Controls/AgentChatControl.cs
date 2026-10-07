using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Controls;

/// <summary>宿主提供聊天视图和受控执行器；模型不能取得任意 VM 方法或跳过确认。</summary>
public sealed class AgentChatControl : UserControl, IDisposable, IPluginKeyboardScope
{
    private readonly PluginManager _manager;
    private readonly PluginManifest _plugin;
    private readonly List<AgentMessage> _messages = [];
    private readonly TextBox _transcript, _input;
    private readonly StackPanel _conversation = new() { Spacing = 16 };
    private readonly ScrollViewer _scroll;
    private readonly Button _send, _cancel, _clear, _retry;
    private readonly TextBlock _status;
    private readonly ProgressBar _activity;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private bool _disposed, _networkConsent;
    private string _lastInput = "";
    private readonly HashSet<string> _declined = [];
    private MainWindow Owner => TopLevel.GetTopLevel(this) as MainWindow ?? throw new InvalidOperationException("Player is not connected.");
    private MainViewModel Vm => Owner.DataContext as MainViewModel ?? throw new InvalidOperationException("Player is not connected.");
    public AgentChatControl(PluginManager manager, PluginManifest plugin)
    {
        _manager = manager; _plugin = plugin; Name = "AgentChat";
        _transcript = ReadOnly(L10n.T("Agent.Welcome")); _transcript.Name = "AgentTranscript";
        _transcript.Margin = new(4, 30); _conversation.Children.Add(_transcript);
        _input = new TextBox { Name = "AgentInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000,
            MinHeight = 80, MaxHeight = 170, PlaceholderText = L10n.T("Agent.InputHint") };
        FullTextToolTips.SetEnabled(_input, false);
        _status = Ui.Text("Agent.Privacy", 12, true); _status.TextWrapping = TextWrapping.Wrap;
        _activity = new ProgressBar { IsIndeterminate = true, Height = 2, IsVisible = false };
        _send = Ui.AsyncButton(L10n.T("Agent.Send"), SendAsync, true);
        _cancel = Ui.Button(L10n.T("Common.Cancel"), () => _request?.Cancel());
        _clear = Ui.Button(L10n.T("Agent.Clear"), () =>
        {
            if (_request is not null) return;
            _messages.Clear(); _conversation.Children.Clear(); _conversation.Children.Add(_transcript);
            _transcript.IsVisible = true; _lastInput = ""; _status.Text = L10n.T("Agent.Privacy");
        });
        _retry = Ui.Button(L10n.T("Agent.RestoreInput"), () => { _input.Text = _lastInput; _input.Focus(); });
        var configure = Ui.AsyncButton(L10n.T("Plugins.PluginConfiguration"), async () => await Owner.OpenPluginConfigurationAsync(plugin));
        _send.Name = "AgentSend"; _cancel.Name = "AgentCancel"; _clear.Name = "AgentClear";
        _input.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; await SendAsync(); } };
        var config = manager.ConfigurationValues(plugin);
        var model = Ui.RawText(config["model"]?.GetValue<string>() is { Length: > 0 } id ? id : L10n.T("Agent.ConfigureFirst"), 13, true);
        model.TextTrimming = TextTrimming.CharacterEllipsis;
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = new(0, 0, 0, 12) };
        toolbar.Children.Add(model); var tools = Ui.Actions(_clear, configure); Grid.SetColumn(tools, 1); toolbar.Children.Add(tools);
        _scroll = new ScrollViewer { Name = "AgentConversation", Content = _conversation, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var sendActions = Ui.Actions(_retry, _cancel, _send); sendActions.HorizontalAlignment = HorizontalAlignment.Right;
        var composer = Ui.Stack(_input, sendActions); composer.Margin = new(0, 12, 0, 10);
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto,Auto,Auto"), MinHeight = 320 };
        layout.Children.Add(toolbar); Grid.SetRow(_scroll, 1); layout.Children.Add(_scroll);
        Grid.SetRow(_activity, 2); layout.Children.Add(_activity); Grid.SetRow(composer, 3); layout.Children.Add(composer);
        Grid.SetRow(_status, 4); layout.Children.Add(_status); Content = layout; Refresh();
    }
    /// <summary>对话及工具记录只能选择复制，消息输入框是唯一可编辑的聊天控件。</summary>
    private static TextBox ReadOnly(string text)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0), MinHeight = 0 };
        FullTextToolTips.SetEnabled(box, false); return box;
    }
    private void Refresh()
    {
        var busy = _request is not null; _send.IsEnabled = _clear.IsEnabled = !busy && !_disposed;
        _input.IsEnabled = !_disposed; _cancel.IsVisible = busy; _activity.IsVisible = busy;
        _retry.IsVisible = !busy && _lastInput.Length > 0;
    }
    private void Append(string role, string text, string? reasoning = null)
    {
        var follow = _scroll.Offset.Y + _scroll.Viewport.Height >= _scroll.Extent.Height - 30;
        _transcript.IsVisible = false;
        var content = Ui.Stack(); var user = role == L10n.T("Agent.You");
        var heading = Ui.RawText(role, 12, true); content.Children.Add(heading);
        if (!string.IsNullOrWhiteSpace(reasoning))
            content.Children.Add(new Expander { Header = L10n.T("Agent.Reasoning"), Content = ReadOnly(reasoning) });
        var message = ReadOnly(text); message.Name = "AgentMessageText"; content.Children.Add(message);
        var copy = Ui.AsyncButton(L10n.T("Agent.Copy"), async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { var data = new DataTransfer(); data.Add(DataTransferItem.CreateText(text)); await clipboard.SetDataAsync(data); } });
        copy.HorizontalAlignment = HorizontalAlignment.Right; copy.Classes.Add("quiet"); content.Children.Add(copy);
        var card = new Border { Child = content, Padding = new(16, 12), CornerRadius = new(14),
            Background = Ui.Brush(user ? "NavSelectedBrush" : "SurfaceBrush"),
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Stretch, MaxWidth = user ? 640 : 920 };
        _conversation.Children.Add(card);
        // 只保留有限 UI 卡片；清空上下文会提示用户，不悄悄假装仍记得早期事务。
        while (_conversation.Children.Count > 100) _conversation.Children.RemoveAt(0);
        if (follow || user) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_disposed) _scroll.ScrollToEnd(); });
    }
    private void ToolRecord(string summary, string json)
    {
        _transcript.IsVisible = false;
        var details = new Expander { Header = L10n.T("Agent.Tool") + " · " + summary, Content = ReadOnly(json) };
        _conversation.Children.Add(new Border { Child = details, Padding = new(12, 8), CornerRadius = new(10),
            BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new(1) });
        if (_scroll.Offset.Y + _scroll.Viewport.Height >= _scroll.Extent.Height - 50)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_disposed) _scroll.ScrollToEnd(); });
    }
    private async Task SendAsync()
    {
        if (_disposed || _request is not null || string.IsNullOrWhiteSpace(_input.Text)) return;
        var config = _manager.ConfigurationValues(_plugin);
        if (string.IsNullOrWhiteSpace(config["baseUrl"]?.GetValue<string>()) || string.IsNullOrWhiteSpace(config["model"]?.GetValue<string>()))
        { _status.Text = L10n.T("Agent.ConfigureFirst"); return; }
        if (_messages.Count > 30 || _messages.Sum(m => m.Text.Length + (m.Reasoning?.Length ?? 0)) > 65_000)
        { _messages.Clear(); Append(_plugin.Name, L10n.T("Agent.ContextReset")); }
        var prior = _messages.Count; _declined.Clear();
        var text = _input.Text.Trim(); _lastInput = text; _input.Text = "";
        _messages.Add(new("user", text)); Append(L10n.T("Agent.You"), text);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _request = request; Refresh();
        try
        {
            if (!_networkConsent)
            {
                var url = config["baseUrl"]!.GetValue<string>();
                var approved = await PlayerDialog.Confirm(Owner, L10n.T("Agent.NetworkConfirm"),
                    L10n.T("Agent.Privacy") + "\n\n" + url, L10n.T("Common.Confirm"));
                EnsureActive(request.Token);
                if (!approved) { _messages.RemoveRange(prior, _messages.Count - prior); _status.Text = L10n.T("Agent.Cancelled"); return; }
                _networkConsent = true;
            }
            for (var round = 0; round < 8; round++)
            {
                EnsureActive(request.Token); _status.Text = L10n.T("Agent.Thinking");
                var reply = await _manager.AgentStepAsync(_plugin, new(_messages.ToArray(), AgentCommandPolicy.Catalog()), request.Token);
                EnsureActive(request.Token);
                _messages.Add(new("assistant", reply.Text, reply.Calls, Reasoning: reply.Reasoning));
                if (reply.Text.Length > 0) Append(_plugin.Name, reply.Text, reply.Reasoning);
                if (reply.Calls.Length == 0)
                {
                    _status.Text = reply.InputTokens is { } incoming && reply.OutputTokens is { } outgoing
                        ? L10n.Format("Agent.Usage", incoming, outgoing) : L10n.T("Agent.Privacy");
                    if (reply.FinishReason == "length") _status.Text += " · " + L10n.T("Agent.OutputLimited");
                    return;
                }
                foreach (var call in reply.Calls)
                {
                    var result = await ExecuteToolAsync(call, request.Token);
                    EnsureActive(request.Token);
                    _messages.Add(new("tool", result, ToolCallId: call.Id));
                }
            }
            _messages.RemoveRange(prior, _messages.Count - prior);
            _status.Text = L10n.T("Agent.Limit");
        }
        catch (OperationCanceledException)
        {
            if (_messages.Count > prior) _messages.RemoveRange(prior, _messages.Count - prior);
            if (!_disposed) _status.Text = L10n.T("Agent.Cancelled");
        }
        catch (Exception error)
        {
            if (_messages.Count > prior) _messages.RemoveRange(prior, _messages.Count - prior);
            if (!_disposed) { _status.Text = L10n.T("Agent.Failed"); Vm.ReportError(_status.Text, error); }
        }
        finally { _request = null; if (!_disposed) Refresh(); }
    }
    private void EnsureActive(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || !_plugin.Enabled || !_manager.Installed.Contains(_plugin)) throw new OperationCanceledException();
    }
    private async Task<string> ExecuteToolAsync(AgentToolCall call, CancellationToken token)
    {
        try
        {
            EnsureActive(token);
            if (_declined.Contains(call.Operation)) return """{"success":false,"reason":"User declined this operation; do not retry."}""";
            var command = AgentCommandPolicy.ValidateAndEncode(call);
            ValidateReferences(call);
            if (AgentCommandPolicy.RequiresConfirmation(call.Operation))
            {
                var approved = await PlayerDialog.Confirm(Owner, L10n.T("Agent.Confirm"), command, L10n.T("Common.Confirm"));
                EnsureActive(token);
                if (!approved) { _declined.Add(call.Operation); Append(L10n.T("Agent.Tool"), call.Operation + " · " + L10n.T("Common.Cancel")); return """{"success":false,"reason":"User declined. Do not retry this action."}"""; }
            }
            EnsureActive(token);
            var result = await Owner.Commands.ExecuteAsync(command, _ => Task.FromResult(true), token);

            var payload = new JsonObject { ["success"] = result.Success, ["data"] = AgentCommandPolicy.Sanitize(result.Data) };
            var json = payload.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true });
            var bounded = json.Length <= 18_000 ? json : new JsonObject { ["success"] = result.Success, ["truncated"] = true, ["hint"] = "Narrow the query." }.ToJsonString();
            if (!_disposed) ToolRecord(call.Operation + " · " + (result.Success ? L10n.T("Agent.Done") : L10n.T("Agent.ToolFailed")), bounded);
            return bounded;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is InvalidDataException or FormatException or InvalidOperationException)
        { Append(L10n.T("Agent.Tool"), L10n.T("Agent.ToolRejected")); return """{"success":false,"reason":"Operation or arguments are not allowed."}"""; }
    }
    private void ValidateReferences(AgentToolCall call)
    {
        var tracks = Vm.State.Tracks.Concat(Vm.State.RecentTemporaryTracks).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        void Track(string id) { if (!tracks.Contains(id)) throw new InvalidDataException("Unknown track ID; paths are not allowed."); }
        void Playlist(string id) { if (!Vm.Playlists.Any(p => p.Id == id)) throw new InvalidDataException("Unknown playlist ID."); }
        if (call.Operation is "player.play" or "music.info" or "favorite.add" or "favorite.remove") { if (call.Arguments.Length > 0) Track(call.Arguments[0]); }
        if (call.Operation == "music.remove") foreach (var id in call.Arguments) Track(id);
        if (call.Operation.StartsWith("playlist.", StringComparison.Ordinal) && call.Operation is not ("playlist.list" or "playlist.create")) Playlist(call.Arguments[0]);
        if (call.Operation is "playlist.add" or "playlist.remove") foreach (var id in call.Arguments.Skip(1)) Track(id);
        if (call.Operation == "playlist.move") Track(call.Arguments[1]);
        if (call.Operation == "queue.play") { Playlist(call.Arguments[0]); if (call.Arguments.Length > 1) Track(call.Arguments[1]); }
        if (call.Operation is "plugins.enable" or "plugins.disable" && !Vm.Plugins.Installed.Any(p => p.Id == call.Arguments[0])) throw new InvalidDataException("Unknown plugin ID.");
        if (call.Operation == "navigate" && call.Arguments[0].StartsWith("playlist:", StringComparison.Ordinal)) Playlist(call.Arguments[0][9..]);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _request?.Cancel(); _messages.Clear(); _conversation.Children.Clear(); _input.Text = ""; _lastInput = ""; _lifetime.Dispose(); Refresh();
    }
}
