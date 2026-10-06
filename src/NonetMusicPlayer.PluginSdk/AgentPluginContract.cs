using System.Text.Json;
using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>Agent 只提出宿主工具请求；不会取得 UI 引用、文件句柄或系统命令能力。</summary>
public sealed record AgentToolCall(string Id, string Operation, string[] Arguments);
public sealed record AgentMessage(string Role, string Text = "", AgentToolCall[]? Calls = null, string? ToolCallId = null);
public sealed record AgentTurn(AgentMessage[] Messages, string Tools);
public sealed record AgentReply(string Text, AgentToolCall[] Calls);

/// <summary>共享有界输入及响应校验，AOT 进程与桌面使用相同的数据协议。</summary>
public static class AgentPluginContract
{
    public static void Validate(AgentTurn turn)
    {
        if (turn.Messages is null || turn.Messages.Length is < 1 or > 80 || turn.Tools is null || turn.Tools.Length > 24_000)
            throw new InvalidDataException("Invalid agent turn.");
        var total = 0;
        foreach (var message in turn.Messages)
        {
            if (message is null || message.Role is not ("user" or "assistant" or "tool") || message.Text is null || message.Text.Length > 24_000)
                throw new InvalidDataException("Invalid agent message.");
            total += message.Text.Length;
            if (message.Calls is { } calls) Validate(new AgentReply("", calls));
            if (message.Role == "tool" && (message.ToolCallId is null || message.ToolCallId.Length is 0 or > 100))
                throw new InvalidDataException("Tool reply requires its call ID.");
        }
        if (total > 120_000) throw new InvalidDataException("Agent conversation is too long.");
    }
    public static void Validate(AgentReply reply)
    {
        if (reply.Text is null || reply.Text.Length > 24_000 || reply.Calls is null || reply.Calls.Length > 4)
            throw new InvalidDataException("Invalid agent reply.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in reply.Calls)
            if (call is null || string.IsNullOrEmpty(call.Id) || call.Id.Length > 100 || !ids.Add(call.Id) || call.Id.Any(char.IsControl)
                || string.IsNullOrEmpty(call.Operation) || call.Operation.Length > 50 || call.Arguments is null || call.Arguments.Length > 16
                || call.Arguments.Any(a => a is null || a.Length > 2000 || a.Any(char.IsControl)))
                throw new InvalidDataException("Invalid agent tool call.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AgentTurn))]
[JsonSerializable(typeof(AgentReply))]
[JsonSerializable(typeof(RpcRequest))]
public partial class AgentPluginJson : JsonSerializerContext;
