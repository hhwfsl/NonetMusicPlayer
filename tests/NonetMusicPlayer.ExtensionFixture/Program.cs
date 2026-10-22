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
        case "fixture.delay":
            File.WriteAllText(rpc.Params["started"], "started");
            await Task.Delay(int.Parse(rpc.Params["milliseconds"]));
            result = new JsonObject { ["reply"] = "delayed" }; break;
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
    private readonly ExtensionFrame _frame = new() { State = new() { ["count"] = 0, ["lastEvent"] = "", ["text"] = "selectable fixture", ["columns"] = "180,*", ["items"] = new JsonArray((JsonNode)new JsonObject { ["id"] = "first", ["title"] = "First item" }) } };
    private string _storage = "";
    public ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization initialization, CancellationToken cancellationToken) { _storage = initialization.StorageDirectory; _frame.State["image"] = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/ADSY2/8FUgKnWOt33AAAAABJRU5ErkJggg=="; return ValueTask.FromResult(_frame); }
    public ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation invocation, CancellationToken cancellationToken)
    {
        if (invocation.Action == "service:media-source")
        {
            if (invocation.Values["query"]?.GetValue<string>() == "slow" && string.IsNullOrEmpty(invocation.Values["cursor"]?.GetValue<string>())) return new(DelayedSearchAsync(invocation,cancellationToken));
            var method=invocation.Values["method"]?.GetValue<string>();
            if (method=="music.artwork" || method=="music.search" && invocation.Values["query"]?.GetValue<string>() is "paged" or "slow")
            {
                var cursor=invocation.Values["cursor"]?.GetValue<string>()??"";var offset=cursor.Length==0?0:int.Parse(cursor);
                var ids=method=="music.artwork" ? JsonNode.Parse(invocation.Values["trackIds"]!.GetValue<string>())!.AsArray().Select(n=>n!.GetValue<string>()).ToArray() : Enumerable.Range(offset,Math.Min(30,6000-offset)).Select(i=>"page-"+i).ToArray();
                var page=new MusicSearchPage { Tracks=ids.Select(id=>new ProviderTrack { Id=id,Title="Paged "+id,Artist="Fixture",DurationSeconds=180,CoverBase64=method=="music.artwork"?"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/ADSY2/8FUgKnWOt33AAAAABJRU5ErkJggg==":null }).ToList(),NextCursor=method=="music.search"&&offset+ids.Length<6000?(offset+ids.Length).ToString():null,Total=6000 };
                _frame.State["serviceResult"]=JsonSerializer.SerializeToNode(page,MusicSourceJson.Default.MusicSearchPage);_frame.Revision++;return ValueTask.FromResult(_frame);
            }
            var track = new ProviderTrack { Id = "remote-1", Title = "Cloud fixture", Artist = "Fixture artist", Album = "Fixture album", DurationSeconds = 180, CoverBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/ADSY2/8FUgKnWOt33AAAAABJRU5ErkJggg==" };
            _frame.State["serviceResult"] = invocation.Values["method"]?.GetValue<string>() switch
            {
                "music.search" => JsonSerializer.SerializeToNode(new MusicSearchPage { Tracks = [track], NextCursor = "next", Total = 2 }, MusicSourceJson.Default.MusicSearchPage),
                "music.describe" => JsonSerializer.SerializeToNode(track, MusicSourceJson.Default.ProviderTrack),
                "catalog.list" => JsonSerializer.SerializeToNode(new ProviderCatalog { Tracks = [track] }, MusicSourceJson.Default.ProviderCatalog),
                _ => new JsonObject()
            };
            _frame.Revision++; return ValueTask.FromResult(_frame);
        }
        _frame.Revision++; _frame.State["count"] = (_frame.State["count"]?.GetValue<int>() ?? 0) + 1;
        if (invocation.Action == "collapse") _frame.State["columns"] = "0,*";
        if (invocation.Action == "expand") _frame.State["columns"] = "180,*";
        if (invocation.Action == "clear-image") _frame.State["image"] = "";
        if (invocation.Action == "invalid-image") _frame.State["image"] = "not an image";
        _frame.State["parameter"] = invocation.Values["parameter"]?.DeepClone();
        return ValueTask.FromResult(_frame);
    }
    private async Task<ExtensionFrame> DelayedSearchAsync(ExtensionInvocation invocation,CancellationToken token)
    {
        await Task.Delay(400,token);var values=(JsonObject)invocation.Values.DeepClone();values["query"]="paged";
        return await InvokeAsync(invocation with { Values=values },token);
    }
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_frame);
    public ValueTask<ExtensionFrame> CompleteAsync(string requestId, JsonObject result, CancellationToken cancellationToken) => ValueTask.FromResult(_frame);
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken cancellationToken) { _frame.State["lastEvent"] = value.Name; _frame.Revision++; return ValueTask.FromResult(_frame); }
    public ValueTask DisposeAsync() { if (_storage.Length > 0) File.WriteAllText(Path.Combine(_storage, "disposed.txt"), "disposed"); return ValueTask.CompletedTask; }
}
