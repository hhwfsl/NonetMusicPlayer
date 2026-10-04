using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>用户级跨安装目录的单实例所有权与有界本地文件激活通信。</summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipe;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string[]> _pending = new();
    private Action<string[]>? _deliver;
    private Task? _listener;
    public bool IsPrimary { get; }
    public SingleInstanceService(string? identity = null)
    {
        identity ??= "NonetMusicPlayer-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "@" + Environment.MachineName)))[..24];
        _pipe = identity;
        _mutex = new Mutex(true, (OperatingSystem.IsWindows() ? "Global\\" : "") + identity, out var created);
        IsPrimary = created;
        if (created) _listener = Task.Run(ListenAsync);
    }
    public void Receive(Action<string[]> callback)
    {
        _deliver = callback;
        while (_pending.TryDequeue(out var files)) callback(files);
    }
    public async Task<bool> ForwardAsync(IEnumerable<string> paths)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var pipe = new NamedPipeClientStream(".", _pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var files = paths.Where(p => !p.StartsWith("--", StringComparison.Ordinal)).Take(128).Select(Path.GetFullPath).ToArray();
            var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files, AppStorage.Json));
            if (data.Length > 32768) return false;
            await pipe.WriteAsync(BitConverter.GetBytes(data.Length), timeout.Token).ConfigureAwait(false); await pipe.WriteAsync(data, timeout.Token).ConfigureAwait(false);
            var acknowledged = new byte[1]; await pipe.ReadExactlyAsync(acknowledged, timeout.Token).ConfigureAwait(false); return acknowledged[0] == 1;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var size = new byte[4]; await server.ReadExactlyAsync(size, timeout.Token).ConfigureAwait(false); var length = BitConverter.ToInt32(size);
                if (length is < 2 or > 32768) continue;
                var data = new byte[length]; await server.ReadExactlyAsync(data, timeout.Token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(data);
                if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 128) continue;
                var files = json.RootElement.EnumerateArray().Select(item => item.GetString() ?? "").Where(Path.IsPathFullyQualified).ToArray();
                _pending.Enqueue(files);
                if (_deliver is { } deliver) while (_pending.TryDequeue(out var request)) deliver(request);
                await server.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException) { AppLog.Warning("Activation", "本地文件激活未完成", error); }
        }
    }
    public void Dispose()
    {
        _stop.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose(); _stop.Dispose();
    }
}
