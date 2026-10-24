using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

/// <summary>仅供启动门控与页面重排验收的通用夹具，不包含私人插件代码。</summary>
public sealed class StartupStyleExtension : INonetExtension
{
    private readonly BaselineExtension _baseline = new();
    private ExtensionFrame _frame = new();
    public async ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization value, CancellationToken token)
    {
        await Task.Delay(120, token);
        _frame = await _baseline.InitializeAsync(value, token);
        _frame.Requests.Add(new("startup", "styles", new() { ["operation"] = "apply", ["layers"] = new JsonArray(new JsonObject { ["id"] = "fixture.startup", ["name"] = "Startup", ["resources"] = new JsonObject { ["AccentBrush"] = "#4268CD" }, ["rules"] = new JsonArray(new JsonObject { ["type"] = "Button", ["classes"] = new JsonArray("player-control"), ["setters"] = new JsonObject { ["cornerRadius"] = "13" } }) }) }));
        return _frame;
    }
    public ValueTask<ExtensionFrame> CompleteAsync(string id, JsonObject result, CancellationToken token) { _frame.Requests.Clear(); return ValueTask.FromResult(_frame); }
    public ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation value, CancellationToken token) => _baseline.InvokeAsync(value, token);
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken token) => _baseline.SyncAsync(token);
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken token) => _baseline.EventAsync(value, token);
    public ValueTask DisposeAsync() => _baseline.DisposeAsync();
}
