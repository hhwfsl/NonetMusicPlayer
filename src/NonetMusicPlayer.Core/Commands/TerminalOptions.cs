using NonetMusicPlayer.Core.Diagnostics;

namespace NonetMusicPlayer.Core.Commands;

/// <summary>仅影响终端显示。行数与字体使用硬边界，防止配置导致无界内存增长。</summary>
public sealed record TerminalOptions(int ScrollbackLines = 1000, double FontSize = 14, TerminalLogLevel MinimumLevel = TerminalLogLevel.Info)
{
    public TerminalOptions Normalize() => new(Math.Clamp(ScrollbackLines, 1, 10000), double.IsFinite(FontSize) ? Math.Clamp(FontSize, 10, 32) : 14,
        Enum.IsDefined(MinimumLevel) ? MinimumLevel : TerminalLogLevel.Info);
    public static TerminalLogLevel ParseLevel(string value) => value.ToUpperInvariant() switch
    {
        "WARN" or "WARNING" => TerminalLogLevel.Warning, "ERROR" => TerminalLogLevel.Error, _ => TerminalLogLevel.Info
    };
}
