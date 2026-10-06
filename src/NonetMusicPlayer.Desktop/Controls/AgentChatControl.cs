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
    private readonly Button _send, _cancel, _clear;
    private readonly TextBlock _status;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private bool _disposed;
    private bool _networkConsent;
    private readonly HashSet<string> _declined = [];
    private MainWindow Owner => TopLevel.GetTopLevel(this) as MainWindow ?? throw new InvalidOperationException("Player is not connected.");
    private MainViewModel Vm => Owner.DataContext as MainViewModel ?? throw new InvalidOperationException("Player is not connected.");
    public AgentChatControl(PluginManager manager, PluginManifest plugin)
    {
        _manager = manager; _plugin = plugin; Name = "AgentChat";
        _transcript = new TextBox { Name = "AgentTranscript", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 310, MinHeight = 160 };
        _input = new TextBox { Name = "AgentInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, Height = 85, PlaceholderText = L10n.T("Agent.InputHint") };
        FullTextToolTips.SetEnabled(_transcript, false); FullTextToolTips.SetEnabled(_input, false);
        _status = Ui.Text("Agent.Privacy", 12, true); _status.TextWrapping = TextWrapping.Wrap;
        _send = Ui.AsyncButton(L10n.T("Agent.Send"), SendAsync, true);
        _cancel = Ui.Button(L10n.T("Common.Cancel"), () => _request?.Cancel());
        _clear = Ui.Button(L10n.T("Agent.Clear"), () => { if (_request is null) { _messages.Clear(); _transcript.Text = ""; } });
        _send.Name = "AgentSend"; _cancel.Name = "AgentCancel"; _clear.Name = "AgentClear";
        _input.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; await SendAsync(); } };
        var buttons = Ui.Actions(_clear, _cancel, _send); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Content = Ui.Stack(_transcript, _input, buttons, _status); Refresh();
    }
    private void Refresh()
    {
        var busy = _request is not null; _send.IsEnabled = _input.IsEnabled = _clear.IsEnabled = !busy && !_disposed; _cancel.IsEnabled = busy;
    }
    private void Append(string role, string text)
    {
        var next = (_transcript.Text ?? "") + (string.IsNullOrEmpty(_transcript.Text) ? "" : "\n\n") + role + "\n" + text;
        // 显示和上下文分别限长，不永久记录用户问题、模型回复或发送凭据。
        _transcript.Text = next.Length > 60_000 ? next[^60_000..] : next;
        _transcript.CaretIndex = _transcript.Text.Length;
    }
    private async Task SendAsync()
    {
        if (_disposed || _request is not null || string.IsNullOrWhiteSpace(_input.Text)) return;
        var config = _manager.ConfigurationValues(_plugin);
        if (string.IsNullOrWhiteSpace(config["baseUrl"]?.GetValue<string>()) || string.IsNullOrWhiteSpace(config["model"]?.GetValue<string>()))
        { _status.Text = L10n.T("Agent.ConfigureFirst"); return; }
        if (_messages.Count > 30 || _messages.Sum(m => m.Text.Length) > 65_000) _messages.Clear();
        var prior = _messages.Count; _declined.Clear();
        var text = _input.Text.Trim(); _input.Text = "";
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
                _messages.Add(new("assistant", reply.Text, reply.Calls));
                if (reply.Text.Length > 0) Append(_plugin.Name, reply.Text);
                if (reply.Calls.Length == 0) { _status.Text = L10n.T("Agent.Privacy"); return; }
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
            if (!_disposed) Append(L10n.T("Agent.Tool"), call.Operation + " · " + (result.Success ? L10n.T("Agent.Done") : L10n.T("Agent.ToolFailed")));
            var payload = new JsonObject { ["success"] = result.Success, ["data"] = AgentCommandPolicy.Sanitize(result.Data) };
            var json = payload.ToJsonString();
            return json.Length <= 18_000 ? json : """{"success":true,"truncated":true,"hint":"Narrow the query."}""";
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
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _request?.Cancel(); _messages.Clear(); _transcript.Text = ""; _lifetime.Dispose(); Refresh();
    }
}
