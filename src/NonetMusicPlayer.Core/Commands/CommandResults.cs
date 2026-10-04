using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>两个宿主共用的结果格式，确保鼠标操作和对应命令具有一致输出。</summary>
public static class CommandResults
{
    public static CommandResult Completed(string operation, JsonNode? data = null) => new(operation, true, LocalizationCatalog.Format("Terminal.Completed", CommandSyntax.PublicName(operation)), data);
    public static CommandResult Failed(string operation, string message) => new(operation, false, message);
    public static string Required(PlayerCommand command, int index)
        => command.Arguments.Count > index ? command.Arguments[index] : throw new FormatException(LocalizationCatalog.Format("Commands.MissingArgument", CommandSyntax.PublicName(command.Name)));
    public static double Number(PlayerCommand command, int index, double minimum, double maximum)
    {
        if (!double.TryParse(Required(command, index), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new FormatException(LocalizationCatalog.Format("Commands.ValueRange", minimum, maximum));
        return value;
    }
    public static JsonNode? ParseSettingValue(string input)
    {
        try { return JsonNode.Parse(input); }
        catch (System.Text.Json.JsonException) { return JsonValue.Create(input); }
    }
    /// <summary>排序位置等离散参数不接受小数，避免悄悄截断用户输入。</summary>
    public static int Integer(PlayerCommand command, int index, int minimum, int maximum)
    {
        var value = Number(command, index, minimum, maximum);
        if (value != Math.Truncate(value)) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.IntegerRequired"));
        return (int)value;
    }
}
