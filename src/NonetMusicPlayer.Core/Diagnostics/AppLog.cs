using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace NonetMusicPlayer.Core.Diagnostics;

/// <summary>有界异步日志；记录失败不打断播放，认证信息和媒体路径会脱敏。</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static LogWriter? _writer;
    private static readonly Queue<LogEntry> Recent = new();
    private static int _recentCharacters;
    /// <summary>仅发布成功写入磁盘的记录，终端不再自行拼造另一套日志。</summary>
    public static event Action<LogEntry>? EntryWritten;
    public static IReadOnlyList<LogEntry> RecentEntries { get { lock (Gate) return Recent.ToArray(); } }
    public static string Version { get; } = typeof(AppLog).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    /// <summary>宿主可指定尚未初始化时的数据目录；默认仍限定在程序目录内。</summary>
    public static Func<string> DefaultRootResolver { get; set; } = () => Path.Combine(AppContext.BaseDirectory, "Data");
    public static string LogDirectory => GetWriter().DirectoryPath;
    public static void Initialize(string root)
    {
        LogWriter? previous;
        lock (Gate)
        {
            var directory = Path.Combine(Path.GetFullPath(root), "Logs");
            if (_writer?.DirectoryPath == directory) return;
            previous = _writer; Recent.Clear(); _recentCharacters = 0; _writer = new LogWriter(directory);
        }
        previous?.Dispose();
    }
    public static void Info(string area, string message) => Write("INFO", area, message, null);
    public static void Warning(string area, string message, Exception? exception = null) => Write("WARN", area, message, exception);
    public static void Error(string area, string message, Exception? exception = null) => Write("ERROR", area, message, exception);
    public static bool Flush(TimeSpan? timeout = null)
    {
        try { return GetWriter().Flush(timeout ?? TimeSpan.FromSeconds(2)); } catch { return false; }
    }
    public static void Shutdown()
    {
        LogWriter? writer; lock (Gate) { writer = _writer; _writer = null; } writer?.Dispose();
    }
    private static LogWriter GetWriter()
    {
        lock (Gate) return _writer ??= new LogWriter(Path.Combine(DefaultRootResolver(), "Logs"));
    }
    private static void Write(string level, string area, string message, Exception? exception)
    {
        try
        {
            var content = Redact(area) + " · " + Redact(message);
            if (exception is not null) content += "\n" + Redact(exception.ToString());
            if (content.Length > 16384) content = content[..16384] + " [truncated]";
            var timestamp = DateTimeOffset.Now;
            GetWriter().Write($"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {content}", DateOnly.FromDateTime(timestamp.DateTime));
        }
        catch { /* 日志失败不能中断可恢复的播放或界面操作。 */ }
    }
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try
        {
            var value = Regex.Replace(text, @"\\+/", "/", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, @"(?im)^[^\r\n]*\\[\""'](?:access[_-]?token|refresh[_-]?token|token|authorization|password|secret|session[_-]?(?:secret|key|token)|api[_-]?key|cookie)\\[\""']\s*[:=][^\r\n]*$", "[sensitive escaped payload redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, @"(?i)\b(?:https?|ftp|file)://[^\s<>\""']+", "[URL redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, @"(?im)^(\s*(?:Authorization|Cookie|Set-Cookie)\s*:\s*).*$", "$1[redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, @"(?i)\b(?:Bearer|Basic)\s+[^\s,;]+", "[authorization redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, """(?i)(["']?(?:access[_-]?token|refresh[_-]?token|id[_-]?token|token|authorization|password|passwd|secret|session[_-]?(?:secret|key|token|id)|client[_-]?secret|private[_-]?key|api[_-]?key|cookie|set-cookie)["']?\s*[:=]\s*)(?:"(?:\\.|[^"\\\r\n])*"|'(?:\\.|[^'\\\r\n])*'|[^\s,;\r\n}]+)""", "$1[redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, @"(?<![A-Za-z0-9])[0-9a-fA-F]{32,}(?![A-Za-z0-9])", "[opaque id redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, @"(?i)(?:[A-Z]:\\|\\\\)[^\r\n\""<>]*", "[local path redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            value = Regex.Replace(value, @"(?<![\w:])/(?:[^\s/\""<>]+/)*[^\s/\""<>][^\r\n\""<>]*", "[local path redacted]", RegexOptions.CultureInvariant, RegexTimeout);
            return value;
        }
        catch { return "[log details omitted: redaction unavailable]"; }
    }
    private sealed class LogWriter : IDisposable
    {
        private const long MaxBytes = 32 * 1024 * 1024;
        private const int MaxFiles = 10;
        private readonly Channel<LogRecord> _queue = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(1024) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        private readonly Task _worker;
        public string DirectoryPath { get; }
        public LogWriter(string directory) { DirectoryPath = directory; _worker = Task.Run(ConsumeAsync); }
        public void Write(string message, DateOnly date) => _queue.Writer.TryWrite(new LogRecord(message, null, date));
        public bool Flush(TimeSpan timeout)
        {
            var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource(timeout);
            _queue.Writer.WriteAsync(new LogRecord(null, barrier, default), cancellation.Token).AsTask().GetAwaiter().GetResult();
            return barrier.Task.Wait(timeout) && barrier.Task.GetAwaiter().GetResult();
        }
        private async Task ConsumeAsync()
        {
            StreamWriter? writer = null;
            DateOnly currentDate = default;
            try
            {
                await foreach (var record in _queue.Reader.ReadAllAsync())
                {
                    try
                    {
                        if (record.Message is not null)
                        {
                            if (writer is null || currentDate != record.Date)
                            {
                                writer?.Dispose(); Directory.CreateDirectory(DirectoryPath);
                                currentDate = record.Date;
                                var path = Path.Combine(DirectoryPath, $"{currentDate:yyyy-MM-dd}_{Version}.log");
                                writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                                RemoveOldLogs();
                            }
                            if (writer.BaseStream.Length >= MaxBytes)
                            {
                                // 单日文件达到上限时保留后半段完整行，文件名始终只有日期与版本。
                                var path = ((FileStream)writer.BaseStream).Name; writer.Dispose();
                                await CompactFileAsync(path);
                                writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                            }
                            await writer.WriteLineAsync(record.Message); await writer.FlushAsync();
                            // 等级只取时间戳后第一个标记，正文中的相同文字不能改变过滤结果。
                            var marker = record.Message.IndexOf(" [", StringComparison.Ordinal);
                            var level = marker < 0 ? "" : record.Message[(marker + 1)..];
                            var entry = new LogEntry(level.StartsWith("[ERROR] ", StringComparison.Ordinal) ? TerminalLogLevel.Error
                                : level.StartsWith("[WARN] ", StringComparison.Ordinal) ? TerminalLogLevel.Warning : TerminalLogLevel.Info, record.Message);
                            lock (Gate)
                            {
                                Recent.Enqueue(entry); _recentCharacters += entry.Text.Length;
                                while (Recent.Count > 10000 || _recentCharacters > 2 * 1024 * 1024) _recentCharacters -= Recent.Dequeue().Text.Length;
                            }
                            if (EntryWritten is not null)
                                foreach (Action<LogEntry> subscriber in EntryWritten.GetInvocationList())
                                    try { subscriber(entry); } catch { /* 显示端故障不能使磁盘日志失败。 */ }
                        }
                        if (record.Barrier is not null) { if (writer is not null) await writer.FlushAsync(); record.Barrier.TrySetResult(true); }
                    }
                    catch
                    {
                        record.Barrier?.TrySetResult(false); try { writer?.Dispose(); } catch { } writer = null;
                    }
                }
            }
            finally { try { writer?.Dispose(); } catch { } }
        }
        private static async Task CompactFileAsync(string path)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    source.Seek(Math.Max(0, source.Length - MaxBytes / 2), SeekOrigin.Begin);
                    // 丢弃部分 UTF-8 行；固定缓冲复制，避免同时在内存中保留整份日志。
                    while (source.ReadByte() is { } value && value is not (-1 or 10)) { }
                    using var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await source.CopyToAsync(target, 65536); await target.FlushAsync();
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private void RemoveOldLogs()
        {
            foreach (var file in new DirectoryInfo(DirectoryPath).EnumerateFiles("*.log")
                .Where(f => Regex.IsMatch(f.Name, @"^\d{4}-\d{2}-\d{2}_[0-9A-Za-z.-]+\.log$", RegexOptions.CultureInvariant, RegexTimeout))
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxFiles))
                try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public void Dispose() { _queue.Writer.TryComplete(); try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { } }
        private sealed record LogRecord(string? Message, TaskCompletionSource<bool>? Barrier, DateOnly Date);
    }
}

/// <summary>磁盘日志的固定等级；终端过滤不会改变写入策略。</summary>
public enum TerminalLogLevel { Info, Warning, Error }
public sealed record LogEntry(TerminalLogLevel Level, string Text);
