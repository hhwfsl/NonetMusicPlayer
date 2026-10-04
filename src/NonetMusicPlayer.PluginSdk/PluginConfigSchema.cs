using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>有界的声明式配置规范，支持对象、列表和默认值，但不执行表达式或脚本。</summary>
public static class PluginConfigSchema
{
    public const string FileName = "plugin_config_schema.json";
    public const int MaximumBytes = 256 * 1024;
    public static JsonObject Read(Stream input)
    {
        using var bounded = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = input.Read(buffer)) > 0) { if (bounded.Length + read > MaximumBytes) throw new InvalidDataException("插件配置规范过大（最大 256 KB）。"); bounded.Write(buffer, 0, read); }
        var schema = JsonNode.Parse(bounded.ToArray(), documentOptions: new JsonDocumentOptions { MaxDepth = 24 }) as JsonObject ?? throw new InvalidDataException("插件配置规范必须是 JSON 对象。");
        var count = 0; ValidateFields(schema, 0, ref count); return schema;
    }
    private static void ValidateFields(JsonObject fields, int depth, ref int count)
    {
        if (depth > 8) throw new InvalidDataException("插件配置最多嵌套 8 层。");
        foreach (var (key, value) in fields)
        {
            if (++count > 200 || !Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_.-]{0,99}$") || value is not JsonObject field) throw new InvalidDataException("插件配置字段过多或字段名不合法。");
            var type = Type(field);
            if (type is not ("string" or "text" or "bool" or "int" or "float" or "number" or "list" or "object")) throw new InvalidDataException("不支持此插件配置字段类型。");
            foreach (var property in new[] { "description", "hint" }) if (field[property] is { } node && (node is not JsonValue || node.GetValue<string>().Length > 6000)) throw new InvalidDataException("插件配置说明过长。");
            if (field["enum"] is { } choices && (choices is not JsonArray array || array.Count is 0 or > 100 || array.Any(item => item is null || item is JsonArray || item is JsonObject option && (option["value"] is null || option["label"] is not JsonValue)))) throw new InvalidDataException("插件配置选项不合法。");
            if (type == "object" && field["items"] is JsonObject children) ValidateFields(children, depth + 1, ref count);
            if (type == "list" && field["items"] is JsonObject item) { var wrapper = new JsonObject { ["item"] = item.DeepClone() }; ValidateFields(wrapper, depth + 1, ref count); }
            if (field["default"] is { } initial) ValidateValue(field, initial, key, depth);
        }
    }
    public static string Type(JsonObject field) => field["type"]?.GetValue<string>() ?? "string";
    public static JsonNode? Default(JsonObject field) => field["default"]?.DeepClone() ?? Type(field) switch
    {
        "bool" => JsonValue.Create(false), "int" => JsonValue.Create(0), "float" or "number" => JsonValue.Create(0d), "list" => new JsonArray(),
        "object" => field["items"] is JsonObject items ? Defaults(items) : new JsonObject(), _ => JsonValue.Create("")
    };
    public static JsonObject Defaults(JsonObject schema)
    {
        var result = new JsonObject(); foreach (var (key, value) in schema) result[key] = Default((JsonObject)value!); return result;
    }
    public static JsonObject Resolve(JsonObject schema, string configuration)
    {
        var values = JsonNode.Parse(configuration) as JsonObject ?? new JsonObject();
        if (schema.Count == 0) return values;
        var result = Defaults(schema); foreach (var (key, value) in values) if (schema.ContainsKey(key)) result[key] = value?.DeepClone(); return result;
    }
    public static void Validate(JsonObject schema, JsonObject values)
    {
        if (values.ToJsonString().Length > MaximumBytes) throw new InvalidDataException("插件配置过大（最大 256 KB）。");
        foreach (var (key, field) in schema) ValidateValue((JsonObject)field!, values[key], key, 0);
        if (schema.Count > 0 && values.Any(pair => !schema.ContainsKey(pair.Key))) throw new InvalidDataException("配置包含规范未声明的字段。");
    }
    public static JsonNode Substitute(JsonNode page, JsonObject config)
    {
        JsonNode? Replace(JsonNode? node)
        {
            if (node is JsonObject obj) { foreach (var key in obj.Select(pair => pair.Key).ToArray()) { var next = Replace(obj[key]); if (!ReferenceEquals(next, obj[key])) obj[key] = next; } }
            else if (node is JsonArray array) { for (var i = 0; i < array.Count; i++) { var next = Replace(array[i]); if (!ReferenceEquals(next, array[i])) array[i] = next; } }
            else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var exact = Regex.Match(text, @"^\$\{config\.([A-Za-z_][A-Za-z0-9_.-]*)\}$");
                if (exact.Success && config.TryGetPropertyValue(exact.Groups[1].Value, out var setting)) return setting?.DeepClone();
                return JsonValue.Create(Regex.Replace(text, @"\$\{config\.([A-Za-z_][A-Za-z0-9_.-]*)\}", match =>
                {
                    if (!config.TryGetPropertyValue(match.Groups[1].Value, out var item)) return match.Value;
                    return item is JsonValue scalar && scalar.TryGetValue<string>(out var plain) ? plain : item?.ToJsonString() ?? "";
                }));
            }
            return node;
        }
        return Replace(page)!;
    }
    public static void RemoveSensitiveFields(JsonObject schema, JsonObject values)
    {
        foreach (var (key, node) in schema)
        {
            var field = (JsonObject)node!;
            if (field["sensitive"]?.GetValue<bool>() == true || field["ui:widget"]?.GetValue<string>() == "password") { values.Remove(key); continue; }
            if (field["items"] is JsonObject items)
            {
                if (Type(field) == "object" && values[key] is JsonObject obj) RemoveSensitiveFields(items, obj);
                if (Type(field) == "list" && Type(items) == "object" && items["items"] is JsonObject children && values[key] is JsonArray array) foreach (var item in array.OfType<JsonObject>()) RemoveSensitiveFields(children, item);
            }
        }
    }
    private static void ValidateValue(JsonObject field, JsonNode? value, string path, int depth)
    {
        if (depth > 8) throw new InvalidDataException(PluginMessages.Get("Plugins.PluginConfigurationNestingIsTooDeep"));
        bool Valid()
        {
            if (value is null) return field["required"]?.GetValue<bool>() != true;
            var type = Type(field);
            if (type == "bool") return value is JsonValue boolean && boolean.TryGetValue<bool>(out _);
            if (type is "int" or "float" or "number")
            {
                if (!double.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || type == "int" && number != Math.Truncate(number)) return false;
                return (field["min"] is null || number >= field["min"]!.GetValue<double>()) && (field["max"] is null || number <= field["max"]!.GetValue<double>());
            }
            if (type is "string" or "text") return value is JsonValue text && text.TryGetValue<string>(out var content) && content.Length <= (field["max_length"]?.GetValue<int>() ?? 16000) && (field["required"]?.GetValue<bool>() != true || !string.IsNullOrWhiteSpace(content));
            if (type == "list")
            {
                if (value is not JsonArray array || array.Count > 200) return false;
                if (field["items"] is JsonObject item) for (var i = 0; i < array.Count; i++) ValidateValue(item, array[i], path + "[" + i + "]", depth + 1);
                return true;
            }
            if (value is not JsonObject obj || obj.Count > 200) return false;
            if (field["items"] is JsonObject children && children.Count > 0) foreach (var (key, child) in children) ValidateValue((JsonObject)child!, obj[key], path + "." + key, depth + 1);
            return true;
        }
        if (!Valid()) throw new InvalidDataException("插件配置字段不合法：" + path);
        if (value is not null && field["enum"] is JsonArray choices && !choices.Any(choice => JsonNode.DeepEquals(choice is JsonObject option ? option["value"] : choice, value))) throw new InvalidDataException("插件配置字段不在可选值中：" + path);
    }
}
