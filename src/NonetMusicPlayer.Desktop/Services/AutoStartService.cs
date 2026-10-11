using System.Security;
using Microsoft.Win32;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>仅注册当前用户登录后的启动项；默认不创建，关闭时只移除 Nonet 自身的注册。</summary>
public static class AutoStartService
{
    public const string RegistrationName = "Nonet";
    public const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string BuildWindowsCommand(string executable) => "\"" + ValidateExecutable(executable) + "\"";
    public static string BuildLinuxEntry(string executable)
    {
        executable = ValidateExecutable(executable);
        var escaped = executable.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        return "[Desktop Entry]\nType=Application\nName=Nonet\nExec=\"" + escaped + "\"\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
    }
    public static string BuildMacEntry(string executable) => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\"><dict><key>Label</key><string>app.nonet.autostart</string><key>ProgramArguments</key><array><string>" + SecurityElement.Escape(ValidateExecutable(executable)) + "</string></array><key>RunAtLoad</key><true/><key>KeepAlive</key><false/></dict></plist>\n";
    private static string ValidateExecutable(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (executable.Any(char.IsControl) || executable.Contains('"')) throw new InvalidDataException(L10n.T("Startup.InvalidExecutable"));
        return executable;
    }
    public static void SetEnabled(bool enabled)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException(L10n.T("Startup.InvalidExecutable"));
        // dotnet run 的共享运行器不能作为本应用启动项；必须使用 Nonet 的应用入口。
        if (!Path.GetFileNameWithoutExtension(executable).Equals("Nonet", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L10n.T("Startup.InvalidExecutable"));
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(WindowsRunKey);
            if (enabled) key.SetValue(RegistrationName, BuildWindowsCommand(executable), RegistryValueKind.String);
            else key.DeleteValue(RegistrationName, throwOnMissingValue: false);
        }
        else
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var file = OperatingSystem.IsMacOS() ? Path.Combine(profile, "Library", "LaunchAgents", "app.nonet.autostart.plist")
                : Path.Combine(string.IsNullOrWhiteSpace(config) ? Path.Combine(profile, ".config") : Path.GetFullPath(config), "autostart", "nonet.desktop");
            DataDirectoryService.RejectLinkedAncestors(file);
            if (enabled) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); AppStorage.AtomicWrite(file, OperatingSystem.IsMacOS() ? BuildMacEntry(executable) : BuildLinuxEntry(executable)); }
            else if (File.Exists(file)) File.Delete(file);
            // 不执行 launchctl bootout，防止取消启动项时终止正由 launchd 启动的当前播放器。
        }
        AppLog.Info("Startup", enabled ? "Enabled current-user login startup" : "Disabled current-user login startup");
    }
}
