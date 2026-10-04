using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>显式的插件宿主能力入口，不支持反射、可执行表达式或系统 Shell 动作。</summary>
public static class PluginHostActions
{
    public static void Execute(PluginManager manager, PluginManifest manifest, MainViewModel vm, PluginHostAction action)
    {
        if (!manifest.Enabled || !manager.Installed.Contains(manifest)) throw new InvalidOperationException(L10n.T("Plugins.ThePluginIsDisabledOrUninstalled"));
        var permission = action.Kind is "navigate" or "search" ? "navigation" : "player-control";
        if (!manifest.Permissions.Contains(permission)) throw new InvalidDataException(L10n.T("Plugins.ThePluginDoesNotHavePermissionForThisAction"));
        switch (action.Kind)
        {
            case "play-pause": vm.TogglePlayPauseCommand.Execute(null); break;
            case "previous": vm.PlayPreviousCommand.Execute(null); break;
            case "next": vm.PlayNextCommand.Execute(null); break;
            case "favorite": vm.ToggleFavorite(vm.CurrentTrack); break;
            case "navigate": vm.Navigate(action.Value); break;
            case "search": vm.Navigate("songs"); vm.SearchText = action.Value; break;
            default: throw new InvalidDataException("此动作不能由交互按钮执行。");
        }
    }
}
