using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NonetMusicPlayer.Desktop.Services;

public static class DefaultAudioAppService
{
    public const string ProgId = "NonetMusicPlayer.Audio";
    public sealed record RegistrationValue(string Key, string Name, string Value);
    public static IReadOnlyList<RegistrationValue> RegistrationPlan(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (executable.Contains('"')) throw new InvalidDataException("应用路径无效。");
        var command = "\"" + executable + "\" \"%1\"";
        var icon = "\"" + executable + "\",0";
        var result = new List<RegistrationValue>
        {
            new(@"Software\Classes\" + ProgId, "", "Nonet audio"),
            new(@"Software\Classes\" + ProgId + @"\DefaultIcon", "", icon),
            new(@"Software\Classes\" + ProgId + @"\shell\open\command", "", command),
            new(@"Software\Classes\Applications\Nonet.exe", "FriendlyAppName", "Nonet"),
            new(@"Software\Classes\Applications\Nonet.exe\shell\open\command", "", command),
            new(@"Software\NonetMusicPlayer\Capabilities", "ApplicationName", "Nonet"),
            new(@"Software\NonetMusicPlayer\Capabilities", "ApplicationDescription", "Local audio and playlist player"),
            new(@"Software\NonetMusicPlayer\Capabilities", "ApplicationIcon", icon),
            new(@"Software\RegisteredApplications", "NonetMusicPlayer", @"Software\NonetMusicPlayer\Capabilities")
        };
        foreach (var extension in MusicLibraryScanner.SupportedExtensions.Order())
        {
            result.Add(new(@"Software\NonetMusicPlayer\Capabilities\FileAssociations", extension, ProgId));
            result.Add(new(@"Software\Classes\Applications\Nonet.exe\SupportedTypes", extension, ""));
            result.Add(new(@"Software\Classes\" + extension + @"\OpenWithProgids", ProgId, ""));
        }
        return result;
    }
    public static void RegisterAndOpenSettings()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var executable = Environment.ProcessPath ?? throw new InvalidDataException(L10n.T("Common.CouldNotLocateTheApplicationExecutable"));
        if (!Path.GetFileName(executable).Equals("Nonet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L10n.T("Playback.SetTheDefaultAudioPlayerFromAPublishedBuild"));
        // 只注册当前用户的打开方式能力，不修改系统保护的 UserChoice。
        foreach (var item in RegistrationPlan(executable))
        {
            using var key = Registry.CurrentUser.CreateSubKey(item.Key);
            key.SetValue(item.Name, item.Value, RegistryValueKind.String);
        }
        SHChangeNotify(0x08000000, 0, 0, 0);
        var uri = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? "ms-settings:defaultapps?registeredAppUser=NonetMusicPlayer" : "ms-settings:defaultapps";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
    public static string LinuxDesktopEntry(string executable, string icon)
    {
        // Exec 先按桌面入口字符串解析，再按带引号参数解析，分别转义。
        static string Escape(string value)
        {
            var quoted = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
            return quoted.Replace("\\", "\\\\", StringComparison.Ordinal);
        }
        if (executable.Contains('\n') || executable.Contains('\r') || icon.Contains('\n') || icon.Contains('\r')) throw new InvalidDataException(L10n.T("Common.InvalidApplicationPath"));
        return "[Desktop Entry]\nType=Application\nName=NonetMusicPlayer\nComment=Audio and playlist player\nExec=\"" + Escape(executable) + "\" %F\nIcon=" + icon.Replace("\\", "\\\\", StringComparison.Ordinal) + "\nTerminal=false\nCategories=AudioVideo;Audio;Player;\nMimeType=audio/mpeg;audio/flac;audio/x-flac;audio/wav;audio/x-wav;audio/mp4;audio/aac;audio/ogg;audio/x-vorbis+ogg;audio/opus;audio/x-opus+ogg;audio/x-ms-wma;audio/aiff;audio/x-ape;audio/x-wavpack;\n";
    }
    public static async Task RegisterLinuxAsync(AppStorage storage)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var executable = Environment.ProcessPath ?? throw new InvalidDataException(L10n.T("Common.CouldNotLocateTheApplicationExecutable"));
        var folder = Path.Combine(storage.Root, "Integration"); Directory.CreateDirectory(folder);
        var desktop = Path.Combine(folder, "NonetMusicPlayer.desktop"); var icon = Path.Combine(folder, "NonetMusicPlayer.png");
        using (var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Nonet/Assets/icon.ico")))
        using (var bitmap = new Avalonia.Media.Imaging.Bitmap(stream)) bitmap.Save(icon, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        AppStorage.AtomicWrite(desktop, LinuxDesktopEntry(executable, icon));
        // xdg-utils 仅写入用户的系统集成入口，不修改音频或数据文件。
        var info = new ProcessStartInfo("xdg-desktop-menu") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "install", "--mode", "user", "--novendor", desktop }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException(L10n.T("Common.CouldNotStartTheSystemAssociationTool"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(L10n.T("Common.SystemRegistrationFailedCheckXdgUtilsAndYourDesktop"));
        }
        if (process.ExitCode != 0) throw new InvalidOperationException(L10n.T("Common.SystemRegistrationFailedCheckXdgUtilsAndYourDesktop"));
    }
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
