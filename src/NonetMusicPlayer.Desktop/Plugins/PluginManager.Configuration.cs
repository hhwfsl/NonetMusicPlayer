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
    public string EffectiveConfiguration(PluginManifest plugin) => _sessionConfiguration.GetValueOrDefault(plugin.Id, plugin.Configuration);
    public JsonObject ConfigurationValues(PluginManifest plugin) => PluginConfigSchema.Resolve(ReadConfigurationSchema(plugin), EffectiveConfiguration(plugin));
}
