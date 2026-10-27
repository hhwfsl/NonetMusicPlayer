using System.Diagnostics;
using System.Globalization;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>以专用标准输入管道连接原生观察器；平时不发送心跳、不创建监测线程。</summary>
public sealed class CrashHandlerService : IDisposable
{
    private Process? _observer;
    private int _fatal, _normal, _disposed;
    public bool IsAvailable { get { try { return _observer is { HasExited: false }; } catch (InvalidOperationException) { return false; } } }
    public int? ProcessId => _observer?.Id;
    public static CrashHandlerService Start(string dataRoot, string? executable = null, bool quiet = false)
    {
        var service = new CrashHandlerService();
        var file = executable ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "NonetCrashHandler.exe" : "NonetCrashHandler");
        if (!File.Exists(file)) { AppLog.Warning("CrashHandler", "Native crash observer is not present."); return service; }
        try
        {
            var folder = Path.Combine(Path.GetFullPath(dataRoot), "Logs", "Crashes");
            PluginPathPolicy.RejectLinkedAncestors(folder); Directory.CreateDirectory(folder);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            foreach (var value in new[] { "--watch", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), folder, MainWindowVersion(), L10n.Language }) start.ArgumentList.Add(value);
            if (quiet) start.ArgumentList.Add("--quiet");
            service._observer = Process.Start(start) ?? throw new IOException("Unable to start native crash observer.");
            L10n.LanguageChanged += service.LanguageChanged;
            AppLog.Info("CrashHandler", "Native crash observer started; PID=" + service._observer.Id);
        }
        catch (Exception error) { AppLog.Warning("CrashHandler", "Crash observer unavailable; application logging remains active.", error); }
        return service;
    }
    private static string MainWindowVersion() => typeof(CrashHandlerService).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    public void ReportFatal(Exception? error)
    {
        if (Interlocked.Exchange(ref _fatal, 1) != 0) return;
        AppLog.Error("CrashHandler", "Fatal application error", error); AppLog.Flush(); Send('F');
    }
    public void CompleteNormalExit() => Interlocked.Exchange(ref _normal, 1);
    private void LanguageChanged(object? sender, EventArgs e) => Send(L10n.Language.StartsWith("en", StringComparison.Ordinal) ? 'E' : L10n.Language.StartsWith("ja", StringComparison.Ordinal) ? 'J' : 'Z');
    private void Send(char marker)
    {
        try { _observer?.StandardInput.BaseStream.WriteByte((byte)marker); _observer?.StandardInput.BaseStream.Flush(); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        L10n.LanguageChanged -= LanguageChanged;
        if (Volatile.Read(ref _normal) != 0 && Volatile.Read(ref _fatal) == 0) Send('C');
        try { _observer?.StandardInput.Dispose(); } catch (IOException) { }
        // 仅释放本地句柄，崩溃提示由独立进程继续承载，不在 UI 线程等待它关闭。
        _observer?.Dispose(); _observer = null;
    }
}
