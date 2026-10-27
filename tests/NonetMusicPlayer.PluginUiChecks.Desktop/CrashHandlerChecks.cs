using System.Diagnostics;
using NonetMusicPlayer.Desktop.Services;

/// <summary>在独立夹具进程模拟退出；不终止真实播放器，不展示崩溃对话框。</summary>
internal static class CrashHandlerChecks
{
    public static int Child(string root, string helper, string mode)
    {
        AppLog.Initialize(root);
        using var observer = CrashHandlerService.Start(root, helper, quiet: true);
        if (!observer.IsAvailable) return 99;
        File.WriteAllText(Path.Combine(root, "observer.pid"), observer.ProcessId!.Value.ToString());
        if (mode == "fatal") { observer.ReportFatal(new InvalidOperationException("Fixture failure api_key=fixture-secret")); observer.CompleteNormalExit(); }
        // Environment.Exit 不执行 using/finally，验证写端消失时原生进程能独立识别异常退出。
        Environment.Exit(23); return 23;
    }
    public static void Run(string output, string helper)
    {
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        helper = Path.GetFullPath(helper);
        var normal = Path.Combine(root, "normal"); Directory.CreateDirectory(normal); AppLog.Initialize(normal);
        var service = CrashHandlerService.Start(normal, helper, quiet: true);
        Require(service.IsAvailable, "Observer started"); using var process = Process.GetProcessById(service.ProcessId!.Value);
        Thread.Sleep(150); process.Refresh(); var memory = process.PrivateMemorySize64; var cpu = process.TotalProcessorTime;
        Thread.Sleep(1000); process.Refresh(); var cpuDelta = process.TotalProcessorTime - cpu;
        Require(cpuDelta < TimeSpan.FromMilliseconds(100) && memory < 16 * 1024 * 1024, "Blocking observer idle overhead");
        service.CompleteNormalExit(); service.Dispose(); Require(process.WaitForExit(3000), "Clean shutdown stops observer");
        Require(Directory.GetFiles(Path.Combine(normal, "Logs", "Crashes")).Length == 0, "Clean shutdown has no crash record");
        foreach (var mode in new[] { "unexpected", "fatal" })
        {
            var data = Path.Combine(root, mode); Directory.CreateDirectory(data);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(CrashHandlerChecks).Assembly.Location);
            foreach (var arg in new[] { "--crash-observer-child", data, helper, mode }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!; Require(child.WaitForExit(10000) && child.ExitCode == 23, "Fixture exits abnormally");
            try { using var observer = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(data, "observer.pid")))); Require(observer.WaitForExit(8000), "Observer finished reporting"); } catch (ArgumentException) { }
            var folder = Path.Combine(data, "Logs", "Crashes"); var limit = Environment.TickCount64 + 8000;
            while (Directory.GetFiles(folder).Length == 0 && Environment.TickCount64 < limit) Thread.Sleep(20);
            var reports = Directory.GetFiles(folder); Require(reports.Length == 1, "Observer independently records " + mode);
            var text = File.ReadAllText(reports[0]); Require(text.Contains(mode == "fatal" ? "fatal-reported" : "unexpected-exit", StringComparison.Ordinal), "Exit classification");
            Require(!Directory.GetFiles(Path.Combine(data, "Logs"), "*.log").Any(path => File.ReadAllText(path).Contains("fixture-secret", StringComparison.Ordinal)), "Managed crash details remain redacted");
        }
        AppLog.Shutdown();
        Console.WriteLine($"PASS C++ observer: clean/fatal/unexpected exit, no dialogs, private={memory / 1024} KiB, idle CPU delta={cpuDelta.TotalMilliseconds:0.00} ms / 1 s.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
