using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>纯数据扩展无需运行时、反射或外部进程；所有副作用仍通过宿主权限服务。</summary>
public sealed class DeclarativeExtension(ExtensionPage page) : INonetExtension
{
    private readonly ExtensionFrame _frame = new() { State = (JsonObject)page.InitialState.DeepClone() };
    public ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization initialization, CancellationToken cancellationToken) => SyncAsync(cancellationToken);
    public ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!page.Actions.TryGetValue(invocation.Action, out var action)) throw new InvalidDataException("Unknown declarative action.");
        _frame.Requests = [new(Guid.NewGuid().ToString("N"), action.Service, (JsonObject)action.Arguments.DeepClone())];
        _frame.Revision++; return ValueTask.FromResult(_frame);
    }
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(_frame); }
    public ValueTask<ExtensionFrame> CompleteAsync(string requestId, JsonObject result, CancellationToken cancellationToken)
    {
        _frame.Requests.RemoveAll(r => r.Id == requestId); _frame.State["result"] = result.DeepClone(); _frame.Revision++;
        return ValueTask.FromResult(_frame);
    }
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken cancellationToken) => SyncAsync(cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
