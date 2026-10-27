using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

/// <summary>仅用于通用转换协议检查的合成封装，不包含任何音乐平台算法或真实歌曲。</summary>
public sealed class FileTransformFixture : INonetExtension
{
    public ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization initialization, CancellationToken token) => ValueTask.FromResult(new ExtensionFrame());
    public async ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation invocation, CancellationToken token)
    {
        var source = invocation.Values["inputPath"]!.GetValue<string>();
        if (Path.GetFileName(source).StartsWith("bad", StringComparison.Ordinal)) return new() { State = new() { ["serviceResult"] = new JsonObject { ["success"] = false, ["message"] = "Synthetic rejection" } } };
        var target = source + ".wav";
        using var input = File.OpenRead(source); input.Position = 5;
        using (var output = File.Create(target)) await input.CopyToAsync(output, token);
        return new() { State = new() { ["serviceResult"] = new JsonObject { ["success"] = true, ["outputPath"] = target } } };
    }
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken token) => ValueTask.FromResult(new ExtensionFrame());
    public ValueTask<ExtensionFrame> CompleteAsync(string id, JsonObject result, CancellationToken token) => ValueTask.FromResult(new ExtensionFrame());
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken token) => ValueTask.FromResult(new ExtensionFrame());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
