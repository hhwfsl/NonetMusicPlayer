using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>保留已发布桌面 API，桌面与 CLI 复用共享核心的账户加密配置存储。</summary>
public static class PluginConfigurationStore
{
    public static string PathFor(string pluginsFolder, PluginManifest plugin) => ProtectedPluginConfiguration.PathFor(pluginsFolder, plugin);
    public static string? Read(string pluginsFolder, PluginManifest plugin) => ProtectedPluginConfiguration.Read(pluginsFolder, plugin);
    public static void Write(string pluginsFolder, PluginManifest plugin, string full, string publicValues) => ProtectedPluginConfiguration.Write(pluginsFolder, plugin, full, publicValues);
    public static void Delete(string pluginsFolder, PluginManifest plugin) => ProtectedPluginConfiguration.Delete(pluginsFolder, plugin);
}
