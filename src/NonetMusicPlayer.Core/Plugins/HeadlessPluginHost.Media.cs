using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Persistence;

namespace NonetMusicPlayer.Core.Plugins;

public sealed partial class HeadlessPluginHost
{
    private readonly Dictionary<string, ExtensionChannel> _extensions = [];
    public Func<PluginManifest, ExtensionHostRequest, CancellationToken, Task<JsonObject>>? HandleRequest { get; set; }
    // 只协商实际支持的数据/动作/音源能力，不向无窗口宿主宣称图形控件能力。
    private static readonly string[] HeadlessCapabilities = ["state.v1", "actions.v1", "commands.v1", "lyrics.v1", "services.v1", "ui.page-optional.v1", "runtime.declarative.v1", "runtime.supervised.v1", "media.search.v1", "media.artwork-batch.v1"];
    public IReadOnlyList<PluginManifest> MusicSearchSources => Installed.Where(p => p.Enabled && p.MusicSearch is not null && (p.Type == "provider" || p.ProvidedServices.Contains("media-source"))).ToArray();
    public void TrustManaged(PluginManifest plugin)
    {
        if (plugin.Runtime != "managed") throw new InvalidOperationException("Managed plugin required.");
        plugin.ManagedExecutionConsent = true; Save();
    }
    public void Reload(PluginManifest plugin) { StopExtension(plugin.Id); if (_clients.Remove(plugin.Id, out var client)) client.Dispose(); }
    private void StopExtension(string id) { if (_extensions.Remove(id, out var channel)) channel.Dispose(); }
    public async Task<JsonObject> ExtensionStateAsync(PluginManifest plugin, CancellationToken token)
        => (await (await ExtensionAsync(plugin, token)).SyncAsync(token)).State.DeepClone().AsObject();
    public async Task<JsonObject> InvokeExtensionAsync(PluginManifest plugin, string action, JsonObject values, CancellationToken token)
    {
        if (action.Length is 0 or > 200) throw new InvalidDataException("Invalid plugin action.");
        var frame = await (await ExtensionAsync(plugin, token)).InvokeAsync(new(action, values), token);
        return new JsonObject { ["state"] = frame.State.DeepClone(), ["requests"] = JsonSerializer.SerializeToNode(frame.Requests, CoreJson.Options) };
    }
    public async Task<MusicSearchPage> SearchMusicAsync(string id, string query, string cursor, CancellationToken token)
    {
        var plugin = MusicSearchSources.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Music search source unavailable.");
        if (query.Trim().Length is 0 or > 300 || cursor.Length > 512) throw new InvalidDataException("Invalid music search query.");
        var result = await MediaAsync(plugin, "music.search", new() { ["query"] = query.Trim(), ["cursor"] = cursor,
            ["limit"] = plugin.MusicSearch!.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), ["includeArtwork"] = plugin.MusicSearch.DeferredArtwork ? "false" : "true" }, token);
        var page = result.Deserialize(MusicSourceJson.Default.MusicSearchPage) ?? throw new InvalidDataException("Empty music search response.");
        MusicSearchContract.ValidatePage(page, plugin.MusicSearch.PageSize); return page;
    }
    public async Task<ProviderTrack?> DescribeMusicAsync(string id, string trackId, CancellationToken token)
    {
        var plugin = MusicSearchSources.FirstOrDefault(p => p.Id == id); if (plugin is null) return null;
        var result = (await MediaAsync(plugin, "music.describe", new() { ["trackId"] = trackId }, token)).Deserialize(MusicSourceJson.Default.ProviderTrack) ?? throw new InvalidDataException("Empty media metadata.");
        MusicSearchContract.ValidateTrack(result); if (result.Id != trackId) throw new InvalidDataException("Music identity changed."); return result;
    }
    private async Task<JsonElement> MediaAsync(PluginManifest plugin, string method, Dictionary<string, string> values, CancellationToken token)
    {
        if (!plugin.Enabled || !plugin.Permissions.Contains("network") || plugin.Type != "provider" && !plugin.ProvidedServices.Contains("media-source")) throw new InvalidOperationException("Enabled media source and network permission required.");
        if (plugin.Type != "extension") return await (await ClientAsync(plugin, token)).CallAsync(method, values, token);
        var payload = new JsonObject { ["method"] = method }; foreach (var pair in values) payload[pair.Key] = pair.Value;
        var frame = await (await ExtensionAsync(plugin, token)).InvokeAsync(new("service:media-source", payload), token);
        return JsonSerializer.SerializeToElement(frame.State["serviceResult"] ?? throw new InvalidDataException("Missing media source result."), CoreJson.Options);
    }
    private async Task<ExtensionChannel> ExtensionAsync(PluginManifest plugin, CancellationToken token)
    {
        if (!plugin.Enabled || plugin.Type != "extension") throw new InvalidOperationException("Enabled extension required.");
        if (_extensions.TryGetValue(plugin.Id, out var cached)) return cached;
        var channel = new ExtensionChannel(DirectoryFor(plugin), plugin, _sessionConfiguration.GetValueOrDefault(plugin.Id, plugin.Configuration), (request, cancellation) => HandleRequest?.Invoke(plugin, request, cancellation) ?? Task.FromResult(new JsonObject { ["success"] = false, ["reason"] = "Headless service unavailable." }), error =>
        { plugin.Enabled = false; AppLog.Error("Plugin:" + plugin.Id, "CLI 扩展已因普通异常停用", error); Save(); });
        try { await channel.StartAsync(token); _extensions[plugin.Id] = channel; return channel; }
        catch (Exception error) { channel.Dispose(); if (error is not (OperationCanceledException or PluginOperationException)) { plugin.Enabled = false; Save(); AppLog.Error("Plugin:" + plugin.Id, "CLI 扩展初始化失败，已停用", error); } throw; }
    }
    /// <summary>进程、托管 RPC 和托管扩展共用冻结的 v2 协议；托管运行不具备崩溃隔离。</summary>
    private sealed class ExtensionChannel : IDisposable
    {
        private readonly string _root, _configuration;
        private readonly PluginManifest _manifest;
        private readonly Action<Exception> _fault;
        private readonly Func<ExtensionHostRequest, CancellationToken, Task<JsonObject>> _request;
        private readonly HashSet<string> _handled = [];
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SemaphoreSlim _gate = new(1);
        private ProviderClient? _process;
        private ManagedPluginLoader? _managed;
        private INonetExtension? _extension;
        private INonetManagedRpcPlugin? _rpc;
        public ExtensionChannel(string root, PluginManifest manifest, string configuration, Func<ExtensionHostRequest, CancellationToken, Task<JsonObject>> request, Action<Exception> fault)
        { _root = root; _manifest = manifest; _configuration = configuration; _request = request; _fault = fault; }
        public async Task StartAsync(CancellationToken token)
        {
            if (_manifest.Runtime == "declarative")
            {
                var page = Path.Combine(_root, _manifest.PageEntry); PluginPathPolicy.RejectLinkedAncestors(page);
                using var input = File.OpenRead(page); _extension = new DeclarativeExtension(ExtensionContract.ReadPage(input));
            }
            else if (_manifest.Runtime == "managed")
            {
                if (!_manifest.ManagedExecutionConsent) throw new InvalidOperationException(Localization.LocalizationCatalog.Get("Extensions.ManagedWarning"));
                var entry = Path.Combine(_root, _manifest.EntryPoints[PluginPlatformPolicy.CurrentRid]);
                _managed = new(entry, _manifest.ExtensionClass, new PluginRuntimeContext(_lifetime.Token,
                    (level, message) => AppLog.Info("Plugin:" + _manifest.Id, AppLog.Redact(message)),
                    async work => { try { await work(_lifetime.Token); } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { } catch (Exception error) { _fault(error); _lifetime.Cancel(); } }));
                _extension = _managed.Instance as INonetExtension; _rpc = _managed.Instance as INonetManagedRpcPlugin;
                if (_extension is null && _rpc is null) throw new InvalidDataException("Extension interface required.");
            }
            else _process = new ProviderClient(_root, _manifest, _fault);
            // 扩展数据放在包目录之外，同 ID 更新/卸载保留配置与状态。
            var storage = Path.Combine(Path.GetDirectoryName(_root)!, "Storage", _manifest.Id); PluginPathPolicy.RejectLinkedAncestors(storage); Directory.CreateDirectory(storage);
            if (_extension is not null) Validate(await _extension.InitializeAsync(new(2, _configuration, storage, HeadlessCapabilities), token));
            else
            {
                var reply = await CallAsync("initialize", new() { ["contractVersion"] = "2", ["configuration"] = _configuration, ["storageDirectory"] = storage,
                    ["capabilities"] = JsonSerializer.Serialize(HeadlessCapabilities, CoreJson.Options) }, token);
                if (!reply.TryGetProperty("contractVersion", out var version) || version.GetInt32() != 2) throw new InvalidDataException("Extension handshake failed.");
            }
        }
        private static ExtensionFrame Validate(ExtensionFrame frame) { ExtensionContract.ValidateFrame(frame); return frame; }
        public async Task<ExtensionFrame> SyncAsync(CancellationToken token)
        {
            await _gate.WaitAsync(token); try { return await CompleteRequestsAsync(_extension is not null ? Validate(await _extension.SyncAsync(token))
                : Validate((await CallAsync("extension.sync", [], token)).Deserialize(ExtensionJson.Default.ExtensionFrame) ?? throw new InvalidDataException("Invalid extension state.")), token); }
            finally { _gate.Release(); }
        }
        public async Task<ExtensionFrame> InvokeAsync(ExtensionInvocation invocation, CancellationToken token)
        {
            await _gate.WaitAsync(token);
            try { return await CompleteRequestsAsync(_extension is not null ? Validate(await _extension.InvokeAsync(invocation, token))
                : Validate((await CallAsync("extension.invoke", new() { ["invocation"] = JsonSerializer.Serialize(invocation, ExtensionJson.Default.ExtensionInvocation) }, token)).Deserialize(ExtensionJson.Default.ExtensionFrame) ?? throw new InvalidDataException("Invalid extension response.")), token); }
            catch (Exception error) when (error is PluginOperationException or ProviderRemoteException) { throw; }
            catch (Exception error) when (error is not OperationCanceledException) { _fault(error); _lifetime.Cancel(); throw; }
            finally { _gate.Release(); }
        }
        private async Task<JsonElement> CallAsync(string method, Dictionary<string, string> values, CancellationToken token)
            => _rpc is not null ? await _rpc.CallAsync(method, values, token) : await _process!.CallAsync(method, values, token);
        private async Task<ExtensionFrame> CompleteRequestsAsync(ExtensionFrame frame, CancellationToken token)
        {
            for (var round = 0; frame.Requests.Count > 0 && round < 16; round++)
            {
                var request = frame.Requests.FirstOrDefault(r => !_handled.Contains(r.Id)); if (request is null) break;
                if (_handled.Count >= 4096) throw new InvalidDataException("Too many extension requests."); _handled.Add(request.Id);
                JsonObject result;
                try { result = await _request(request, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { result = new() { ["success"] = false, ["reason"] = "Headless host rejected this service or permission." }; }
                frame = _extension is not null ? Validate(await _extension.CompleteAsync(request.Id, result, token))
                    : Validate((await CallAsync("extension.complete", new() { ["id"] = request.Id, ["result"] = result.ToJsonString() }, token)).Deserialize(ExtensionJson.Default.ExtensionFrame) ?? throw new InvalidDataException("Invalid completion state."));
            }
            return frame;
        }
        public void Dispose()
        {
            _lifetime.Cancel(); _process?.NotifyLifecycle(_manifest, "lifecycle.shutdown"); _process?.Dispose();
            if (_managed is not null) _managed.DisposeAsync().AsTask().GetAwaiter().GetResult(); else if (_extension is not null) _extension.DisposeAsync().AsTask().GetAwaiter().GetResult(); _lifetime.Dispose();
        }
    }
}
