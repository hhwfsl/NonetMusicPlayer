using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

var extension = new BaselineExtension();
while (Console.ReadLine() is { } line)
{
    var rpc = JsonSerializer.Deserialize(line, AgentPluginJson.Default.RpcRequest)!;
    JsonNode result;
    switch (rpc.Method)
    {
        case "initialize":
            await extension.InitializeAsync(new(2, "{}", rpc.Params.GetValueOrDefault("storageDirectory") ?? "", ExtensionContract.Capabilities), default);
            result = new JsonObject { ["contractVersion"] = 2, ["capabilities"] = new JsonArray(ExtensionContract.Capabilities.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()) }; break;
        case "extension.invoke": result = JsonSerializer.SerializeToNode(await extension.InvokeAsync(JsonSerializer.Deserialize(rpc.Params["invocation"], ExtensionJson.Default.ExtensionInvocation)!, default), ExtensionJson.Default.ExtensionFrame)!; break;
        case "extension.event": result = JsonSerializer.SerializeToNode(await extension.EventAsync(JsonSerializer.Deserialize(rpc.Params["event"], ExtensionJson.Default.ExtensionEvent)!, default), ExtensionJson.Default.ExtensionFrame)!; break;
        case "extension.complete": result = JsonSerializer.SerializeToNode(await extension.CompleteAsync(rpc.Params["id"], JsonNode.Parse(rpc.Params["result"])!.AsObject(), default), ExtensionJson.Default.ExtensionFrame)!; break;
        case "lifecycle.disable": case "lifecycle.shutdown": case "lifecycle.uninstall":
            await extension.DisposeAsync(); result = new JsonObject { ["ok"] = true }; break;
        default: result = JsonSerializer.SerializeToNode(await extension.SyncAsync(default), ExtensionJson.Default.ExtensionFrame)!; break;
    }
    Console.WriteLine(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = rpc.Id, ["result"] = result }.ToJsonString());
    if (rpc.Method.StartsWith("lifecycle.")) break;
}

/// <summary>无业务耦合的计数器夹具，用于进程/托管模式和 v2 冻结基线回归。</summary>
public sealed class BaselineExtension : INonetExtension
{
    private readonly ExtensionFrame _frame = new() { State = new() { ["count"] = 0, ["lastEvent"] = "", ["text"] = "selectable fixture" } };
    private string _storage = "";
    public ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization initialization, CancellationToken cancellationToken) { _storage = initialization.StorageDirectory; return ValueTask.FromResult(_frame); }
    public ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation invocation, CancellationToken cancellationToken) { _frame.Revision++; _frame.State["count"] = (_frame.State["count"]?.GetValue<int>() ?? 0) + 1; return ValueTask.FromResult(_frame); }
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_frame);
    public ValueTask<ExtensionFrame> CompleteAsync(string requestId, JsonObject result, CancellationToken cancellationToken) => ValueTask.FromResult(_frame);
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken cancellationToken) { _frame.State["lastEvent"] = value.Name; _frame.Revision++; return ValueTask.FromResult(_frame); }
    public ValueTask DisposeAsync() { if (_storage.Length > 0) File.WriteAllText(Path.Combine(_storage, "disposed.txt"), "disposed"); return ValueTask.CompletedTask; }
}
