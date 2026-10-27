using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

/// <summary>有保存会话、媒体封面和慢析构的通用夹具，不包含私人音源的代码或凭据。</summary>
public sealed class SourceLifecycleExtension : INonetExtension
{
    private readonly ExtensionFrame _frame = new(); private string _storage = "";
    public ValueTask<ExtensionFrame> InitializeAsync(ExtensionInitialization value, CancellationToken token)
    {
        _storage = value.StorageDirectory; Directory.CreateDirectory(_storage);
        var path = Path.Combine(_storage, "fixture-session"); if (!File.Exists(path)) File.WriteAllText(path, "fixture-authorized");
        File.WriteAllText(Path.Combine(_storage, "start-" + Guid.NewGuid().ToString("N")), "started");
        _frame.State["account"] = File.ReadAllText(path); return ValueTask.FromResult(_frame);
    }
    public async ValueTask<ExtensionFrame> InvokeAsync(ExtensionInvocation value, CancellationToken token)
    {
        _frame.Requests.Clear(); _frame.State.Remove("serviceResult");
        if (value.Action == "configure") _frame.Requests.Add(new(Guid.NewGuid().ToString("N"), "config", new()));
        if (value.Action == "service:media-source")
        {
            var method = value.Values["method"]?.GetValue<string>();
            if (method == "music.artwork")
            {
                File.WriteAllText(Path.Combine(_storage, "metadata-started"), "started");
                while (!File.Exists(Path.Combine(_storage, "allow-metadata"))) await Task.Delay(10, token);
                _frame.State["serviceResult"] = JsonSerializer.SerializeToNode(new MusicSearchPage { Tracks = [new() { Id = "remote-bg", Title = "Background enriched", Artist = "Background artist", DurationSeconds = 210, CoverBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/ADSY2/8FUgKnWOt33AAAAABJRU5ErkJggg==" }] }, MusicSourceJson.Default.MusicSearchPage);
            }
            else
            {
                var background = value.Values["catalogId"]?.GetValue<string>() == "background";
                _frame.State["serviceResult"] = JsonSerializer.SerializeToNode(new ProviderCatalog { Tracks = [new() { Id = background ? "remote-bg" : "remote-1", Title = background ? "Pending metadata" : "Imported title", Artist = "Imported artist", Album = "Imported album", DurationSeconds = 180, CoverBase64 = value.Values["includeArtwork"]?.GetValue<string>() == "false" ? null : "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAEElEQVR4AQEFAPr/ADSY2/8FUgKnWOt33AAAAABJRU5ErkJggg==" }] }, MusicSourceJson.Default.ProviderCatalog);
            }
        }
        ++_frame.Revision; return _frame;
    }
    public ValueTask<ExtensionFrame> SyncAsync(CancellationToken token) => ValueTask.FromResult(_frame);
    public ValueTask<ExtensionFrame> CompleteAsync(string id, JsonObject result, CancellationToken token) { _frame.Requests.Clear(); return ValueTask.FromResult(_frame); }
    public ValueTask<ExtensionFrame> EventAsync(ExtensionEvent value, CancellationToken token) => ValueTask.FromResult(_frame);
    public ValueTask DisposeAsync() { Thread.Sleep(1500); File.WriteAllText(Path.Combine(_storage, "cleanup-" + Guid.NewGuid().ToString("N")), "done"); return ValueTask.CompletedTask; }
}
