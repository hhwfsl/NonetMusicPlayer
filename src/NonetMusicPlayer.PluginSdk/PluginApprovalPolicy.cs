namespace NonetMusicPlayer.Core.Plugins;

/// <summary>审批只影响已获授权的宿主事务，不扩大插件权限或授予系统 Shell。</summary>
public static class PluginApprovalPolicy
{
    public static bool IsMode(string mode) => mode is "ask" or "assist" or "full";
    public static bool RequiresConfirmation(string mode, string operation, bool normallyRequired)
    {
        if (!IsMode(mode)) throw new InvalidDataException("Invalid approval mode.");
        if (!normallyRequired || mode == "full") return false;
        // assist 只省略可恢复的界面/播放设置，删除和插件代码执行仍需确认。
        return mode != "assist" || operation is not ("settings.set" or "ui.layout.apply" or "ui.layout.reset");
    }
}

