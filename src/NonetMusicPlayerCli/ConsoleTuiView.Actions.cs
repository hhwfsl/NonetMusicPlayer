using System.Globalization;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Runtime;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Persistence;
using NonetMusicPlayer.Core.Plugins;

namespace NonetMusicPlayerCli;

internal sealed partial class ConsoleTuiView
{
    private Task? EditForm(ConsoleKeyInfo key, CancellationToken token)
    {
        var form = _form!; var field = form.Current;
        if (key.Key == ConsoleKey.Escape) { _form = null; return null; }
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.N && form.Fields.LastOrDefault()?.Repeatable == true)
        {
            var last = form.Fields[^1]; form.Fields.Add(new(last.Name) { Repeatable = true, Optional = true }); form.Selected = form.Fields.Count - 1; form.Caret = 0; return null;
        }
        if (key.Key == ConsoleKey.Tab || key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            var direction = key.Key == ConsoleKey.UpArrow || key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
            form.Selected = (form.Selected + direction + form.Fields.Count) % Math.Max(1, form.Fields.Count); form.Caret = form.Current?.Value.Length ?? 0; return null;
        }
        if (key.Key == ConsoleKey.Enter)
        {
            if (!key.Modifiers.HasFlag(ConsoleModifiers.Control) && form.Selected < form.Fields.Count - 1) { form.Selected++; form.Caret = form.Current!.Value.Length; return null; }
            try { var command = form.Build(); _form = null; return Execute(command, token, true); }
            catch (Exception error) { form.Error = error.Message; return null; }
        }
        if (field is null) return null;
        if (field.IsChoice)
        {
            if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Spacebar) field.Change(key.Key == ConsoleKey.LeftArrow ? -1 : 1);
        }
        else { var caret = form.Caret; field.Value = EditText(key, field.Value, ref caret); form.Caret = caret; }
        return null;
    }
    private IReadOnlyList<TuiItem> Items() => _view is null ? [] : Page switch
    {
        "Music" => _view.Music, "Playlists" => _playlist is not null ? _view.PlaylistTracks.GetValueOrDefault(_playlist) ?? [] : _view.Playlists,
        "Recent" => _view.Recent, "Plugins" => _view.Plugins,
        "Help" => HelpSections.Select(key => new TuiItem(key, T(key), "")).ToArray(),
        "Settings" => _view.Settings.Select(pair => new TuiItem(pair.Key, SettingLabel(pair.Key), pair.Value is JsonValue value && value.TryGetValue<string>(out var text) ? text == "Playback.SystemDefault" ? LocalizationCatalog.Get(text) : text : pair.Value?.ToJsonString() ?? "")).ToArray(), _ => []
    };
    private string Initial(string name)
    {
        var item = Items().ElementAtOrDefault(_selected);
        return name switch
        {
            "playlist-id" => _playlist ?? (Page == "Playlists" ? item?.Id : _view?.Playlists.FirstOrDefault()?.Id) ?? "",
            "track-id" or "track-id..." => Page is "Music" or "Recent" || _playlist is not null ? item?.Id ?? "" : _view?.CurrentId ?? "",
            "id" => Page == "Plugins" ? item?.Id ?? "" : _playlist ?? (Page == "Playlists" ? item?.Id : _view?.Playlists.FirstOrDefault()?.Id) ?? "",
            "yyyy-MM-dd" => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "day|month|year" => "day", "keep-files|delete-files" => "keep-files", "1-based-position" => "1",
            "repeat-all|repeat-one|shuffle" => "repeat-all", _ => ""
        };
    }
    private IReadOnlyList<string> Choices(string name) => name switch
    {
        "playlist-id" => _view?.Playlists.Select(p => p.Id).ToArray() ?? [],
        "track-id" => _view?.Music.Select(p => p.Id).ToArray() ?? [], _ => []
    };
    private Task? BeginNamedAction(string name, CancellationToken token) => BeginAction(Actions.Single(d => d.Name == name), token);
    private Task? BeginAction(CommandDefinition command, CancellationToken token)
    {
        var form = TuiForm.FromCommand(command, Initial, Choices);
        if (form.Fields.Count == 0) return Execute(CommandSyntax.PublicName(command.Name), token, true);
        _form = form; form.Caret = form.Current?.Value.Length ?? 0; return null;
    }
    private void OpenSetting(string key, JsonNode? current)
    {
        var field = TuiField.Setting(key, current); var isString = current is JsonValue scalar && scalar.TryGetValue<string>(out _);
        _form = new(SettingLabel(key), [field], fields => "settings set " + TuiForm.Quote(key) + " " + TuiForm.Quote(isString ? JsonValue.Create(fields[0].Value)!.ToJsonString() : JsonNode.Parse(fields[0].Value)!.ToJsonString()));
        _form.Caret = field.Value.Length;
    }
    private static string SettingLabel(string name) => T("Setting" + char.ToUpperInvariant(name[0]) + name[1..]);
    private async Task SelectDevice(CancellationToken token)
    {
        _executing = true;
        try
        {
            var result = await router.ExecuteAsync("player devices", cancellationToken: token);
            if (!result.Success) { ShowResult(result); return; }
            var choices = result.Data?.AsArray().Select(node => node!.GetValue<string>()).ToArray() ?? [];
            if (choices.Length == 0) { ShowResult(result); return; }
            var current = _view?.Settings["deviceName"]?.GetValue<string>() ?? choices[0];
            _form = new(SettingLabel("deviceName"), [new TuiField("device", choices.Contains(current) ? current : choices[0], choices)], fields => "player device -- " + TuiForm.Quote(fields[0].Value));
        }
        finally { _executing = false; }
    }
    private async Task Execute(string command, CancellationToken token, bool showResult = false)
    {
        _executing = true;
        try
        {
            var result = await router.ExecuteAsync(command, definition =>
            {
                _confirmationText = CommandSyntax.PublicName(definition.Name);
                var answer = _confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return ConfirmAsync(answer, token);
            }, token);
            _notice = result.Message;
            if (showResult || !result.Success) ShowResult(result);
        }
        finally { _executing = false; _confirmation = null; }
    }
    private async Task<bool> ConfirmAsync(TaskCompletionSource<bool> answer, CancellationToken token)
    {
        try { return await answer.Task.WaitAsync(token); } finally { if (ReferenceEquals(_confirmation, answer)) _confirmation = null; }
    }
    private void ShowResult(CommandResult result)
    {
        var text = result.Message + (result.Data is null ? "" : "\n" + result.Data.ToJsonString(CoreJson.Readable));
        _result = text.Replace("\r", "").Split('\n'); _resultOffset = 0;
    }
    private async Task ConfigurePlugin(string id, CancellationToken token)
    {
        _executing = true;
        try
        {
            var schemaResult = await router.ExecuteAsync("plugins schema " + TuiForm.Quote(id), cancellationToken: token);
            if (!schemaResult.Success) { ShowResult(schemaResult); return; }
            var configResult = await router.ExecuteAsync("plugins config " + TuiForm.Quote(id), cancellationToken: token);
            var schema = schemaResult.Data as JsonObject; var config = configResult.Data as JsonObject;
            if (schema is null || schema.Count == 0) { _notice = T("NoConfiguration"); return; }
            var values = PluginConfigSchema.Resolve(schema, config?.ToJsonString() ?? "{}");
            var fields = schema.Select(pair =>
            {
                var definition = pair.Value!.AsObject(); var choices = definition["enum"] as JsonArray;
                var field = TuiField.Setting(pair.Key, values[pair.Key]);
                return new TuiField(pair.Key, field.Value, choices is null ? field.Choices : choices.Select(c => c is JsonObject obj ? obj["value"]! : c!).Select(c => c is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : c.ToJsonString()).ToArray())
                { Private = definition["sensitive"]?.GetValue<bool>() == true || definition["ui:widget"]?.GetValue<string>() == "password", Label = definition["description"]?.GetValue<string>() ?? pair.Key };
            }).ToArray();
            _form = new(T("PluginConfiguration"), fields, edited =>
            {
                var result = new JsonObject();
                foreach (var field in edited) result[field.Name] = PluginConfigSchema.Type(schema[field.Name]!.AsObject()) is "string" or "text" ? JsonValue.Create(field.Value) : JsonNode.Parse(field.Value);
                PluginConfigSchema.Validate(schema, result);
                return "plugins config " + TuiForm.Quote(id) + " " + TuiForm.Quote(result.ToJsonString());
            });
            _form.Caret = _form.Current?.Value.Length ?? 0;
        }
        finally { _executing = false; }
    }
}
