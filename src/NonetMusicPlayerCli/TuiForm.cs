using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NonetMusicPlayer.Core.Commands;

namespace NonetMusicPlayerCli;

/// <summary>键盘表单只负责参数采集；实际校验、确认和执行仍交给共享命令路由。</summary>
internal sealed class TuiForm(string title, IEnumerable<TuiField> fields, Func<IReadOnlyList<TuiField>, string> build)
{
    public string Title { get; } = title;
    public List<TuiField> Fields { get; } = fields.ToList();
    public int Selected { get; set; }
    public int Caret { get; set; }
    public string? Error { get; set; }
    public TuiField? Current => Fields.ElementAtOrDefault(Selected);
    public string Build() => build(Fields);
    public static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    public static TuiForm FromCommand(CommandDefinition command, Func<string, string> initial, Func<string, IReadOnlyList<string>> choices)
    {
        var fields = Regex.Matches(command.Usage, "<([^>]+)>|\\[([^]]+)\\]").Select(m =>
        {
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var options = choices(name);
            if (options.Count == 0 && name.Contains('|') && !name.Contains("file") && !name.Contains("track-id") && !name.Contains("folder")) options = name.Split('|');
            return new TuiField(name, initial(name), options) { Optional = !m.Groups[1].Success, Repeatable = name.EndsWith("...", StringComparison.Ordinal), Private = command.Name == "plugins.config" && name == "json" };
        });
        return new(command.PublicUsage, fields, values => CommandSyntax.PublicName(command.Name) + " -- " + string.Join(" ", values.Where(f => !f.Optional || f.Value.Length > 0).Select(f => Quote(f.Value))));
    }
}

internal sealed class TuiField(string name, string value = "", IReadOnlyList<string>? choices = null)
{
    public string Name { get; } = name;
    public string Label { get; init; } = name;
    public string Value { get; set; } = value;
    public IReadOnlyList<string> Choices { get; } = choices ?? [];
    public bool Optional { get; init; }
    public bool Repeatable { get; init; }
    public bool Private { get; init; }
    public bool IsChoice => Choices.Count > 0;
    public void Change(int direction)
    {
        if (!IsChoice) return;
        var index = Choices.ToList().IndexOf(Value);
        Value = Choices[(Math.Max(0, index) + direction + Choices.Count) % Choices.Count];
    }
    public static TuiField Setting(string name, JsonNode? value)
    {
        var options = name switch
        {
            "language" => new[] { "zh-CN", "en-US", "ja-JP" }, "terminalMinimumLogLevel" => ["INFO", "WARN", "ERROR"],
            "playMode" => ["1", "2", "3"], _ => Array.Empty<string>()
        };
        if (value is JsonValue scalar && scalar.TryGetValue<bool>(out _)) options = ["false", "true"];
        return new(name, value is JsonValue text && text.TryGetValue<string>(out var content) ? content : value?.ToJsonString(NonetMusicPlayer.Core.Persistence.CoreJson.Readable) ?? "", options);
    }
}
