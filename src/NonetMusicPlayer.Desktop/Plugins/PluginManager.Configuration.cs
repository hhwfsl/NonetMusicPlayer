using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager
{
    /// <summary>审批升级需在配置保存时确认；降低权限不需要再次确认。</summary>
    public bool NeedsApprovalModeConfirmation(PluginManifest manifest, string json)
    {
        if (!manifest.SupportsApprovalModes) return false;
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var mode = document.RootElement.TryGetProperty("approvalMode", out var value) ? value.GetString() ?? "ask" : "ask";
        if (!Core.Plugins.PluginApprovalPolicy.IsMode(mode)) throw new InvalidDataException("Invalid approval mode.");
        return mode != "ask" && mode != manifest.ApprovalMode;
    }

    /// <summary>仅宿主确认后的内联控件使用，不重启会话，保留完整加密配置。</summary>
    internal void SetApprovalMode(PluginManifest plugin, string mode)
    {
        RequireWritable(); RequireInstalled(plugin);
        if (!plugin.SupportsApprovalModes || !PluginApprovalPolicy.IsMode(mode)) throw new InvalidDataException("Invalid approval mode.");
        var values = ConfigurationValues(plugin); values["approvalMode"] = mode; PluginConfigSchema.Validate(ReadConfigurationSchema(plugin), values);
        var previous = plugin.ApprovalMode; var priorPublic = plugin.Configuration; var priorFull = EffectiveConfiguration(plugin); var json = values.ToJsonString();
        var publicValues = JsonNode.Parse(plugin.Configuration)!.AsObject(); publicValues["approvalMode"] = mode;
        try { PluginConfigurationStore.Write(_storage.PluginsFolder, plugin, json, publicValues.ToJsonString()); plugin.ApprovalMode = mode; plugin.Configuration = publicValues.ToJsonString(); _sessionConfiguration[plugin.Id] = json; Save(); }
        catch
        {
            plugin.ApprovalMode = previous; plugin.Configuration = priorPublic; _sessionConfiguration[plugin.Id] = priorFull;
            try { PluginConfigurationStore.Write(_storage.PluginsFolder, plugin, priorFull, priorPublic); } catch { AppLog.Warning("Extensions", "Approval persistence rollback failed."); }
            throw;
        }
    }
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
    public JsonObject ConfigurationValues(PluginManifest plugin)
    {
        var values = PluginConfigSchema.Resolve(ReadConfigurationSchema(plugin), EffectiveConfiguration(plugin));
        // 安装包/旧配置不能恢复因新增权限而撤销的宿主审批授权。
        if (plugin.SupportsApprovalModes) values["approvalMode"] = plugin.ApprovalMode;
        return values;
    }
}
