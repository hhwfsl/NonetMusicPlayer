using System.Text.Json;
using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>Agent 只提出宿主工具请求；不会取得 UI 引用、文件句柄或系统命令能力。</summary>
public sealed record AgentToolCall(string Id, string Operation, string[] Arguments);
[method: JsonConstructor]
public sealed record AgentMessage(string Role, string Text = "", AgentToolCall[]? Calls = null, string? ToolCallId = null, string? Reasoning = null)
{
    /// <summary>只包含经用户选择及确认的文件快照，没有本机路径或自动读取能力。</summary>
    public AgentAttachment[] Attachments { get; init; } = [];
    // 保留 SDK 3.4 的二进制构造与解构签名，不能因新增字段迫使旧进程重新构建。
    public AgentMessage(string role, string text, AgentToolCall[]? calls, string? toolCallId) : this(role, text, calls, toolCallId, null) { }
    public void Deconstruct(out string role, out string text, out AgentToolCall[]? calls, out string? toolCallId)
    { role = Role; text = Text; calls = Calls; toolCallId = ToolCallId; }
}
public sealed record AgentAttachment(string Name, string Data);
public sealed record AgentTurn(AgentMessage[] Messages, string Tools);
// 新字段均为可选；旧插件返回仅有 Text/Calls 的响应仍然有效。
[method: JsonConstructor]
public sealed record AgentReply(string Text, AgentToolCall[] Calls, string? Reasoning = null, int? InputTokens = null, int? OutputTokens = null, string? FinishReason = null)
{
    public AgentReply(string text, AgentToolCall[] calls) : this(text, calls, null, null, null, null) { }
    public void Deconstruct(out string text, out AgentToolCall[] calls) { text = Text; calls = Calls; }
}
public sealed record AgentModels(string[] Models);

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
            if (message is null || message.Role is not ("user" or "assistant" or "tool") || message.Text is null || message.Text.Length > 24_000 || message.Reasoning?.Length > 24_000)
                throw new InvalidDataException("Invalid agent message.");
            if (message.Attachments.Length > 8 || message.Attachments.Sum(a => a.Data.Length) > 28_000_000
                || message.Attachments.Any(a => a.Name.Length is 0 or > 250 || a.Data.Length > 14_000_000 || a.Name.IndexOfAny(['/', '\\']) >= 0))
                throw new InvalidDataException("Invalid user-selected attachment.");
            total += message.Text.Length + (message.Reasoning?.Length ?? 0);
            if (message.Calls is { } calls) Validate(new AgentReply("", calls));
            if (message.Role == "tool" && (message.ToolCallId is null || message.ToolCallId.Length is 0 or > 100))
                throw new InvalidDataException("Tool reply requires its call ID.");
        }
        if (total > 120_000) throw new InvalidDataException("Agent conversation is too long.");
    }
    public static void Validate(AgentReply reply)
    {
        if (reply.Text is null || reply.Text.Length > 24_000 || reply.Calls is null || reply.Calls.Length > 4
            || reply.Reasoning?.Length > 24_000 || reply.InputTokens < 0 || reply.OutputTokens < 0 || reply.FinishReason?.Length > 100)
            throw new InvalidDataException("Invalid agent reply.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in reply.Calls)
            if (call is null || string.IsNullOrEmpty(call.Id) || call.Id.Length > 100 || !ids.Add(call.Id) || call.Id.Any(char.IsControl)
                || string.IsNullOrEmpty(call.Operation) || call.Operation.Length > 50 || call.Arguments is null || call.Arguments.Length > 16
                || call.Arguments.Any(a => a is null || a.Length > (call.Operation.StartsWith("extension.", StringComparison.Ordinal) ? 100000 : 2000) || !call.Operation.StartsWith("extension.", StringComparison.Ordinal) && a.Any(char.IsControl)))
                throw new InvalidDataException("Invalid agent tool call.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AgentTurn))]
[JsonSerializable(typeof(AgentReply))]
[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(AgentModels))]
public partial class AgentPluginJson : JsonSerializerContext;
