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
            if (!plugin.Enabled || plugin.Type != "agent") throw new InvalidOperationException("Agent plugin is not enabled.");
            var client = await GetClientAsync(plugin, operation.Token);
            var response = await client.CallAsync("agent.step", new() { ["turn"] = JsonSerializer.Serialize(turn, AgentPluginJson.Default.AgentTurn) }, operation.Token);
            var reply = response.Deserialize(AgentPluginJson.Default.AgentReply) ?? throw new InvalidDataException("Empty agent reply.");
            AgentPluginContract.Validate(reply);
            return reply;
        }
        finally { LifecycleChanged -= Changed; }
    }
}
