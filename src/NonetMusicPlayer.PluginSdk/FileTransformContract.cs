namespace NonetMusicPlayer.Core.Plugins;

/// <summary>可选本地文件转换角色。用户提交的文件逐个处理，不改变旧导入/播放协议。</summary>
public static class FileTransformContract
{
    public const string Capability = "files.transform.v1";
    public const string Service = "file-transform";
    public static void ValidateExtensions(IReadOnlyList<string> extensions)
    {
        if (extensions.Count > 64 || extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != extensions.Count
            || extensions.Any(e => !System.Text.RegularExpressions.Regex.IsMatch(e, "^\\.[a-z0-9]{1,16}$")))
            throw new InvalidDataException("Invalid transform extensions.");
    }
    /// <summary>转换只能生成另一文件，不允许将输入本体当作可被后续步骤改写的输出。</summary>
    public static string ValidateOutput(string input, string output)
    {
        if (!Path.IsPathFullyQualified(output) || !File.Exists(output)) throw new InvalidDataException("Transform output is missing.");
        var result = Path.GetFullPath(output);
        if ((File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Transform output cannot be a link.");
        if (string.Equals(Path.GetFullPath(input), result, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Transform must preserve the source file.");
        PluginPathPolicy.RejectLinkedAncestors(result); return result;
    }
}
