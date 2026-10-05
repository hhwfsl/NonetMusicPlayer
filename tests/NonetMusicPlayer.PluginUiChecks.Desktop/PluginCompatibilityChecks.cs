using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;

/// <summary>冻结旧版 Contract v1 的最小包；宿主升级不能要求插件按应用版本重新打包。</summary>
internal static class PluginCompatibilityChecks
{
    public static void Run(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), "compatibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var storage = new AppStorage(Path.Combine(root, "Data"), root);
        using var manager = new PluginManager(storage);
        // 使用原有字段和默认值，不写入 SDK 3.1 新增的音频写入授权或歌词字段。
        var legacy = new[]
        {
            """{"id":"compat.theme","name":"Legacy theme","type":"theme","version":"1.0.0","tokens":{"Accent":"#6699CC"}}""",
            """{"id":"compat.widget","name":"Legacy widget","type":"widget","version":"1.0.0","widgets":[{"title":"Legacy","text":"Contract v1"}]}""",
            """{"id":"compat.provider","name":"Legacy provider","type":"provider","version":"1.0.0","permissions":["network","process"],"entryPoints":{"win-x64":"worker.exe"}}""",
            """{"id":"compat.page","name":"Legacy page","type":"ui","version":"1.0.0","permissions":[],"pageEntry":"page.json"}"""
        };
        for (var i = 0; i < legacy.Length; i++)
        {
            var manifest = manager.Install(Package(root, legacy[i], "Legacy page"));
            manager.SetEnabled(manifest, true);
            Check(manifest.ContractVersion == 1 && manifest.Enabled, "Legacy v1 installs and enables without version pinning");
            if (manifest.Type is "ui" or "widget") Check(manager.LoadPage(manifest).Widgets.Count == 1, "Legacy page/widget remains readable");
        }
        var old = manager.Install(UpdatePackage(root, "1.0.0", "Old", "string"));
        manager.Configure(old, "{\"caption\":\"Custom preserved setting\"}"); manager.SetEnabled(old, true);
        var releaseCount = 0; manager.UiPluginUnavailable += id => { if (id == old.Id) releaseCount++; };
        var updated = manager.Install(UpdatePackage(root, "1.0.1", "New", "string"));
        Check(manager.Installed.Count(p => p.Id == old.Id) == 1 && updated.Enabled && updated.Version == "1.0.1"
            && manager.ConfigurationValues(updated)["caption"]!.GetValue<string>() == "Custom preserved setting"
            && manager.LoadPage(updated).Title == "New" && releaseCount == 1, "Same ID update replaces once, preserves config/enabled state, releases old page");
        Check(!Directory.EnumerateDirectories(storage.PluginsFolder).Any(p => Path.GetFileName(p).StartsWith(".rollback-") || Path.GetFileName(p).StartsWith(".stage-")), "Successful update retains no old plugin or staging files");
        Reject(() => manager.Install(UpdatePackage(root, "1.0.1", "Duplicate", "string")), "Equal version rejected");
        Reject(() => manager.Install(UpdatePackage(root, "1.0.0", "Downgrade", "string")), "Downgrade rejected");
        Reject(() => manager.Install(UpdatePackage(root, "1.0.2", "Invalid config", "int")), "Incompatible config rejected before replacement");
        Check(updated.Enabled && manager.LoadPage(updated).Title == "New", "Rejected update keeps active old package and config");
        if (OperatingSystem.IsWindows())
        {
            // 阻止索引替换，验证已覆盖文件的事务能够恢复，且不丢失旧启用状态。
            var index = Path.Combine(storage.PluginsFolder, "installed.json"); var originalIndex = File.ReadAllBytes(index);
            using (var locked = File.Open(index, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => manager.Install(UpdatePackage(root, "1.0.2", "Must rollback", "string")), "Index save failure rolls back");
            Check(manager.Installed.Contains(updated) && updated.Enabled && manager.LoadPage(updated).Title == "New"
                && File.ReadAllBytes(index).SequenceEqual(originalIndex), "Failed commit restores old files, index, manifest and enabled state");
        }
        var changed = manager.Install(UpdatePackage(root, "1.0.2", "Changed permission", "string", statistics: true));
        Check(!changed.Enabled && !changed.AudioTagWriteConsent && manager.ConfigurationValues(changed)["caption"]!.GetValue<string>() == "Custom preserved setting", "Permission change disables update without discarding config or silently granting writes");
        using var restarted = new PluginManager(storage);
        Check(restarted.Installed.Count == 5 && restarted.Installed.Single(p => p.Id == changed.Id).Version == "1.0.2", "Restart reads updated index alongside all legacy plugin types");
        Console.WriteLine("PASS legacy Contract v1 theme/widget/provider/UI compatibility, same-ID update, config/state preservation, no old files, rejection/rollback, permission safety, restart");
    }
    private static string UpdatePackage(string root, string version, string title, string type, bool statistics = false)
    {
        var manifest = new PluginManifest { Id = "compat.update", Name = "Update fixture", Type = "ui", Version = version, PageEntry = "page.json", Permissions = statistics ? ["statistics"] : [] };
        var schema = type == "string" ? """{"caption":{"type":"string","default":"Default"}}""" : """{"caption":{"type":"int","default":1}}""";
        return Package(root, JsonSerializer.Serialize(manifest, AppStorage.Json), title, schema);
    }
    private static string Package(string root, string manifest, string title, string? schema = null)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".impp");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("manifest.json", manifest);
        using var json = JsonDocument.Parse(manifest);
        if (json.RootElement.GetProperty("type").GetString() == "provider") Write("worker.exe", "Never executed by this fixture");
        if (json.RootElement.GetProperty("type").GetString() == "ui") Write("page.json", JsonSerializer.Serialize(new { schemaVersion = 1, title, widgets = new[] { new { type = "text", text = schema is null ? "Legacy text" : schema.Contains("\"int\"") ? "Numeric setting" : "${config.caption}" } } }));
        if (schema is not null) Write(PluginConfigSchema.FileName, schema);
        return path;
        void Write(string name, string text) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
    }
    private static void Reject(Action operation, string message)
    {
        try { operation(); } catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
