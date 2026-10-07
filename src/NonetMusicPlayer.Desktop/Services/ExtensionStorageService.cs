using System.Text;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>插件键值存储只写所属 Storage 子目录，不接受路径；同 ID 更新保留数据。</summary>
public sealed class ExtensionStorageService(string directory)
{
    private readonly object _gate = new();
    private string FilePath => Path.Combine(directory, "host-values.json");
    public JsonObject Execute(JsonObject arguments)
    {
        lock (_gate)
        {
            PluginPathPolicy.RejectLinkedAncestors(FilePath);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000) throw new InvalidDataException("Stored data exceeds quota.");
            var state = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject() : new JsonObject();
            var operation = arguments["operation"]?.GetValue<string>() ?? "get";
            var key = arguments["key"]?.GetValue<string>() ?? "";
            if (operation != "list" && !System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-zA-Z0-9_.-]{1,100}$")) throw new InvalidDataException("Invalid storage key.");
            if (operation == "list") return new() { ["success"] = true, ["keys"] = new JsonArray(state.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray()) };
            if (operation == "get") return new() { ["success"] = true, ["value"] = state[key]?.DeepClone(), ["exists"] = state.ContainsKey(key) };
            if (operation == "put") state[key] = arguments["value"]?.DeepClone();
            else if (operation == "delete") state.Remove(key);
            else throw new InvalidDataException("Unknown storage operation.");
            var json = state.ToJsonString();
            if (state.Count > 512 || Encoding.UTF8.GetByteCount(json) > 2_000_000) throw new InvalidDataException("Plugin storage quota exceeded.");
            Directory.CreateDirectory(directory); AppStorage.AtomicWrite(FilePath, json);
            return new() { ["success"] = true };
        }
    }
}
