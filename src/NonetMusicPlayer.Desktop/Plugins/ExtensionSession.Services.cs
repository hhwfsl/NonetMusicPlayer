using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>所有扩展形态复用同一服务入口与权限检查；不接受反射或任意宿主方法名。</summary>
public sealed partial class ExtensionSession
{
    internal static readonly AsyncLocal<string?> Caller = new();
    private ExtensionStorageService? _values;
    private ExtensionPcmLease? _pcm;
    private ViewModels.MainViewModel? _audioOwner;
    private readonly Queue<long> _publishedEvents = new();
    private bool PermitPublishedEvent()
    {
        var now = Environment.TickCount64;
        while (_publishedEvents.TryPeek(out var time) && now - time >= 1000) _publishedEvents.Dequeue();
        // 自定义事件限制为每插件每秒二十条，阻止互相回传造成无界事件风暴。
        if (_publishedEvents.Count >= 20) return false;
        _publishedEvents.Enqueue(now); return true;
    }
    internal async Task AttachAudioAsync()
    {
        if (Manifest.Runtime != "managed" || !Manifest.ManagedExecutionConsent || !Manifest.Permissions.Contains("audio-processing")) return;
        await StartAsync();
        if (_disposed || !Manifest.Enabled) return;
        if (_managed?.Instance is not INonetAudioExtension) throw new InvalidDataException("Declared audio factory is missing.");
        if (_pcm is null && _managed?.Instance is INonetAudioExtension factory)
        {
            _pcm = new(factory.CreateProcessor()); _audioOwner = _manager.ExtensionViewModel;
            await Dispatcher.UIThread.InvokeAsync(() => _audioOwner.SetExtensionAudio(Manifest.Id, _pcm));
        }
    }
    internal async Task ReleaseAudioAsync()
    {
        if (_pcm is null) return;
        var lease = _pcm; lease.Revoke();
        if (_audioOwner is { } owner) await Dispatcher.UIThread.InvokeAsync(() => owner.SetExtensionAudio(Manifest.Id, null, lease));
        await lease.ReleaseAsync(); _pcm = null; _audioOwner = null;
    }
    private async Task<JsonObject> UniversalServiceAsync(ExtensionHostRequest request)
    {
        var args = request.Arguments;
        switch (request.Service)
        {
            case "artwork": return await ArtworkServiceAsync(args);
            case "maintenance":
                var action = args["operation"]?.GetValue<string>() ?? "";
                if (action == "software-check" && Manifest.Permissions.Contains("settings-write")) await Owner.CheckUpdatesAsync(_gesture);
                else if (action == "plugins-check" && Manifest.Permissions.Contains("plugins-control")) await Owner.CheckPluginUpdatesAsync();
                else throw new InvalidDataException("Maintenance permission denied or unsupported operation.");
                return new() { ["success"] = true, ["version"] = Owner.VersionNumber };
            case "metadata": return await MetadataServiceAsync(args);
            case "files": return await FilesServiceAsync(args);
            case "host":
                return new() { ["success"] = true, ["contractVersion"] = 2,
                    ["capabilities"] = Strings(ExtensionContract.Capabilities), ["slots"] = Strings(ExtensionContract.Slots),
                    ["hooks"] = Strings(UniversalExtensionContract.Hooks), ["services"] = Strings(["commands", "catalog", "config", "files.pick", "lyrics", "dialogs.confirm", "dialogs.notify", "dialogs.prompt", "ui", "development", .. UniversalExtensionContract.Services]),
                    ["commands"] = ExtensionCatalog(), ["pluginId"] = Manifest.Id,
                    ["state"] = Manifest.Permissions.Contains("music-read") ? HostState() : new JsonObject { ["page"] = _manager.ExtensionViewModel.Page } };
            case "query":
                var operation = args["operation"]?.GetValue<string>() ?? "status";
                // 只读查询采用与终端相同的业务源，但先分页再脱敏，避免完整音乐库进入进程帧。
                if (operation is not ("status" or "music.list" or "music.info" or "playlist.list" or "queue.list" or "history.list" or "statistics" or "player.devices" or "settings.list" or "settings.get" or "plugins.list" or "selection.list"))
                    throw new InvalidDataException("Not a query operation.");
                var arguments = (args["arguments"] as JsonArray ?? []).Select(a => a!.GetValue<string>()).ToArray();
                var command = UniversalPluginCommandPolicy.ValidateFor(new(request.Id, operation, arguments), Manifest.Permissions);
                var offset = args["offset"]?.GetValue<int>() ?? 0; var limit = args["limit"]?.GetValue<int>() ?? 100;
                if (offset < 0 || limit is < 1 or > 100) throw new InvalidDataException("Invalid query page.");
                if (PagedQuery(operation, arguments, offset, limit) is { } page) return page;
                var result = await Owner.Commands.ExecuteAsync(command, cancellationToken: _lifetime.Token);
                var total = result.Data is JsonArray list ? list.Count : 1;
                var data = result.Data is JsonArray array ? new JsonArray(array.Skip(offset).Take(limit).Select(a => a?.DeepClone()).ToArray()) : result.Data;
                return new() { ["success"] = result.Success, ["data"] = PluginCommandPolicy.Sanitize(data), ["total"] = total, ["offset"] = offset, ["limit"] = limit };
            case "storage":
                return (_values ??= new ExtensionStorageService(_manager.ExtensionStorage(Manifest))).Execute(args);
            case "log":
                var text = args["message"]?.GetValue<string>() ?? "";
                if (text.Length is 0 or > 4000) throw new InvalidDataException("Invalid log message.");
                text = AppLog.Redact(text); var category = "Plugin:" + Manifest.Id;
                switch (args["level"]?.GetValue<string>() ?? "info")
                {
                    case "info": AppLog.Info(category, text); break;
                    case "warning": AppLog.Warning(category, text); break;
                    case "error": AppLog.Error(category, text); break;
                    default: throw new InvalidDataException("Invalid log level.");
                }
                return new() { ["success"] = true };
            case "events":
                if (!Manifest.Permissions.Contains("plugin-services")) throw new InvalidDataException("Plugin service permission required.");
                var name = args["name"]?.GetValue<string>() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9.-]{0,60}$")) throw new InvalidDataException("Invalid event name.");
                if (!PermitPublishedEvent()) return new() { ["success"] = false, ["reason"] = "Event rate exceeded." };
                var payload = args["data"] as JsonObject ?? new();
                _manager.BroadcastExtensionEvent("plugin:" + Manifest.Id + "." + name, payload, Manifest.Id);
                return new() { ["success"] = true };
        }
        throw new InvalidDataException("Unknown service.");
    }
    private JsonObject? PagedQuery(string operation, string[] arguments, int offset, int limit)
    {
        var vm = _manager.ExtensionViewModel;
        IEnumerable<JsonNode?>? items = null;
        JsonObject Track(Models.TrackItem track) => new() { ["id"] = track.Id, ["title"] = track.Title, ["artist"] = track.Artist, ["album"] = track.Album, ["duration"] = track.DurationSeconds, ["favorite"] = track.IsFavorite };
        // 先跳过并取页，再创建 JSON；大型列表不再先序列化整库又丢弃绝大部分对象。
        if (operation == "music.list")
        {
            var tracks = vm.State.Tracks.Where(t => arguments.Length == 0 || (t.Title + " " + t.Artist + " " + t.Album).Contains(arguments[0], StringComparison.CurrentCultureIgnoreCase));
            var total = tracks.Count(); return Result(tracks.Skip(offset).Take(limit).Select(t => (JsonNode?)Track(t)), total);
        }
        if (operation == "queue.list") items = vm.PlaybackQueue.Select(t => (JsonNode?)Track(t));
        if (operation == "playlist.list") items = vm.Playlists.Select(p => (JsonNode?)new JsonObject { ["id"] = p.Id, ["name"] = p.IsSystem ? L10n.T("Playlists.LikedSongs") : p.Name, ["description"] = p.Description, ["count"] = p.TrackIds.Count });
        if (operation == "history.list") items = vm.State.History.Select(id => (JsonNode?)JsonValue.Create(id));
        if (items is null) return null;
        return Result(items.Skip(offset).Take(limit), operation == "queue.list" ? vm.PlaybackQueue.Count : operation == "playlist.list" ? vm.Playlists.Count : vm.State.History.Count);
        JsonObject Result(IEnumerable<JsonNode?> page, int total) => new() { ["success"] = true, ["data"] = new JsonArray(page.ToArray()), ["total"] = total, ["offset"] = offset, ["limit"] = limit };
    }
    internal void RefreshHostPresentation() { if (_initialized && !_disposed) Changed?.Invoke(Frame); }
    internal JsonObject HostState()
    {
        var vm = _manager.ExtensionViewModel;
        return new() { ["trackId"] = vm.CurrentTrack?.Id, ["title"] = vm.CurrentTrack?.Title, ["artist"] = vm.CurrentTrack?.Artist,
            ["version"] = Owner.VersionNumber, ["playing"] = vm.IsPlaying, ["position"] = vm.PlaybackPosition, ["duration"] = vm.PlaybackDuration,
            ["volume"] = vm.Volume, ["mode"] = vm.Settings.PlayMode.ToString(), ["page"] = vm.Page,
            ["source"] = vm.PlayingSourcePage, ["favorite"] = vm.CurrentTrack?.IsFavorite,
            ["language"] = vm.Settings.Language, ["theme"] = vm.Settings.Theme, ["accent"] = vm.Settings.Accent,
            ["effectiveTheme"] = Owner.ActualThemeVariant.ToString(), ["windowWidth"] = Owner.Bounds.Width, ["windowHeight"] = Owner.Bounds.Height, ["renderScaling"] = Owner.RenderScaling,
            ["windowState"] = Owner.WindowState.ToString(), ["visible"] = Owner.IsVisible };
    }
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
    internal async Task<ExtensionHookDecision> EvaluateAsync(ExtensionHook hook, CancellationToken cancellationToken)
    {
        var priorDecision = DecisionScope.Value; DecisionScope.Value = true;
        try { await StartAsync(); } catch { DecisionScope.Value = priorDecision; throw; }
        var entered = false;
        try
        {
            await _gate.WaitAsync(cancellationToken); entered = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
            ExtensionHookDecision decision;
            if (_managed?.Instance is INonetWorkflowExtension workflow)
                decision = await Task.Run(async () => await workflow.EvaluateAsync(hook, timeout.Token), timeout.Token).WaitAsync(timeout.Token);
            else if (_process is not null)
                decision = (await _process.CallAsync("extension.hook", new() { ["hook"] = System.Text.Json.JsonSerializer.Serialize(hook, UniversalExtensionJson.Default.ExtensionHook) }, timeout.Token))
                    .Deserialize(UniversalExtensionJson.Default.ExtensionHookDecision) ?? throw new InvalidDataException("Empty hook result.");
            else throw new InvalidDataException("Declared workflow interface is not implemented.");
            UniversalExtensionContract.ValidateDecision(decision);
            var mutableField = hook.Name == "navigation.before" ? "page" : hook.Name == "playback.before" ? "trackId" : null;
            foreach (var pair in decision.Data)
                if (pair.Key != mutableField || pair.Value is not JsonValue scalar || !scalar.TryGetValue<string>(out var value) || value.Length is 0 or > 500 || value.Any(char.IsControl))
                    throw new InvalidDataException("Invalid workflow decision field.");
            return decision;
        }
        finally { DecisionScope.Value = priorDecision; if (entered) _gate.Release(); }
    }
    internal async Task<JsonElement> InvokeProvidedServiceAsync(string name, JsonObject arguments, CancellationToken token)
    {
        if (!Manifest.ProvidedServices.Contains(name)) throw new InvalidDataException("Service not declared.");
        // 当前插件请求播放自己的音源时，串行 RPC 已返回，无需再次等待自己的会话锁。
        var reentrant = Caller.Value == Manifest.Id && _initialized;
        if (!reentrant) { await StartAsync(); await _gate.WaitAsync(token); }
        try
        {
            var frame = await CallAsync("extension.invoke", new() { ["invocation"] = JsonSerializer.Serialize(new ExtensionInvocation("service:" + name, arguments), ExtensionJson.Default.ExtensionInvocation) });
            ExtensionContract.ValidateFrame(frame);
            if (frame.Requests.Count > 0) throw new InvalidDataException("Media service results cannot recursively request host transactions.");
            using var document = JsonDocument.Parse(frame.State["serviceResult"]?.ToJsonString() ?? "{}");
            return document.RootElement.Clone();
        }
        finally { if (!reentrant) _gate.Release(); }
    }
    private async Task<JsonObject> RequestNativeServiceAsync(ExtensionHostRequest request, bool userGesture, CancellationToken token)
    {
        ExtensionContract.ValidateFrame(new() { Requests = [request] });
        await _gate.WaitAsync(token);
        try { _gesture = userGesture; token.ThrowIfCancellationRequested(); _lifetime.Token.ThrowIfCancellationRequested(); return await ServiceAsync(request); }
        finally { _gesture = false; _gate.Release(); }
    }
    internal async Task<INonetNativeView> CreateNativeViewAsync(string slot)
    {
        if (Manifest.Runtime != "managed" || !Manifest.ManagedExecutionConsent || !Manifest.Permissions.Contains("native-ui") || !Manifest.Permissions.Contains("ui-extend"))
            throw new InvalidDataException("Native view permission denied.");
        await StartAsync();
        if (_managed?.Instance is not INonetNativeViewExtension factory) throw new InvalidDataException("Native view factory is missing.");
        return await Dispatcher.UIThread.InvokeAsync(() => factory.CreateView(new(slot, Manifest.Permissions.Contains("music-read") ? HostState() : new JsonObject { ["page"] = _manager.ExtensionViewModel.Page },
            RequestNativeServiceAsync,
            (action, values) => InvokeAsync(action, values, userGesture: true))));
    }
}
