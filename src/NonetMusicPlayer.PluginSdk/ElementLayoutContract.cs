using System.Text.Json.Nodes;
using System.Text.Json;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>可选控件级布局协议，不引用 GUI 库。目标来自 ui/elements，名称唯一时可直接使用控件名。</summary>
public static class ElementLayoutContract
{
    public const string Capability = "ui.elements.v1";
    public const int MaximumPatches = 256;
    public static JsonObject Catalog(int offset = 0, int limit = 100) => new() { ["operation"] = "elements", ["offset"] = offset, ["limit"] = limit };
    /// <summary>在已查询的控件锚点插入数据 UI；原生控件通过 native:true 和对应权限使用相同服务。</summary>
    public static JsonObject Mount(string key, string target, ExtensionNode view, string placement = "append") => new()
    {
        ["operation"] = "mount-element", ["key"] = key, ["target"] = target, ["placement"] = placement,
        ["view"] = JsonSerializer.SerializeToNode(view, ExtensionJson.Default.ExtensionNode)
    };
    public static JsonObject Unmount(string? key = null) => new() { ["operation"] = "unmount-element", ["key"] = key };
    public static JsonObject Reset() => new() { ["operation"] = "reset-elements" };
    /// <summary>生成调用参数；属性是有限的布局值，不允许绑定表达式、脚本或任意反射。</summary>
    public static JsonObject Update(params ElementLayoutPatch[] patches) => new()
    {
        ["operation"] = "update-elements",
        ["patches"] = new JsonArray(patches.Select(p => (JsonNode)new JsonObject
        {
            ["target"] = p.Target, ["properties"] = p.Properties.DeepClone(), ["parent"] = p.Parent,
            ["index"] = p.Index
        }).ToArray())
    };
}

/// <summary>Parent 非空时重排到目标 Panel；停用插件恢复父级、顺序和原绑定。Index=-1 表示末尾。</summary>
public sealed record ElementLayoutPatch(string Target, JsonObject Properties, string? Parent = null, int Index = -1);
