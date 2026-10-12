using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

/// <summary>独立生成的故障夹具，不依赖任何私人插件。</summary>
public sealed class FaultExtension : INonetExtension, INonetRuntimeAware
{
    private PluginRuntimeContext? _runtime;
    private readonly ExtensionFrame _frame = new();
    public void AttachRuntime(PluginRuntimeContext context) => _runtime = context;
    public ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization value, CancellationToken token)
    {
        if (value.Configuration.Contains("failInit")) throw new InvalidOperationException("Fixture initialization failed.");
        _runtime?.Log("Fixture ready."); return ValueTask.FromResult(_frame);
    }
    public ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation value, CancellationToken token)
    {
        if (value.Action == "throw") throw new InvalidOperationException("Fixture action failed.");
        if (value.Action == "handled") throw new PluginOperationException("Recoverable operation.");
        if (value.Action == "background") _ = _runtime!.RunAsync(async cancellation => { await Task.Delay(50, cancellation); throw new InvalidOperationException("Fixture background failed."); });
        return ValueTask.FromResult(_frame);
    }
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken token) => throw new InvalidOperationException("Fixture event failed.");
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken token) => ValueTask.FromResult(_frame);
    public ValueTask<ExtensionFrame> CompleteAsync(string id, JsonObject result, CancellationToken token) => ValueTask.FromResult(_frame);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>冻结 v1 方法形态的可选托管入口。</summary>
public sealed class ManagedRpcFixture : INonetManagedRpcPlugin
{
    public ValueTask<JsonElement> CallAsync(string method, IReadOnlyDictionary<string, string> parameters, CancellationToken token)
    {
        if (method is "lyrics.search" or "catalog.list") throw new InvalidOperationException("Fixture RPC failed.");
        using var json = JsonDocument.Parse("{\"contractVersion\":1}"); return ValueTask.FromResult(json.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
