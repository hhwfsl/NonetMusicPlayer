using System.Text;
using System.Text.Json.Nodes;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>规范化后的播放器命令；参数永远作为数据处理，不进入系统 Shell。</summary>
public sealed record PlayerCommand(string Name, IReadOnlyList<string> Arguments, bool Confirmed = false, bool Declined = false);

/// <summary>统一的操作结果。UI 操作和命令行操作写入同一个结果流。</summary>
public sealed record CommandResult(string Operation, bool Success, string Message, JsonNode? Data = null, bool RequiresConfirmation = false);

public sealed record CommandDefinition(string Name, string Usage, bool Destructive = false, bool DesktopOnly = false)
{
    public string PublicUsage => CommandSyntax.PublicName(Name) + Usage[Name.Length..];
}

/// <summary>公开语法与内部操作标识分离；内部点号不是用户需要输入的命令。</summary>
public static class CommandSyntax
{
    public static string PublicName(string name) => name switch
    {
        "help" => "nonet help", "clear" => "nonet clear", "app.exit" => "nonet app exit",
        "terminal.exit" => "nonet exit", "status" => "nonet player status", "statistics" => "nonet statistics show",
        "navigate" => "nonet navigation open", "undo" => "nonet music undo", "help.open" => "nonet documentation open",
        _ => "nonet " + name.Replace('.', ' ')
    };
    public static bool IsPrivate(string input)
    {
        try { return CommandLineParser.Parse(input).Name == "plugins.config"; }
        catch (FormatException) { return input.Contains("plugins", StringComparison.OrdinalIgnoreCase) && input.Contains("config", StringComparison.OrdinalIgnoreCase); }
    }
}

/// <summary>负责宿主差异；窗口动作只在桌面宿主存在，命令协议和确认策略保持相同。</summary>
public interface IPlayerCommandBackend
{
    bool HasWindow { get; }
    Task<CommandResult> ExecuteAsync(PlayerCommand command, CancellationToken cancellationToken);
}

/// <summary>支持单/双引号与转义引号的参数解析器；保留 Windows 路径中的反斜杠。</summary>
public static class CommandLineParser
{
    public static PlayerCommand Parse(string input)
    {
        if (input.Length > 16384) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.CommandTooLong"));
        var tokens = new List<string>(); var token = new StringBuilder(); char quote = '\0'; var started = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '\\' && i + 1 < input.Length && quote == '"' && input[i + 1] == quote) { token.Append(input[++i]); started = true; continue; }
            if (c is '\'' or '"') { if (quote == '\0') { quote = c; started = true; continue; } if (quote == c) { quote = '\0'; continue; } }
            if (char.IsWhiteSpace(c) && quote == '\0') { if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; } continue; }
            if (char.IsControl(c)) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.ControlCharacters"));
            token.Append(c); started = true;
        }
        if (quote != '\0') throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.UnclosedQuote"));
        if (started) tokens.Add(token.ToString());
        if (tokens.Count == 0) throw new FormatException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Commands.EnterCommand"));
        // 两种终端已处于播放器会话中，因此程序名前缀可省略；外部 Shell 仍由 nonet 可执行文件进入。
        // lmp 仅作为已保存命令的兼容别名；新的可执行文件及公开帮助统一为 nonet。
        var commandStart = tokens[0].Equals("nonet", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("lmp", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (tokens.Count <= commandStart) throw new FormatException("nonet help");
        var selector = tokens[commandStart].ToLowerInvariant();
        var argumentStart = commandStart + 1;
        string name;
        if (selector is "--help" or "-h" or "help") name = "help";
        else if (selector == "clear") name = "clear";
        else if (selector is "exit" or "quit") name = "terminal.exit";
        else
        {
            if (selector.StartsWith('-') || selector.Contains('.') || tokens.Count <= commandStart + 1)
                throw new FormatException("nonet help");
            var category = selector; var action = tokens[commandStart + 1].ToLowerInvariant(); argumentStart = commandStart + 2;
            if (action.Contains('.') || action.StartsWith('-')) throw new FormatException("nonet help");
            name = (category, action) switch
            {
                ("player", "status") => "status", ("statistics", "show") => "statistics",
                ("navigation", "open") => "navigate", ("music", "undo") => "undo", ("documentation", "open") => "help.open",
                _ => category + "." + action
            };
        }
        var args = new List<string>(); var confirmed = false; var declined = false; var options = true;
        foreach (var value in tokens.Skip(argumentStart))
        {
            // -- 后的内容全部作为参数，允许名称恰好为 -y 或 -n 的歌单。
            if (options && value == "--") { options = false; continue; }
            if (options && value is "-y" or "--yes") { confirmed = true; continue; }
            if (options && value is "-n" or "--no") { declined = true; continue; }
            args.Add(value);
        }
        if (confirmed && declined) throw new FormatException("-y / -n");
        return new(name, args, confirmed, declined);
    }
}
