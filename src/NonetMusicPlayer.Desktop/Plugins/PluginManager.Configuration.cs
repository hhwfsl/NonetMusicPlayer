using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager
{
    public JsonObject ReadConfigurationSchema(PluginManifest plugin)
    {
        RequireInstalled(plugin);
        var root = Path.Combine(_storage.PluginsFolder, plugin.Id);
        DataDirectoryService.RejectLinkedAncestors(root);
        var path = Path.Combine(root, PluginConfigSchema.FileName);
        if (!File.Exists(path)) path = Path.Combine(root, "plugin_config_schema");
        if (!File.Exists(path)) return new JsonObject();
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("插件配置规范不允许链接。");
        using var input = File.OpenRead(path); return PluginConfigSchema.Read(input);
    }
    public string EffectiveConfiguration(PluginManifest plugin)
    {
        if (_sessionConfiguration.TryGetValue(plugin.Id, out var session)) return session;
        // 只有 Agent 使用持久凭据存储，其他插件仍保留既有的会话敏感字段策略。
        if (plugin.Type is "agent" or "extension" && PluginConfigurationStore.Read(_storage.PluginsFolder, plugin) is { } persisted) { _sessionConfiguration[plugin.Id] = persisted; return persisted; }
        return plugin.Configuration;
    }
    public JsonObject ConfigurationValues(PluginManifest plugin) => PluginConfigSchema.Resolve(ReadConfigurationSchema(plugin), EffectiveConfiguration(plugin));
}
