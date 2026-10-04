using NonetMusicPlayer.Desktop.Services;

internal static class LogChecks
{
    internal static void Run(string output)
    {
        var previousRoot = Path.GetDirectoryName(AppLog.LogDirectory)!;
        var root = Path.Combine(Path.GetFullPath(output), "log-check-data-" + Guid.NewGuid().ToString("N")); AppLog.Initialize(root); Directory.CreateDirectory(AppLog.LogDirectory);
        for (var i = 0; i < 14; i++)
        {
            var path = Path.Combine(AppLog.LogDirectory, $"2000-01-{i + 1:00}_{AppLog.Version}.log"); File.WriteAllText(path, "old log fixture"); File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
        var secrets = new[] { "SECRET_PASSWORD", "SECRET_BEARER", "SECRET_COOKIE", "SECRET_COOKIE_TWO", "SECRET_SESSION", "SECRET_URL_QUERY", "ESCAPED_SECRET", "ESCAPED_URL_SECRET", "1234567890abcdef1234567890abcdef" };
        AppLog.Error("Safety", "https://user:password@example.test/audio?token=SECRET_URL_QUERY\nAuthorization: Bearer SECRET_BEARER\nCookie: first=SECRET_COOKIE; second=SECRET_COOKIE_TWO\n{\"password\":\"SECRET_PASSWORD\", \"sessionSecret\":\"SECRET_SESSION\"}\nopaque=1234567890abcdef1234567890abcdef\nD:\\PrivateMusic\\must-not-appear.flac", new IOException("Token=SECRET_BEARER; file https://example.test/?secret=SECRET_URL_QUERY"));
        AppLog.Warning("SafetyEscaped", "{\\\"sessionSecret\\\":\\\"ESCAPED_SECRET\\\"}\nhttps:\\/\\/example.test/?token=ESCAPED_URL_SECRET\n/data/PrivateMedia Space/must-not-appear.flac");
        Parallel.For(0, 100, i => AppLog.Info("Concurrency", $"Log record {i}"));
        Require(AppLog.Flush(), "Log queue flush");
        var files = Directory.GetFiles(AppLog.LogDirectory, "*.log"); Require(files.Length <= 10, "Bounded log retention");
        Require(files.All(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^\d{4}-\d{2}-\d{2}_[0-9A-Za-z.-]+\.log$")), "Log filename contains only separated date and version");
        var text = string.Join("\n", files.Select(ReadActiveLog));
        foreach (var secret in secrets) Require(!text.Contains(secret), "Logs must not retain credentials or authenticated URLs");
        Require(!text.Contains("PrivateMusic") && !text.Contains("PrivateMedia") && !text.Contains("must-not-appear"), "Logs must hide private media paths");
        Require(text.Contains("Safety") && text.Contains("Concurrency") && text.Contains("IOException"), "Useful diagnosis remains after redaction");
        var active = Path.Combine(AppLog.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}_{AppLog.Version}.log");
        AppLog.Shutdown();
        using (var large = new FileStream(active, FileMode.Append, FileAccess.Write))
        { var block = System.Text.Encoding.UTF8.GetBytes(new string('r', 16383) + "\n"); for (var i = 0; i < 2050; i++) large.Write(block); }
        AppLog.Initialize(root); AppLog.Info("Retention", "retained newest record"); Require(AppLog.Flush(), "Retention flush");
        Require(new FileInfo(active).Length < 32 * 1024 * 1024 && ReadActiveLog(active).Contains("retained newest record"), "Oversize daily file keeps a bounded tail without suffixes");
        var writer = typeof(AppLog).GetField("_writer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var enqueue = writer.GetType().GetMethod("Write")!;
        enqueue.Invoke(writer, ["before midnight", new DateOnly(2026, 10, 2)]);
        enqueue.Invoke(writer, ["after midnight", new DateOnly(2026, 10, 3)]);
        Require(AppLog.Flush(), "Midnight rollover flush");
        Require(Directory.GetFiles(AppLog.LogDirectory, "2026-10-02_*.log").Any(f => ReadActiveLog(f).Contains("before midnight")) &&
            Directory.GetFiles(AppLog.LogDirectory, "2026-10-03_*.log").Any(f => ReadActiveLog(f).Contains("after midnight")), "Queue records retain their date and roll over at midnight");
        AppLog.Shutdown(); AppLog.Initialize(previousRoot);
        Console.WriteLine("PASS LOGS: background concurrent writes, flush, bounded rotation, credential/URL/path redaction and exception diagnostics");
    }
    private static string ReadActiveLog(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
