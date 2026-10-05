using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Core.Persistence;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>隔离音源进程的 JSON-RPC 客户端，串行请求、响应限长并在失败后释放进程。</summary>
public sealed class ProviderClient : IDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _rpcGate = new(1);
    private long _sequence;
    private bool _disposed;
    public bool CatalogInitialized { get; private set; }
    public bool IsDisposed => _disposed;
    public ProviderClient(string root, PluginManifest manifest)
    {
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        if (!manifest.EntryPoints.TryGetValue(rid, out var entry)) throw new PlatformNotSupportedException(Localization.LocalizationCatalog.Format("Plugins.RuntimeEntryMissing", rid));
        PluginPathPolicy.ValidateRelativePath(entry);
        var executable = Path.GetFullPath(Path.Combine(root, entry));
        if (!executable.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable)) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Plugins.EntryMissing"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        _process = new Process { StartInfo = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) } };
        _process.ErrorDataReceived += (_, _) => { }; // 持续排空标准错误，不持久化插件凭据或 URL。
        if (!_process.Start()) throw new IOException(Localization.LocalizationCatalog.Get("Plugins.ProcessStartFailed"));
        _process.BeginErrorReadLine();
    }
    public async Task<JsonElement> CallAsync(string method, Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(method.StartsWith("lyrics.", StringComparison.Ordinal) ? 50 : 20));
        await _rpcGate.WaitAsync(timeout.Token);
        try
        {
            var id = ++_sequence;
            var request = JsonSerializer.Serialize(new RpcRequest { Id = id, Method = method, Params = parameters ?? [] }, CoreJson.Rpc);
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token); await _process.StandardInput.FlushAsync(timeout.Token);
            var line = new StringBuilder(); var single = new char[1];
            while (true)
            {
                if (await _process.StandardOutput.ReadAsync(single.AsMemory(), timeout.Token) == 0) throw new IOException(Localization.LocalizationCatalog.Get("Plugins.ProcessExited"));
                if (single[0] == '\n') break;
                line.Append(single[0]); if (line.Length > 8_000_000) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Plugins.ResponseTooLarge"));
            }
            using var document = JsonDocument.Parse(line.ToString()); var response = document.RootElement;
            if (!response.TryGetProperty("jsonrpc", out var version) || version.GetString() != "2.0" || response.GetProperty("id").GetInt64() != id) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Plugins.InvalidRpc"));
            if (response.TryGetProperty("error", out _)) throw new IOException(Localization.LocalizationCatalog.Get("Plugins.ProviderFailed"));
            return response.GetProperty("result").Clone();
        }
        catch { Dispose(); throw; }
        finally { _rpcGate.Release(); }
    }

    /// <summary>按有界分页初始化当前进程的曲目目录；进程重启后不能假定旧目录仍在插件内存中。</summary>
    public async Task<IReadOnlyList<ProviderTrack>> ReadCatalogAsync(CancellationToken cancellationToken = default)
    {
        var tracks = new List<ProviderTrack>(); var cursors = new HashSet<string>(StringComparer.Ordinal); var cursor = "";
        do
        {
            if (!cursors.Add(cursor)) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Commands.PaginationStopped"));
            var result = await CallAsync("catalog.list", new() { ["cursor"] = cursor }, cancellationToken);
            var page = result.Deserialize<ProviderCatalog>(CoreJson.Options) ?? throw new InvalidDataException(Localization.LocalizationCatalog.Get("Commands.EmptyCatalog"));
            if (page.Tracks.Count > 1000 || tracks.Count + page.Tracks.Count > 50000) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Commands.CatalogTooLarge"));
            foreach (var track in page.Tracks)
                if (track.Id.Length is 0 or > 300 || track.Title.Length is 0 or > 1000 || !double.IsFinite(track.DurationSeconds) || track.DurationSeconds < 0) throw new InvalidDataException(Localization.LocalizationCatalog.Get("Commands.InvalidMetadata"));
            tracks.AddRange(page.Tracks); cursor = page.NextCursor ?? "";
        } while (cursor.Length > 0);
        CatalogInitialized = true; return tracks.DistinctBy(track => track.Id).ToArray();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try
        {
            if (!_process.HasExited) _process.Kill(true);
            // Kill 是异步请求；卸载前必须等待实际退出，否则 Windows 仍会锁住插件程序目录。
            if (!_process.WaitForExit(2000)) throw new IOException("Plugin process did not exit; package files retained");
        }
        catch (InvalidOperationException) { }
        finally { _process.Dispose(); }
    }
    /// <summary>可选析构回调最多执行两秒，失败后仍释放进程；不让不响应的插件阻止退出。</summary>
    public void NotifyLifecycle(PluginManifest manifest, string method)
    {
        if (_disposed || !manifest.LifecycleMethods.Contains(method)) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Task.Run(() => CallAsync(method, new() { ["reason"] = method[10..] }, timeout.Token)).GetAwaiter().GetResult();
        }
        catch (Exception error) { Diagnostics.AppLog.Warning("Plugins", "Plugin lifecycle callback failed: " + method, error); }
    }
}
