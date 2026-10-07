using System.Text.Json;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager
{
    /// <summary>模型通信沿用隔离进程；停用、卸载和关闭立即取消当前请求。</summary>
    public async Task<AgentReply> AgentStepAsync(PluginManifest plugin, AgentTurn turn, CancellationToken token = default)
    {
        AgentPluginContract.Validate(turn);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        void Changed(PluginManifest changed, string stage)
        {
            if (changed.Id == plugin.Id && stage is "disabled" or "uninstalling" or "shutdown") operation.Cancel();
        }
        LifecycleChanged += Changed;
        try
        {
            RequireInstalled(plugin);
            if (!plugin.Enabled || plugin.Type is not ("agent" or "extension")) throw new InvalidOperationException("Agent plugin is not enabled.");
            var client = await GetClientAsync(plugin, operation.Token);
            var response = await client.CallAsync("agent.step", new() { ["turn"] = JsonSerializer.Serialize(turn, AgentPluginJson.Default.AgentTurn) }, operation.Token, TimeSpan.Zero);
            var reply = response.Deserialize(AgentPluginJson.Default.AgentReply) ?? throw new InvalidDataException("Empty agent reply.");
            AgentPluginContract.Validate(reply);
            return reply;
        }
        finally { LifecycleChanged -= Changed; }
    }
    /// <summary>显式点击获取模型才使用草稿鉴权；临时进程不会覆盖已保存配置。</summary>
    public async Task<AgentModels> AgentModelsAsync(PluginManifest plugin, string draft, CancellationToken token = default)
    {
        RequireInstalled(plugin);
        if (!plugin.Enabled || plugin.Type is not ("agent" or "extension")) throw new InvalidOperationException("Agent plugin is not enabled.");
        PluginConfigSchema.Validate(ReadConfigurationSchema(plugin), System.Text.Json.Nodes.JsonNode.Parse(draft)!.AsObject());
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        void Changed(PluginManifest changed, string stage) { if (changed.Id == plugin.Id && stage is "disabled" or "uninstalling" or "shutdown") operation.Cancel(); }
        LifecycleChanged += Changed;
        try
        {
            using var client = new ProviderClient(Path.Combine(_storage.PluginsFolder, plugin.Id), plugin);
            var hello = await client.CallAsync("initialize", new() { ["contractVersion"] = plugin.ContractVersion.ToString(), ["configuration"] = draft }, operation.Token);
            if (hello.GetProperty("contractVersion").GetInt32() != plugin.ContractVersion) throw new InvalidDataException("Invalid handshake.");
            var response = await client.CallAsync("agent.models", cancellationToken: operation.Token, requestTimeout: TimeSpan.Zero);
            var result = response.Deserialize(AgentPluginJson.Default.AgentModels) ?? throw new InvalidDataException("Empty models.");
            if (result.Models is null || result.Models.Length > 1000 || result.Models.Any(m => m.Length is 0 or > 200 || m.Any(char.IsControl))) throw new InvalidDataException("Invalid model list.");
            return result;
        }
        finally { LifecycleChanged -= Changed; }
    }
}
