using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager
{
    private readonly SemaphoreSlim _lyricsGate = new(1);
    public static bool EmbeddingRequested(string json)
    {
        using var document = JsonDocument.Parse(json); return document.RootElement.TryGetProperty("embedLyrics", out var enabled) && enabled.ValueKind == JsonValueKind.True;
    }
    public bool NeedsAudioTagConfirmation(PluginManifest plugin, string json) => HasLyricsSource(plugin) && EmbeddingRequested(json) && !plugin.AudioTagWriteConsent;
    public bool EmbedsLyrics(PluginManifest plugin) => EmbeddingRequested(ConfigurationValues(plugin).ToJsonString());
    public IEnumerable<PluginManifest> AutomaticLyricsPlugins => Installed.Where(p => p.Enabled && HasLyricsSource(p) && ConfigurationValues(p)["autoFetch"]?.GetValue<bool>() != false).ToArray();
    public async Task<LyricsSearchResult> SearchLyricsAsync(PluginManifest plugin, LyricsQuery query, CancellationToken token = default)
    {
        var result = (await LyricsCallAsync(plugin, "lyrics.search", new() { ["query"] = JsonSerializer.Serialize(query, LyricsPluginJson.Options) }, token)).Deserialize<LyricsSearchResult>(LyricsPluginJson.Options);
        if (result?.Candidates is null || result.Warnings is null || result.Candidates.Count > 150 || result.Warnings.Count > 20
            || result.Candidates.Any(c => c is null || string.IsNullOrWhiteSpace(c.Title) || c.Title.Length > 500 || c.Artist is null || c.Artist.Length > 500 || c.Album is null || c.Album.Length > 500 || c.Id is null || c.Id.Length is 0 or > 150 || c.Token is null || c.Token.Length > 200 || c.Source is null || c.Source.Length > 50 || !double.IsFinite(c.DurationSeconds) || c.DurationSeconds is < 0 or > 86400 || !double.IsFinite(c.Score)))
            throw new InvalidDataException("Invalid lyrics candidates");
        return result;
    }
    public async Task<LyricsFetchResult> FetchLyricsAsync(PluginManifest plugin, LyricsCandidate candidate, string format, CancellationToken token = default)
        => ValidateLyrics((await LyricsCallAsync(plugin, "lyrics.fetch", new() { ["candidate"] = JsonSerializer.Serialize(candidate, LyricsPluginJson.Options), ["format"] = format }, token)).Deserialize<LyricsFetchResult>(LyricsPluginJson.Options));
    public async Task<LyricsFetchResult> MatchLyricsAsync(PluginManifest plugin, LyricsQuery query, CancellationToken token = default)
        => ValidateLyrics((await LyricsCallAsync(plugin, "lyrics.auto", new() { ["query"] = JsonSerializer.Serialize(query, LyricsPluginJson.Options) }, token)).Deserialize<LyricsFetchResult>(LyricsPluginJson.Options));
    private async Task<JsonElement> LyricsCallAsync(PluginManifest plugin, string method, Dictionary<string, string> parameters, CancellationToken token)
    {
        await _lyricsGate.WaitAsync(token);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        void CancelOnLifecycle(PluginManifest changed, string stage) { if (changed.Id == plugin.Id && stage is "disabled" or "uninstalling" or "shutdown") operation.Cancel(); }
        LifecycleChanged += CancelOnLifecycle;
        try { RequireInstalled(plugin); if (!HasLyricsSource(plugin) || !plugin.Enabled) throw new InvalidOperationException(L10n.T("Plugins.EnableThisPagePluginFirst")); if (plugin.Type == "extension") return await CallExtensionLyricsAsync(plugin, method, parameters, operation.Token); var client = await GetClientAsync(plugin, operation.Token); return await client.CallAsync(method, parameters, operation.Token); }
        finally { LifecycleChanged -= CancelOnLifecycle; _lyricsGate.Release(); }
    }
    private static LyricsFetchResult ValidateLyrics(LyricsFetchResult? result)
    {
        if (result is null || result.Format != "lrc" || Encoding.UTF8.GetByteCount(result.Text) > 2_000_000) throw new InvalidDataException("Invalid lyrics response");
        if (result.Text.Length > 0 && !LyricsService.Parse(result.Text).Any(line => line.Timed)) throw new InvalidDataException("LyricsSearch.NoMatch"); return result;
    }
    public async Task ApplyLyricsAsync(PluginManifest plugin, TrackItem track, LyricsFetchResult result, LyricsService lyrics, bool embed, CancellationToken token = default)
    {
        RequireWritable(); RequireInstalled(plugin); ValidateLyrics(result);
        if (!plugin.Enabled || !HasLyricsSource(plugin) || string.IsNullOrWhiteSpace(result.Text)) throw new InvalidOperationException(L10n.T("LyricsSearch.NoMatch"));
        if (embed)
        {
            if (!plugin.Permissions.Contains("audio-tags") || !plugin.AudioTagWriteConsent || !EmbedsLyrics(plugin)) throw new InvalidOperationException(L10n.T("LyricsSearch.ConsentRequired"));
            await Core.Library.AudioLyricsTagWriter.EmbedAsync(track.FilePath, result.Text, Path.Combine(_storage.BackupFolder, "AudioTags"), token,
                () => Installed.Contains(plugin) && plugin.Enabled && plugin.AudioTagWriteConsent && EmbedsLyrics(plugin) && !_storage.IsMigrating);
            // 删除宿主管理的旧副本，使内嵌歌词生效；从不删除用户提供的源 LRC。
            foreach (var extension in new[] { ".lrc", ".txt" }) { var file = lyrics.PathFor(track.Id, extension); if (File.Exists(file)) File.Delete(file); }
            track.LyricsSourcePath = null;
            track.FileSize = new FileInfo(track.FilePath).Length;
        }
        else { token.ThrowIfCancellationRequested(); if (!plugin.Enabled) throw new OperationCanceledException(); lyrics.Save(track.Id, result.Text); track.LyricsSourcePath = lyrics.PathFor(track.Id); }
        track.LyricsDisabled = false;
        AppLog.Info("LyricsPlugin", embed ? "Lyrics embedded; source backup retained" : "Fetched lyrics associated");
    }
}
