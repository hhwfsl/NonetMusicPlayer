using System.IO.Compression;
using System.Text.Json;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Core.Commands;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager : IDisposable
{
    private readonly AppStorage _storage;
    private readonly Dictionary<string, ProviderClient> _clients = [];
    private readonly Dictionary<string, string> _sessionConfiguration = [];
    public List<PluginManifest> Installed { get; private set; } = [];
    public string? RecoveryMessage { get; private set; }
    public event Action<string>? UiPluginUnavailable;
    public event EventHandler<CommandResult>? OperationCompleted;
    public event Action<PluginManifest, string>? LifecycleChanged;
    private string IndexPath => Path.Combine(_storage.PluginsFolder, "installed.json");
    public PluginManager(AppStorage storage)
    {
        _storage = storage;
        try
        {
            if (File.Exists(IndexPath)) Installed = JsonSerializer.Deserialize<List<PluginManifest>>(File.ReadAllText(IndexPath), AppStorage.Json) ?? [];
            Installed = Installed.OfType<PluginManifest>().ToList();
            foreach (var manifest in Installed) manifest.Validate();
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidDataException) { Installed = []; RecoveryMessage = L10n.T("Plugins.ThePluginIndexIsDamagedPluginsWereNotStarted"); }
    }
    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\\') || path.Split('/').Any(x => x is ".." or "." or "") || path.Contains('\0')) throw new InvalidDataException("插件包含不安全的相对路径。");
    }
    public static PluginManifest Inspect(string package) => PluginPackageInspector.Inspect(package);
    public PluginManifest Install(string package)
    {
        RequireWritable();
        var manifest = Inspect(package); var target = Path.Combine(_storage.PluginsFolder, manifest.Id);
        if (Directory.Exists(target)) throw new InvalidOperationException("该插件已经安装。请先卸载旧版再安装新版。配置会随卸载移除。");
        var stage = Path.Combine(_storage.PluginsFolder, ".stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            ZipFile.ExtractToDirectory(package, stage); Directory.Move(stage, target); Installed.Add(manifest); Save(); OperationCompleted?.Invoke(this, CommandResults.Completed("plugins.install")); return manifest;
        }
        catch { Installed.Remove(manifest); if (Directory.Exists(target)) Directory.Delete(target, true); throw; }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public void SetEnabled(PluginManifest manifest, bool enabled)
    {
        RequireWritable();
        RequireInstalled(manifest);
        manifest.Validate();
        if (enabled && manifest.Type == "ui") ReadUiPage(manifest);
        if (!enabled && _clients.Remove(manifest.Id, out var client)) { client.NotifyLifecycle(manifest, "lifecycle.disable"); client.Dispose(); }
        var previous = manifest.Enabled; manifest.Enabled = enabled;
        try { Save(); }
        catch
        {
            manifest.Enabled = previous;
            try { AppStorage.AtomicWrite(IndexPath, JsonSerializer.Serialize(Installed, AppStorage.Json), true); _storage.SyncPendingResources(); }
            catch (Exception rollbackError) { AppLog.Warning("Plugins", "插件状态回滚未完全保存", rollbackError); }
            throw;
        }
        finally { if (!manifest.Enabled && manifest.Type is "ui" or "widget") NotifyUiUnavailable(manifest.Id); RefreshUiRuntime(manifest); }
        OperationCompleted?.Invoke(this, CommandResults.Completed(enabled ? "plugins.enable" : "plugins.disable"));
        PublishLifecycle(manifest, enabled ? "enabled" : "disabled");
    }
    public PluginPageDefinition LoadPage(PluginManifest manifest)
    {
        RequireInstalled(manifest);
        if (manifest.Type is not ("ui" or "widget") || !manifest.Enabled) throw new InvalidOperationException(L10n.T("Plugins.EnableThisPagePluginFirst"));
        if (manifest.Type == "widget") return new(manifest.Name, "", manifest.Widgets.Select(w => new PluginPageWidget("text", w.Title, w.Text, null)).ToArray());
        return ReadUiPage(manifest);
    }
    private PluginPageDefinition ReadUiPage(PluginManifest manifest)
    {
        manifest.Validate();
        var root = Path.GetFullPath(Path.Combine(_storage.PluginsFolder, manifest.Id));
        var file = Path.GetFullPath(Path.Combine(root, manifest.PageEntry));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!file.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new InvalidDataException("UI 页面路径超出插件目录。");
        var path = root;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("UI 插件目录不允许链接。");
        foreach (var segment in manifest.PageEntry.Split('/'))
        {
            path = Path.Combine(path, segment);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("UI 页面路径不允许链接。");
        }
        if (new FileInfo(file).Length > 256 * 1024) throw new InvalidDataException("插件页面过大。");
        var pageNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { MaxDepth = 24 }) ?? throw new InvalidDataException("插件页面为空。");
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(PluginConfigSchema.Substitute(pageNode, ConfigurationValues(manifest)).ToJsonString()));
        return PluginPageContract.Read(stream, manifest.Permissions);
    }
    private void RequireInstalled(PluginManifest manifest)
    {
        if (!Installed.Contains(manifest)) throw new InvalidOperationException("插件已卸载或不属于当前安装列表。");
    }
    private void RequireWritable()
    {
        if (_storage.IsMigrating) throw new InvalidOperationException("正在复制数据目录，请稍后再修改插件。");
    }
    private void NotifyUiUnavailable(string id)
    {
        if (UiPluginUnavailable is null) return;
        foreach (Action<string> subscriber in UiPluginUnavailable.GetInvocationList())
            try { subscriber(id); } catch (Exception error) { AppLog.Warning("PluginUI", "插件页面释放失败", error); }
    }
    public void Configure(PluginManifest manifest, string json)
    {
        RequireWritable();
        RequireInstalled(manifest);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("配置必须是 JSON 对象。");
        var schema = ReadConfigurationSchema(manifest);
        PluginConfigSchema.Validate(schema, System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject());
        var previous = manifest.Configuration; var priorSession = _sessionConfiguration.GetValueOrDefault(manifest.Id);
        _sessionConfiguration[manifest.Id] = json;
        // 敏感配置仅保留在本次会话中；递归检查嵌套字段，不能只检查顶层名称。
        var persisted = Scrub(document.RootElement).AsObject(); PluginConfigSchema.RemoveSensitiveFields(schema, persisted); manifest.Configuration = persisted.ToJsonString();
        try { if (manifest.Enabled && manifest.Type == "ui") ReadUiPage(manifest); Save(); }
        catch { manifest.Configuration = previous; if (priorSession is null) _sessionConfiguration.Remove(manifest.Id); else _sessionConfiguration[manifest.Id] = priorSession; throw; }
        if (_clients.Remove(manifest.Id, out var client)) client.Dispose();
        if (manifest.Type == "ui") { NotifyUiUnavailable(manifest.Id); if (_pets.Remove(manifest.Id, out var pet)) pet.Close(); RefreshUiRuntime(manifest); }
        OperationCompleted?.Invoke(this, CommandResults.Completed("plugins.config"));
    }
    private static System.Text.Json.Nodes.JsonNode Scrub(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var node = new System.Text.Json.Nodes.JsonObject();
            foreach (var property in value.EnumerateObject())
                if (!System.Text.RegularExpressions.Regex.IsMatch(property.Name, "token|password|secret|authorization|cookie|credential|api.?key", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) node[property.Name] = Scrub(property.Value);
            return node;
        }
        if (value.ValueKind == JsonValueKind.Array) { var node = new System.Text.Json.Nodes.JsonArray(); foreach (var item in value.EnumerateArray()) node.Add(Scrub(item)); return node; }
        return System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText())!;
    }
    public void Uninstall(PluginManifest manifest, bool deleteFiles = false)
    {
        SetEnabled(manifest, false); _sessionConfiguration.Remove(manifest.Id);
        var directory = Path.Combine(_storage.PluginsFolder, manifest.Id);
        // 先验证插件 ID 再解析所属目录，不接受调用方传入的任意目录。
        manifest.Validate();
        _storage.ArchivePendingPluginRemoval(manifest.Id);
        if (manifest.Type == "provider" && manifest.LifecycleMethods.Contains("lifecycle.uninstall"))
            try { using var cleanup = new ProviderClient(directory, manifest); cleanup.NotifyLifecycle(manifest, "lifecycle.uninstall"); }
            catch (Exception error) { AppLog.Warning("Plugins", "Plugin uninstall callback failed", error); }
        PublishLifecycle(manifest, "uninstalling"); DataDirectoryService.RejectLinkedAncestors(directory);
        if (Directory.Exists(directory))
        {
            if (deleteFiles) Core.Plugins.PluginPathPolicy.AfterProcessExit(() => Directory.Delete(directory, true));
            else { var retained = Path.Combine(_storage.PluginsFolder, "Retained", manifest.Id + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.GetDirectoryName(retained)!); Core.Plugins.PluginPathPolicy.AfterProcessExit(() => Directory.Move(directory, retained)); }
        }
        Installed.Remove(manifest); Save(); OperationCompleted?.Invoke(this, CommandResults.Completed("plugins.uninstall"));
    }
    private async Task<ProviderClient> GetClientAsync(PluginManifest manifest)
    {
        if (manifest.Type != "provider") throw new InvalidOperationException("这个插件不是音源插件。");
        if (!manifest.Enabled) throw new InvalidOperationException("请先启用音源插件。");
        if (_clients.TryGetValue(manifest.Id, out var current)) return current;
        var client = new ProviderClient(Path.Combine(_storage.PluginsFolder, manifest.Id), manifest);
        try
        {
            var result = await client.CallAsync("initialize", new() { ["contractVersion"] = "1", ["hostVersion"] = "0.3.0", ["configuration"] = _sessionConfiguration.GetValueOrDefault(manifest.Id, manifest.Configuration) });
            if (!result.TryGetProperty("contractVersion", out var version) || version.GetInt32() != 1) throw new InvalidDataException("插件 Contract 握手失败。");
            _clients.Add(manifest.Id, client); return client;
        }
        catch { client.Dispose(); throw; }
    }
    public async Task<IReadOnlyList<TrackItem>> LoadCatalogAsync(PluginManifest manifest, LyricsService lyrics, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(manifest); var items = new List<TrackItem>();
            var catalog = await client.ReadCatalogAsync(cancellationToken);
            foreach (var item in catalog)
            {
                    var track = new TrackItem(manifest.Id + ":" + item.Id, item.Title, item.Artist, item.Album, "", "." + item.Format, 0) { ProviderId = manifest.Id, ProviderTrackId = item.Id, DurationSeconds = item.DurationSeconds };
                    if (item.CoverBase64 is { Length: <= 2_000_000 } cover)
                    {
                        var path = Path.Combine(_storage.ArtworkFolder, MusicLibraryScanner.StableId(track.Id) + ".image"); File.WriteAllBytes(path, Convert.FromBase64String(cover)); track.CoverPath = path;
                    }
                    if (item.Lyrics is { Length: <= 100_000 } text) lyrics.Save(track.Id, text); items.Add(track);
            }
            return items.DistinctBy(x => x.Id).ToArray();
        }
        catch { if (_clients.Remove(manifest.Id, out var client)) client.Dispose(); throw; }
    }
    public async Task<string> ResolveAsync(TrackItem track)
    {
        var manifest = Installed.FirstOrDefault(p => p.Id == track.ProviderId) ?? throw new InvalidOperationException("音源插件已卸载。");
        try
        {
            var client = await GetClientAsync(manifest);
            // 旧曲库来自前一进程；新的音源进程需先建立自己的曲目映射。
            if (!client.CatalogInitialized) await client.ReadCatalogAsync();
            var response = await client.CallAsync("playback.resolve", new() { ["trackId"] = track.ProviderTrackId! });
            var source = response.Deserialize<PlaybackSource>(AppStorage.Json) ?? throw new InvalidDataException("插件没有提供播放源。");
            if (source.Kind != "loopback-http") throw new InvalidDataException("本版 Contract 支持 loopback-http，不接受服务器 URL。");
            LoopbackRangeStream.Validate(new Uri(source.Url)); return source.Url;
        }
        catch { if (_clients.Remove(manifest.Id, out var client)) client.Dispose(); throw; }
    }
    public void Save()
    {
        RequireWritable();
        AppStorage.AtomicWrite(IndexPath, JsonSerializer.Serialize(Installed, AppStorage.Json), true);
        _storage.SyncPendingResources();
    }
    public void Dispose()
    {
        DisposeUiRuntime();
        foreach (var pair in _clients) { if (Installed.FirstOrDefault(p => p.Id == pair.Key) is { } manifest) pair.Value.NotifyLifecycle(manifest, "lifecycle.shutdown"); pair.Value.Dispose(); } _clients.Clear();
        foreach (var manifest in Installed.Where(p => p.Type is "ui" or "widget")) NotifyUiUnavailable(manifest.Id);
        foreach (var manifest in Installed.Where(p => p.Enabled)) PublishLifecycle(manifest, "shutdown");
    }
    private void PublishLifecycle(PluginManifest manifest, string stage)
    {
        if (LifecycleChanged is null) return;
        foreach (Action<PluginManifest, string> callback in LifecycleChanged.GetInvocationList())
            try { callback(manifest, stage); } catch (Exception error) { AppLog.Warning("Plugins", "Lifecycle subscriber failed", error); }
    }
}
