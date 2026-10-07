using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>通用进程扩展会话。页面只订阅状态，导航离开不销毁后台任务。</summary>
public sealed partial class ExtensionSession : IDisposable
{
    private readonly PluginManager _manager;
    public PluginManifest Manifest { get; }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1);
    private ProviderClient? _process;
    private ManagedExtensionClient? _managed;
    private INonetExtension? _declarative;
    private bool _initialized, _polling, _disposed, _gesture;
    private readonly HashSet<string> _handled = [];
    private static readonly AsyncLocal<string[]?> ServiceStack = new();
    private static readonly AsyncLocal<bool> DecisionScope = new();
    public Dictionary<string, JsonNode?> Inputs { get; } = [];
    public ExtensionFrame Frame { get; private set; } = new();
    public event Action<ExtensionFrame>? Changed;
    public bool IsDisposed => _disposed;
    public ExtensionSession(PluginManager manager, PluginManifest manifest) { _manager = manager; Manifest = manifest; }
    private MainWindow Owner => _manager.ExtensionHost ?? throw new InvalidOperationException("Host unavailable.");
    public async Task StartAsync()
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            if (_initialized) return;
            if (Manifest.Runtime == "declarative")
            {
                _declarative = new DeclarativeExtension(_manager.LoadExtensionPage(Manifest));
                Frame = await _declarative.InitializeAsync(new(2, _manager.ConfigurationValues(Manifest).ToJsonString(), _manager.ExtensionStorage(Manifest), ExtensionContract.Capabilities), _lifetime.Token);
                _initialized = true;
            }
            else if (Manifest.Runtime == "managed")
            {
                if (!Manifest.ManagedExecutionConsent) throw new InvalidDataException("Explicit managed consent required.");
                var path = Path.Combine(_manager.ExtensionDirectory(Manifest), Manifest.EntryPoints[PluginPlatformPolicy.CurrentRid]);
                PluginPathPolicy.RejectLinkedAncestors(path); _managed = new(path, Manifest.ExtensionClass);
                var initialization = new ExtensionInitialization(2, _manager.ConfigurationValues(Manifest).ToJsonString(),
                    _manager.ExtensionStorage(Manifest), ExtensionContract.Capabilities);
                Frame = await Task.Run(async () => await _managed.Instance.InitializeAsync(initialization, _lifetime.Token), _lifetime.Token);
                ExtensionContract.ValidateFrame(Frame); _initialized = true;
            }
            else
            {
            _process = new ProviderClient(_manager.ExtensionDirectory(Manifest), Manifest);
            var hello = await _process.CallAsync("initialize", new() { ["contractVersion"] = "2",
                ["configuration"] = _manager.ConfigurationValues(Manifest).ToJsonString(), ["storageDirectory"] = _manager.ExtensionStorage(Manifest),
                ["capabilities"] = JsonSerializer.Serialize(ExtensionContract.Capabilities) }, _lifetime.Token);
            if (hello.GetProperty("contractVersion").GetInt32() != 2) throw new InvalidDataException("Invalid handshake.");
            var capabilities = hello.GetProperty("capabilities").EnumerateArray().Select(v => v.GetString()).ToHashSet();
            if (Manifest.RequiredCapabilities.Any(c => !capabilities.Contains(c))) throw new InvalidDataException("Missing negotiated capability.");
            Frame = await CallAsync("extension.sync"); ExtensionContract.ValidateFrame(Frame); _initialized = true;
            }
        }
        catch { Dispose(); throw; }
        finally { _gate.Release(); }
        await PublishAsync(Frame);
    }
    public async Task InvokeAsync(string action, JsonObject values, bool userGesture = false)
    {
        await StartAsync(); await _gate.WaitAsync(_lifetime.Token);
        try
        {
            _gesture = userGesture;
            await PublishAsync(await CallAsync("extension.invoke", new() { ["invocation"] = JsonSerializer.Serialize(new ExtensionInvocation(action, values), ExtensionJson.Default.ExtensionInvocation) }));
        }
        finally { _gesture = false; _gate.Release(); }
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ExtensionEvent> _pendingEvents = new();
    private int _eventPump;
    public async Task EventAsync(ExtensionEvent value)
    {
        if (_disposed || !UniversalExtensionContract.MatchesEvent(Manifest.Events, value.Name)) return;
        // 同名事件只保留最新值，慢进程不会积累几千条进度或界面更新。
        if (_pendingEvents.Count >= 64 && !_pendingEvents.ContainsKey(value.Name)) return;
        _pendingEvents[value.Name] = value;
        if (Interlocked.CompareExchange(ref _eventPump, 1, 0) != 0) return;
        try
        {
            await StartAsync();
            while (!_disposed && _pendingEvents.Keys.FirstOrDefault() is { } key)
            {
                if (!_pendingEvents.TryRemove(key, out var next)) continue;
                await _gate.WaitAsync(_lifetime.Token);
                try { await PublishAsync(await CallAsync("extension.event", new() { ["event"] = JsonSerializer.Serialize(next, ExtensionJson.Default.ExtensionEvent) })); }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AppLog.Warning("Extensions", "Extension event failed: " + Manifest.Id, error); }
        finally
        {
            Interlocked.Exchange(ref _eventPump, 0);
            if (!_disposed && _pendingEvents.Values.FirstOrDefault() is { } pending) _ = EventAsync(pending);
        }
    }
    private async Task<ExtensionFrame> CallAsync(string method, Dictionary<string, string>? args = null)
        {
        var instance = _declarative ?? _managed?.Instance;
        if (instance is null) return (await _process!.CallAsync(method, args, _lifetime.Token)).Deserialize(ExtensionJson.Default.ExtensionFrame) ?? throw new InvalidDataException("Empty frame.");
        return await Task.Run(async () => method switch
        {
            "extension.invoke" => await instance.InvokeAsync(JsonSerializer.Deserialize(args!["invocation"], ExtensionJson.Default.ExtensionInvocation)!, _lifetime.Token),
            "extension.event" => await instance.EventAsync(JsonSerializer.Deserialize(args!["event"], ExtensionJson.Default.ExtensionEvent)!, _lifetime.Token),
            "extension.complete" => await instance.CompleteAsync(args!["id"], JsonNode.Parse(args["result"])!.AsObject(), _lifetime.Token),
            _ => await instance.SyncAsync(_lifetime.Token)
        }, _lifetime.Token);
    }
    private async Task PublishAsync(ExtensionFrame frame)
    {
        ExtensionContract.ValidateFrame(frame);
        // 请求 ID 去重，重发状态不会重复执行事务。
        for (var round = 0; frame.Requests.Count > 0 && round < 16; round++)
        {
            var fresh = frame.Requests.FirstOrDefault(r => !_handled.Contains(r.Id)); if (fresh is null) break;
            if (_handled.Count > 4096) throw new InvalidDataException("Too many requests."); _handled.Add(fresh.Id);
            JsonObject result;
            try { result = await ServiceAsync(fresh); }
            catch (Exception error) when (error is not OperationCanceledException)
            { AppLog.Warning("Extensions", "Service rejected: " + fresh.Service, error); result = new() { ["success"] = false, ["message"] = "Request rejected." }; }
            frame = await CallAsync("extension.complete", new() { ["id"] = fresh.Id, ["result"] = result.ToJsonString() });
            ExtensionContract.ValidateFrame(frame);
        }
        Frame = frame; Changed?.Invoke(frame);
        if (frame.Busy && !_polling) { _polling = true; _ = PollAsync(); }
    }
    private async Task PollAsync()
    {
        try
        {
            while (!_disposed && Frame.Busy)
            {
                await Task.Delay(200, _lifetime.Token); await _gate.WaitAsync(_lifetime.Token);
                try { await PublishAsync(await CallAsync("extension.sync")); } finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Dispose(); _manager.ReportExtensionError(error); }
        finally { _polling = false; }
    }
    private async Task<JsonObject> ServiceAsync(ExtensionHostRequest request)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ServiceAsync(request));
        if (DecisionScope.Value) return new() { ["success"] = false, ["reason"] = "Host requests are not permitted during workflow decisions." };
        var prior = Caller.Value; Caller.Value = Manifest.Id;
        try { return UniversalExtensionContract.IsService(request.Service) ? await UniversalServiceAsync(request) : await LegacyServiceAsync(request); }
        finally { Caller.Value = prior; }
    }
    private async Task<JsonObject> LegacyServiceAsync(ExtensionHostRequest request)
    {
        var args = request.Arguments;
        if (request.Service == "ui") return await UiServiceAsync(args);
        if (request.Service == "development") return await DevelopmentServiceAsync(args);
        if (request.Service == "dialogs.prompt")
        {
            if (!_gesture) throw new InvalidDataException("An input dialog requires a user gesture.");
            var text = args["text"]?.GetValue<string>() ?? ""; var initial = args["initial"]?.GetValue<string>() ?? "";
            if (text.Length > 4000 || initial.Length > 200) throw new InvalidDataException("Dialog input too long.");
            var value = await PlayerDialog.Prompt(Owner, Manifest.Name, text, initial);
            if (value?.Length > 200) return new() { ["success"] = false };
            return new() { ["success"] = value is not null, ["value"] = value };
        }
        if (request.Service is "dialogs.confirm" or "dialogs.notify")
        {
            var text = args["text"]?.GetValue<string>() ?? "";
            if (text.Length > 4000) throw new InvalidDataException("Dialog text too long.");
            if (request.Service == "dialogs.notify") { _manager.ExtensionViewModel.ReportWarning(Manifest.Name + " · " + text); return new() { ["success"] = true }; }
            if (!_gesture) throw new InvalidDataException("A confirmation dialog requires a user gesture.");
            return new() { ["success"] = await PlayerDialog.Confirm(Owner, Manifest.Name, text, L10n.T("Common.Confirm")) };
        }
        if (request.Service == "lyrics")
        {
            var vm = _manager.ExtensionViewModel;
            var track = vm.State.Tracks.Concat(vm.State.RecentTemporaryTracks).FirstOrDefault(t => t.Id == args["trackId"]?.GetValue<string>()) ?? throw new InvalidDataException("Unknown track ID.");
            var operation = args["operation"]?.GetValue<string>() ?? "read";
            if (operation == "parse")
            {
                if (!Manifest.Permissions.Contains("music-read")) throw new InvalidDataException("Music read permission required.");
                var lines = Services.LyricsService.Parse(args["text"]?.GetValue<string>() ?? vm.Lyrics.ReadForTrack(track.Id, track.FilePath));
                // 未标注结束时间的逐字片段使用 null，不将内部 NaN 发到 JSON 协议。
                JsonArray Words(IReadOnlyList<Services.LyricWord> words) => new(words.Select(w => (JsonNode?)new JsonObject { ["seconds"] = w.Seconds, ["text"] = w.Text, ["endSeconds"] = double.IsFinite(w.EndSeconds) ? JsonValue.Create(w.EndSeconds) : null }).ToArray());
                return new() { ["success"] = true, ["lines"] = new JsonArray(lines.Select(l => (JsonNode?)new JsonObject { ["seconds"] = l.Seconds, ["text"] = l.Text, ["translation"] = l.Translation, ["timed"] = l.Timed, ["words"] = Words(l.Words), ["translationWords"] = Words(l.TranslationWords) }).ToArray()) };
            }
            if (operation == "read")
            {
                if (!Manifest.Permissions.Contains("music-read")) throw new InvalidDataException("Music read permission required.");
                return new() { ["success"] = true, ["text"] = track.LyricsDisabled ? "" : vm.Lyrics.ReadForTrack(track.Id, track.FilePath), ["disabled"] = track.LyricsDisabled };
            }
            if (!Manifest.Permissions.Contains("lyrics-write") || track.LyricsDisabled && !_gesture) throw new InvalidDataException("Lyrics write permission required; unlinked lyrics cannot be automatically replaced.");
            if (operation == "unlink") track.LyricsDisabled = true;
            else if (operation == "associate")
            {
                var text = args["text"]?.GetValue<string>() ?? "";
                if (System.Text.Encoding.UTF8.GetByteCount(text) > 2_000_000 || text.Length == 0) throw new InvalidDataException("Invalid lyrics text.");
                vm.Lyrics.Save(track.Id, text); track.LyricsSourcePath = vm.Lyrics.PathFor(track.Id); track.LyricsDisabled = false;
            }
            else throw new InvalidDataException("Unknown lyrics operation.");
            if (vm.CurrentTrack?.Id == track.Id) vm.ReloadLyrics(); vm.Save(); return new() { ["success"] = true };
        }
        if (request.Service == "catalog") return ExtensionCatalog();
        if (request.Service == "config")
        {
            if (!_gesture) throw new InvalidDataException("Configuration requires a user gesture.");
            await Owner.OpenPluginConfigurationAsync(Manifest); return new() { ["success"] = true };
        }
        if (request.Service == "files.pick")
        {
            if (!_gesture || !Manifest.Permissions.Contains("user-files")) throw new InvalidDataException("User-selected file permission required.");
            var files = await Owner.OpenFilesAsync(L10n.T("Common.Choose"), ["*"], true);
            if (files.Length == 0) return new() { ["success"] = true, ["files"] = new JsonArray() };
            if (files.Length > 8 || files.Sum(f => new FileInfo(f).Length) > 20_000_000 || files.Any(f => new FileInfo(f).Length > 10_000_000))
                throw new InvalidDataException("Select up to 8 files, 10 MB per file and 20 MB total.");
            if (!await PlayerDialog.Confirm(Owner, L10n.T("Extensions.UploadTitle"), L10n.Format("Extensions.UploadConfirm", Manifest.Name,
                string.Join("\n", files.Select(Path.GetFileName))), L10n.T("Common.Confirm"))) return new() { ["success"] = false };
            var selected = new JsonArray();
            foreach (var file in files)
            {
                // 只读本次选择，不返回路径或删除能力；接收插件必须可信。
                var bytes = await File.ReadAllBytesAsync(file, _lifetime.Token);
                if (bytes.Length > 10_000_000) throw new InvalidDataException("Selected file changed size.");
                selected.Add(new JsonObject { ["name"] = Path.GetFileName(file), ["data"] = Convert.ToBase64String(bytes) });
            }
            return new() { ["success"] = true, ["files"] = selected };
        }
        if (request.Service.StartsWith("plugin:", StringComparison.Ordinal))
        {
            if (!Manifest.Permissions.Contains("plugin-services")) throw new InvalidDataException("Plugin service permission required.");
            var parts = request.Service[7..].Split('/');
            if (parts.Length != 2) throw new InvalidDataException("Invalid plugin service.");
            var target = _manager.Installed.FirstOrDefault(p => p.Id == parts[0] && p.Enabled && p.Type == "extension");
            if (parts.Length != 2 || target is null || target.Id == Manifest.Id || !target.ProvidedServices.Contains(parts[1])) throw new InvalidDataException("Service unavailable.");
            var prior = ServiceStack.Value ?? [];
            if (prior.Length >= 4 || prior.Contains(target.Id)) throw new InvalidDataException("Cyclic plugin service request.");
            ServiceStack.Value = [.. prior, Manifest.Id, target.Id];
            try
            {
                var targetSession = _manager.Extension(target);
                await targetSession.InvokeAsync("service:" + parts[1], (JsonObject)args.DeepClone());
                return new() { ["success"] = true, ["data"] = targetSession.Frame.State["serviceResult"]?.DeepClone() };
            }
            finally { ServiceStack.Value = prior; }
        }
        if (request.Service == "commands")
        {
            var operation = args["operation"]!.GetValue<string>();
            var arguments = args["arguments"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();
            var tracks = _manager.ExtensionTracks;
            if (operation is "player.play" or "music.info" or "favorite.add" or "favorite.remove" or "music.remove")
                foreach (var id in arguments) if (!tracks.Contains(id)) throw new InvalidDataException("Track ID required; paths are not allowed.");
            if (operation is "playlist.add" or "playlist.remove" or "playlist.move")
                foreach (var id in arguments.Skip(1).Take(operation == "playlist.move" ? 1 : int.MaxValue))
                    if (!tracks.Contains(id)) throw new InvalidDataException("Track ID required.");
            var encoded = UniversalPluginCommandPolicy.ValidateFor(new(request.Id, operation, arguments), Manifest.Permissions);
            if (PluginApprovalPolicy.RequiresConfirmation(Manifest.ApprovalMode, operation, PluginCommandPolicy.RequiresConfirmation(operation)) && !await PlayerDialog.Confirm(Owner, L10n.T("Agent.Confirm"), encoded, L10n.T("Common.Confirm")))
                return new() { ["success"] = false, ["reason"] = "User declined. Do not retry." };
            _lifetime.Token.ThrowIfCancellationRequested();
            var result = await Owner.Commands.ExecuteAsync(encoded, _ => Task.FromResult(true), _lifetime.Token);
            return new() { ["success"] = result.Success, ["data"] = PluginCommandPolicy.Sanitize(result.Data) };
        }
        throw new InvalidDataException("Service is not enabled.");
    }
    private async Task DisposeManagedAsync(string reason) { try { await ReleaseAudioAsync(); await _managed!.Instance.EventAsync(new(reason, new()), CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2)); await _managed.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception error) { AppLog.Warning("Extensions", "Managed cleanup failed.", error); } }
    public void Dispose() => Stop("lifecycle.shutdown");
    public void Stop(string reason)
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel();
        if (_process is not null) { _process.NotifyLifecycle(Manifest, reason); _process.Dispose(); }
        if (_managed is not null) _ = DisposeManagedAsync(reason);
        if (_declarative is not null) _ = _declarative.DisposeAsync();
    }
}
