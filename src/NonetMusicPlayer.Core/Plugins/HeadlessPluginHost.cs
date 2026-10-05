using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Streaming;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>CLI 的插件生命周期；复用桌面契约与包审查，不创建图形页面或桌宠。</summary>
public sealed class HeadlessPluginHost : IDisposable
{
    private readonly string _root, _index;
    private readonly Dictionary<string, ProviderClient> _clients = [];
    private readonly Dictionary<string, string> _sessionConfiguration = [];
    public List<PluginManifest> Installed { get; }
    public HeadlessPluginHost(string root)
    {
        _root = Path.GetFullPath(root); PluginPathPolicy.RejectLinkedAncestors(_root); Directory.CreateDirectory(_root); _index = Path.Combine(_root, "installed.json");
        Installed = File.Exists(_index) ? JsonSerializer.Deserialize<List<PluginManifest>>(File.ReadAllText(_index), CoreJson.Options) ?? [] : [];
        foreach (var plugin in Installed) plugin.Validate();
    }
    public PluginManifest Find(string id) => Installed.FirstOrDefault(p => p.Id == id) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PluginNotFound"));
    public PluginManifest Install(string package)
    {
        var plugin = PluginPackageInspector.Inspect(package); var target = DirectoryFor(plugin);
        if (Installed.Any(p => p.Id == plugin.Id) || Directory.Exists(target)) throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PluginAlreadyInstalled"));
        var stage = Path.Combine(_root, ".stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try { ZipFile.ExtractToDirectory(package, stage); Directory.Move(stage, target); Installed.Add(plugin); Save(); return plugin; }
        catch { Installed.Remove(plugin); if (Directory.Exists(target)) DeleteOwnedDirectory(target); throw; }
        finally { if (Directory.Exists(stage)) DeleteOwnedDirectory(stage); }
    }
    public void SetEnabled(PluginManifest plugin, bool enabled)
    {
        plugin.Validate(); DirectoryFor(plugin);
        if (enabled && plugin.Type is "ui" or "widget" or "theme" or "lyrics") throw new PlatformNotSupportedException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PluginUiUnsupported"));
        if (!enabled && _clients.Remove(plugin.Id, out var client)) { client.NotifyLifecycle(plugin, "lifecycle.disable"); client.Dispose(); }
        var previous = plugin.Enabled; plugin.Enabled = enabled;
        try { Save(); } catch { plugin.Enabled = previous; throw; }
    }
    public void Uninstall(PluginManifest plugin, bool deleteFiles = false)
    {
        SetEnabled(plugin, false); var directory = DirectoryFor(plugin);
        if (plugin.LifecycleMethods.Contains("lifecycle.uninstall"))
            try { using var cleanup = new ProviderClient(directory, plugin); cleanup.NotifyLifecycle(plugin, "lifecycle.uninstall"); }
            catch (Exception error) { Diagnostics.AppLog.Warning("Plugins", "Plugin uninstall callback failed", error); }
        if (Directory.Exists(directory)) { if (deleteFiles) PluginPathPolicy.AfterProcessExit(() => DeleteOwnedDirectory(directory)); else { var retained = Path.Combine(Path.GetDirectoryName(directory)!, "Retained", plugin.Id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.GetDirectoryName(retained)!); PluginPathPolicy.AfterProcessExit(() => Directory.Move(directory, retained)); } }
        Installed.Remove(plugin); _sessionConfiguration.Remove(plugin.Id); Save();
    }
    public JsonObject Schema(PluginManifest plugin)
    {
        var directory = DirectoryFor(plugin);
        var file = new[] { PluginConfigSchema.FileName, "plugin_config_schema" }.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        if (file is null) return new JsonObject();
        using var input = File.OpenRead(file); return PluginConfigSchema.Read(input);
    }
    public void Configure(PluginManifest plugin, string json)
    {
        var schema = Schema(plugin); var values = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ConfigObject"));
        PluginConfigSchema.Validate(schema, values);
        var old = plugin.Configuration; var previousSession = _sessionConfiguration.GetValueOrDefault(plugin.Id);
        var persisted = values.DeepClone().AsObject(); Scrub(persisted); PluginConfigSchema.RemoveSensitiveFields(schema, persisted);
        plugin.Configuration = persisted.ToJsonString(); _sessionConfiguration[plugin.Id] = json;
        try { Save(); }
        catch { plugin.Configuration = old; if (previousSession is null) _sessionConfiguration.Remove(plugin.Id); else _sessionConfiguration[plugin.Id] = previousSession; throw; }
        if (_clients.Remove(plugin.Id, out var client)) client.Dispose();
    }
    private static void Scrub(JsonNode node)
    {
        if (node is JsonObject obj)
        foreach (var key in obj.Select(p => p.Key).ToArray())
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(key, "token|secret|password|authorization|cookie|api.?key", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) obj.Remove(key);
            else if (obj[key] is { } child) Scrub(child);
        }
        else if (node is JsonArray array) foreach (var child in array.OfType<JsonNode>()) Scrub(child);
    }
    private string DirectoryFor(PluginManifest plugin)
    {
        plugin.Validate(); var path = Path.GetFullPath(Path.Combine(_root, plugin.Id));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.PathEscape"));
        PluginPathPolicy.RejectLinkedAncestors(path); return path;
    }
    private void DeleteOwnedDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ExternalDeleteDenied"));
        PluginPathPolicy.RejectLinkedAncestors(full); Directory.Delete(full, true);
    }
    private void Save() => AtomicFile.Write(_index, JsonSerializer.Serialize(Installed, CoreJson.Options), true);
    private async Task<ProviderClient> ClientAsync(PluginManifest plugin, CancellationToken cancellationToken)
    {
        if (!plugin.Enabled || plugin.Type != "provider") throw new InvalidOperationException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.EnableProvider"));
        if (_clients.TryGetValue(plugin.Id, out var existing)) return existing;
        var client = new ProviderClient(DirectoryFor(plugin), plugin);
        try
        {
            var response = await client.CallAsync("initialize", new() { ["contractVersion"] = "1", ["hostVersion"] = "0.3.0", ["configuration"] = _sessionConfiguration.GetValueOrDefault(plugin.Id, plugin.Configuration) }, cancellationToken);
            if (!response.TryGetProperty("contractVersion", out var version) || version.GetInt32() != 1) throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.HandshakeFailed"));
            _clients.Add(plugin.Id, client); return client;
        }
        catch { client.Dispose(); throw; }
    }
    public async Task<IReadOnlyList<ProviderTrack>> CatalogAsync(PluginManifest plugin, CancellationToken cancellationToken)
    {
        var client = await ClientAsync(plugin, cancellationToken);
        try { return await client.ReadCatalogAsync(cancellationToken); }
        catch { _clients.Remove(plugin.Id); client.Dispose(); throw; }
    }
    public async Task<string> ResolveAsync(string pluginId, string trackId, CancellationToken cancellationToken)
    {
        var client = await ClientAsync(Find(pluginId), cancellationToken);
        if (!client.CatalogInitialized) await CatalogAsync(Find(pluginId), cancellationToken);
        var result = await CallProviderAsync(pluginId, client, "playback.resolve", new() { ["trackId"] = trackId }, cancellationToken);
        var source = result.Deserialize<PlaybackSource>(CoreJson.Options) ?? throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.NoProviderAudio"));
        if (source.Kind != "loopback-http") throw new InvalidDataException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.LoopbackOnly"));
        LoopbackRangeStream.Validate(new Uri(source.Url)); return source.Url;
    }
    public void Dispose() { foreach (var pair in _clients) { if (Installed.FirstOrDefault(p => p.Id == pair.Key) is { } manifest) pair.Value.NotifyLifecycle(manifest, "lifecycle.shutdown"); pair.Value.Dispose(); } _clients.Clear(); }

    /// <summary>失败后丢弃失效进程，下次请求可以重新握手，而非永久复用已释放客户端。</summary>
    private async Task<JsonElement> CallProviderAsync(string id, ProviderClient client, string method, Dictionary<string, string> parameters, CancellationToken token)
    {
        try { return await client.CallAsync(method, parameters, token); }
        catch { _clients.Remove(id); client.Dispose(); throw; }
    }
}
