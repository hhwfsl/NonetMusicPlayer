namespace NonetMusicPlayer.Desktop.Services;

/// <summary>显式检查实际精简发行包，不创建界面，也不输出凭据。</summary>
internal static class ReleaseVerifier
{
    /// <summary>验证实际精简包中的歌词 RPC、配置授权及标签写入，只操作隔离目录生成的测试音频。</summary>
    public static int RunLyrics(string package, string output)
    {
        try
        {
            var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
            var storage = new AppStorage(Path.Combine(root, "data")); AppLog.Initialize(storage.Root);
            using var manager = new Plugins.PluginManager(storage);
            var plugin = manager.Install(Path.GetFullPath(package)); manager.SetEnabled(plugin, true);
            if (manager.LoadPage(plugin).Widgets is not [{ Type: "lyrics-search" }]) throw new InvalidDataException("Lyrics page contract failed.");
            var search = manager.SearchLyricsAsync(plugin, new("光の道標", "鹿乃")).GetAwaiter().GetResult();
            var candidate = search.Candidates.First(c => c.Source == "kugou");
            var result = manager.MatchLyricsAsync(plugin, new(candidate.Title, candidate.Artist, candidate.Album, candidate.DurationSeconds)).GetAwaiter().GetResult();
            if (result.Text.Length == 0 || result.WordTimed || result.Text.Contains("TME享有本翻译作品的著作权") || result.Text.Contains("[tool:LDDC"))
                throw new InvalidDataException("Lyrics default format / cleanup failed.");
            // 构造可读取标签的 MP3 帧，不使用或覆盖用户提供的音频文件。
            var frames = new byte[417 * 10];
            for (var i = 0; i < 10; i++) { frames[i * 417] = 0xff; frames[i * 417 + 1] = 0xfb; frames[i * 417 + 2] = 0x90; frames[i * 417 + 3] = 0x64; }
            var file = Path.Combine(root, "fixture.mp3"); File.WriteAllBytes(file, frames);
            var track = new Models.TrackItem("release-lyrics", candidate.Title, candidate.Artist, candidate.Album, file, ".mp3", frames.Length);
            var lyrics = new LyricsService(storage.DefaultLyricsFolder);
            manager.ApplyLyricsAsync(plugin, track, result, lyrics, false).GetAwaiter().GetResult();
            if (lyrics.Read(track.Id) != result.Text || !File.ReadAllBytes(file).SequenceEqual(frames)) throw new InvalidDataException("Lyrics association changed source audio.");
            var config = manager.ConfigurationValues(plugin); config["embedLyrics"] = true; var rejected = false;
            try { manager.Configure(plugin, config.ToJsonString()); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected || plugin.AudioTagWriteConsent) throw new InvalidDataException("Audio write confirmation bypassed.");
            manager.Configure(plugin, config.ToJsonString(), true);
            manager.ApplyLyricsAsync(plugin, track, result, lyrics, true).GetAwaiter().GetResult();
            if (lyrics.ExistingPath(track.Id) is not null || lyrics.ReadForTrack(track.Id, file) != result.Text
                || !File.ReadAllBytes(Directory.GetFiles(Path.Combine(storage.BackupFolder, "AudioTags")).Single()).SequenceEqual(frames))
                throw new InvalidDataException("Embedded lyrics / original backup failed.");
            config["embedLyrics"] = false; manager.Configure(plugin, config.ToJsonString());
            if (plugin.AudioTagWriteConsent) throw new InvalidDataException("Audio write grant was not revoked.");
            manager.SetEnabled(plugin, false); manager.Uninstall(plugin);
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nLyricsRpc=True\nDefaultLine=True\nCleanup=True\nAssociation=True\nWriteConsent=True\nEmbeddedLyrics=True\nOriginalBackup=True\nLifecycle=True\n");
            return 0;
        }
        catch (Exception e) { Directory.CreateDirectory(output); AppStorage.AtomicWrite(Path.Combine(Path.GetFullPath(output), "result.txt"), "FAIL\n" + AppLog.Redact(e.GetType().Name + ": " + e.Message)); return 1; }
        finally { AppLog.Flush(); AppLog.Shutdown(); }
    }
    public static int RunProvider(string package, string fixtureFolder, string output)
    {
        try
        {
            var storage = new AppStorage(Path.Combine(Path.GetFullPath(output), "data")); AppLog.Initialize(storage.Root); using var manager = new Plugins.PluginManager(storage);
            var manifest = manager.Install(Path.GetFullPath(package)); manager.Configure(manifest, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["fixtureFolder"] = Path.GetFullPath(fixtureFolder) }, AppStorage.Json)); manager.SetEnabled(manifest, true);
            var tracks = manager.LoadCatalogAsync(manifest, new LyricsService(storage.DefaultLyricsFolder)).GetAwaiter().GetResult();
            // 精简包也验证进程重启：无需用户重新刷新目录即可解析保存的曲目。
            manager.SetEnabled(manifest, false); manager.SetEnabled(manifest, true);
            var track = tracks.First(); var source = manager.ResolveAsync(track).GetAwaiter().GetResult();
            using (var stream = new LoopbackRangeStream(new Uri(source))) { var bytes = new byte[4]; stream.ReadExactly(bytes); if (System.Text.Encoding.ASCII.GetString(bytes) != "RIFF") throw new InvalidDataException("Unexpected provider fixture."); }
            using (var audio = new NativeAudioPlayer()) { audio.LoadAsync(source).GetAwaiter().GetResult(); if (audio.Duration.TotalSeconds <= 0) throw new InvalidDataException("Provider audio failed."); VerifyPlaybackClock(audio); audio.Position = TimeSpan.FromSeconds(2); audio.Stop(); VerifyPlaybackEnd(audio); }
            manager.SetEnabled(manifest, false); manager.Uninstall(manifest);
            AppStorage.AtomicWrite(Path.Combine(Path.GetFullPath(output), "result.txt"), "PASS\nPluginInstall=True\nJsonRpc=True\nLoopbackRange=True\nStreamingAudio=True\nNaturalPlaybackEnd=True\nDisable=True\n"); return 0;
        }
        catch (Exception e) { Directory.CreateDirectory(output); AppStorage.AtomicWrite(Path.Combine(Path.GetFullPath(output), "result.txt"), "FAIL\n" + AppLog.Redact(e.GetType().Name + ": " + e.Message)); return 1; }
        finally { AppLog.Flush(); AppLog.Shutdown(); }
    }
    public static int Run(string fixture, string output)
    {
        try
        {
            var storage = new AppStorage(Path.Combine(Path.GetFullPath(output), "data")); AppLog.Initialize(storage.Root); var scanner = new MusicLibraryScanner(storage);
            var track = scanner.ReadTrack(Path.GetFullPath(fixture));
            if (track.DurationSeconds <= 0 || track.CoverPath is null || !File.Exists(track.CoverPath)) throw new InvalidDataException("Trimmed metadata / embedded artwork failed.");
            using var audio = new NativeAudioPlayer(); audio.LoadAsync(track.FilePath).GetAwaiter().GetResult();
            if (audio.Duration.TotalSeconds <= 0) throw new InvalidDataException("Trimmed audio decoder failed.");
            if (Math.Abs(audio.Duration.TotalSeconds - track.DurationSeconds) > Math.Max(.25, track.DurationSeconds * .01)) throw new InvalidDataException("Trimmed playback duration differs from source metadata (sample-rate/channel mismatch).");
            var clock = VerifyPlaybackClock(audio);
            audio.Position = TimeSpan.FromSeconds(Math.Min(2, audio.Duration.TotalSeconds / 2)); audio.Stop();
            VerifyPlaybackEnd(audio);
            track.IsFavorite = true;
            var state = new Models.AppState { Tracks = [track], LastTrackId = track.Id, LastPosition = 1.25, LastSourcePage = "playlist:verify", LastPlaylistId = "verify", Settings = new Models.AppSettings { Volume = 37, PlayMode = Models.PlayMode.RepeatOne }, Playlists = [new Models.Playlist { Id = "verify", Name = "发行包歌单", Description = "序列化回归", CoverPath = track.CoverPath, TrackIds = [track.Id] }] }; storage.Save(state);
            var temporary = new Models.TrackItem("release-temporary", "Temporary history", track.Artist, track.Album, track.FilePath, track.Extension, track.FileSize) { CoverPath = track.CoverPath, DurationSeconds = track.DurationSeconds };
            state.History = [temporary.Id]; state.RecentTemporaryTracks = [temporary];
            state.Settings.FontFilePath = Path.Combine("Fonts", "reference.ttf");
            state.Settings.TitleBarOpacity = .2; state.Settings.NavigationOpacity = .4; state.Settings.ContentOpacity = .6; state.Settings.PlayerOpacity = .8;
            state.Settings.DesktopLyricsHeight = 180; state.Settings.DesktopLyricsWidth = 900; state.Settings.DesktopLyricsLocked = true;
            state.Settings.CloseToTray = true; state.Settings.OptimizeMemoryWhenMinimized = true;
            state.Settings.TerminalScrollbackLines = 2345; state.Settings.TerminalFontSize = 19; state.Settings.TerminalMinimumLogLevel = "WARN";
            var fontVerified = false;
            if (OperatingSystem.IsWindows())
            {
                var installedFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
                if (File.Exists(installedFont))
                {
                    // 不创建窗口或加载用户资料库，初始化发行包实际字体后端，以检出仅精简发布才出现的字体错误。
                    Program.BuildAvaloniaApp().SetupWithoutStarting();
                    state.Settings.FontFilePath = AppFontService.Import(storage, installedFont);
                    var family = AppFontService.Resolve(storage, state.Settings);
                    if (!new Avalonia.Media.Typeface(family).GlyphTypeface.FamilyName.Contains("Segoe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Trimmed font-file import / typeface resolution failed.");
                    fontVerified = true;
                }
            }
            state.SoftwareDefaultGroupCovers.Add("artist:" + track.Artist);
            state.GroupCovers["album:" + track.Album] = track.CoverPath;
            storage.Save(state);
            var restored = storage.Load();
            if (!restored.SoftwareDefaultGroupCovers.Contains("artist:" + track.Artist) || restored.GroupCovers.GetValueOrDefault("album:" + track.Album) != track.CoverPath)
                throw new InvalidDataException("Trimmed group artwork modes persistence failed.");
            if (restored.Tracks.Count != 1 || !restored.Tracks[0].IsFavorite || !restored.Playlists.Single(p => p.IsSystem).TrackIds.Contains(track.Id)
                || restored.Playlists.Single(p => p.Id == "verify").Name != "发行包歌单" || restored.Playlists.Single(p => p.Id == "verify").Description != "序列化回归"
                || restored.Playlists.Single(p => p.Id == "verify").CoverPath != track.CoverPath || restored.LastPosition != 1.25 || restored.LastSourcePage != "playlist:verify" || restored.Settings.Volume != 37)
                throw new InvalidDataException("Trimmed observable JSON persistence / playback context failed.");
            if (restored.RecentTemporaryTracks is not [var recent] || recent.Id != temporary.Id || recent.CoverPath != track.CoverPath
                || restored.Settings.FontFilePath != state.Settings.FontFilePath || restored.Settings.TitleBarOpacity != .2 || restored.Settings.NavigationOpacity != .4
                || restored.Settings.ContentOpacity != .6 || restored.Settings.PlayerOpacity != .8)
                throw new InvalidDataException("Trimmed temporary history / font preference / region opacity persistence failed.");
            if (restored.Settings.DesktopLyricsHeight != 180 || restored.Settings.DesktopLyricsWidth != 900 || !restored.Settings.DesktopLyricsLocked || !restored.Settings.CloseToTray || !restored.Settings.OptimizeMemoryWhenMinimized)
                throw new InvalidDataException("Trimmed desktop lyrics / tray / memory preference persistence failed.");
            if (restored.Settings.TerminalScrollbackLines != 2345 || restored.Settings.TerminalFontSize != 19 || restored.Settings.TerminalMinimumLogLevel != "WARN")
                throw new InvalidDataException("Trimmed terminal preferences persistence failed.");
            VerifyPluginConfiguration(storage, output);
            var layout = new UiLayoutService(storage);
            var activationId = "NonetMusicPlayer-release-check-" + Guid.NewGuid().ToString("N");
            using (var first = new SingleInstanceService(activationId))
            using (var second = new SingleInstanceService(activationId))
            {
                string[]? activated = null; first.Receive(files => activated = files);
                if (!first.IsPrimary || second.IsPrimary || !second.ForwardAsync([Path.GetFullPath(fixture)]).GetAwaiter().GetResult() || activated is not [var forwarded] || forwarded != Path.GetFullPath(fixture)) throw new InvalidDataException("Trimmed single-instance ownership / file activation failed.");
            }
            var customLayout = UiLayoutService.DefaultJson.Replace("\"fontSize\": 16", "\"fontSize\": 15");
            layout.Apply(customLayout);
            if (new UiLayoutService(storage).AppliedJson != customLayout || !layout.HasBackup) throw new InvalidDataException("Trimmed layout persistence failed.");
            var rejectedLayout = false;
            try { layout.Apply(customLayout.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99")); } catch (Models.UiLayoutException) { rejectedLayout = true; }
            if (!rejectedLayout || File.ReadAllText(layout.Path) != customLayout) throw new InvalidDataException("Trimmed layout validation failed.");
            layout.RestoreBackup(); layout.RestoreDefault();
            layout.Apply(customLayout.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1"));
            if (!layout.HasLegacyBackup || layout.Current.SchemaVersion != 2) throw new InvalidDataException("Trimmed legacy layout migration failed.");
            AppStorage.AtomicWrite(Path.Combine(Path.GetFullPath(output), "result.txt"), $"PASS\nTitle={track.Title}\nArtist={track.Artist}\nDuration={track.DurationSeconds:0.00}\nDecodedSampleRate={audio.DecodedFormat?.SampleRate}\nDecodedChannels={audio.DecodedFormat?.Channels}\nAudioClockSeconds={clock.Audio:0.000}\nWallClockSeconds={clock.Wall:0.000}\nEmbeddedCover=True\nNativeAudio=True\nNaturalPlaybackEnd=True\nJsonPersistence=True\nTemporaryHistory=True\nRegionOpacity=True\nFontFile={fontVerified}\nPluginConfigSchema=True\nPluginConfigSubstitution=True\nPluginSessionSecrets=True\nDesktopLyricsPreferences=True\nTrayMemoryPreferences=True\n");
            return 0;
        }
        catch (Exception e) { Directory.CreateDirectory(output); AppStorage.AtomicWrite(Path.Combine(Path.GetFullPath(output), "result.txt"), "FAIL\n" + AppLog.Redact(e.GetType().Name + ": " + e.Message)); return 1; }
        finally { AppLog.Flush(); AppLog.Shutdown(); }
    }
    private static void VerifyPluginConfiguration(AppStorage storage, string output)
    {
        var stage = Path.Combine(Path.GetFullPath(output), "configuration-fixture"); Directory.CreateDirectory(stage);
        AppStorage.AtomicWrite(Path.Combine(stage, "manifest.json"), """{"id":"release.schema-test","name":"Schema verification","type":"ui","version":"1.0.0","contractVersion":1,"pageEntry":"page.json","permissions":["navigation","lyrics-editor","player-control"],"menuContributions":[{"location":"lyrics.more","label":"Lyric tool","action":"open-page"}]}""");
        AppStorage.AtomicWrite(Path.Combine(stage, NonetMusicPlayer.Core.Plugins.PluginConfigSchema.FileName), """{"caption":{"type":"string","default":"Default"},"interval":{"type":"int","default":150,"min":80,"max":400},"credential":{"type":"string","default":"","sensitive":true}}""");
        AppStorage.AtomicWrite(Path.Combine(stage, "page.json"), """{"schemaVersion":1,"title":"${config.caption}","widgets":[{"type":"snake","tickMilliseconds":"${config.interval}"},{"type":"lyrics-timing"}]}""");
        var package = Path.Combine(Path.GetFullPath(output), "configuration-fixture.impp"); System.IO.Compression.ZipFile.CreateFromDirectory(stage, package);
        using var manager = new Plugins.PluginManager(storage); var plugin = manager.Install(package);
        manager.Configure(plugin, """{"caption":"Configured","interval":100,"credential":"not-for-disk"}""");
        manager.SetEnabled(plugin, true); var page = manager.LoadPage(plugin);
        if (page.Title != "Configured" || page.Widgets[0].Snake?.TickMilliseconds != 100 || plugin.Configuration.Contains("not-for-disk") || manager.ConfigurationValues(plugin)["credential"]!.GetValue<string>() != "not-for-disk")
            throw new InvalidDataException("Trimmed plugin schema / typed substitution / session secret failed.");
        var restoredManifest = System.Text.Json.JsonSerializer.Deserialize<NonetMusicPlayer.Core.Plugins.PluginManifest>(System.Text.Json.JsonSerializer.Serialize(plugin, AppStorage.Json), AppStorage.Json);
        if (restoredManifest?.MenuContributions is not [{ Location: "lyrics.more", Action: "open-page" }] || page.Widgets[1].Type != "lyrics-timing")
            throw new InvalidDataException("Trimmed lyric menu contribution / editor contract failed.");
        var previous = plugin.Configuration; var rejected = false;
        try { manager.Configure(plugin, """{"caption":"Bad","interval":1,"credential":""}"""); } catch (InvalidDataException) { rejected = true; }
        if (!rejected || previous != plugin.Configuration) throw new InvalidDataException("Trimmed plugin configuration rollback failed.");
        manager.SetEnabled(plugin, false); manager.Uninstall(plugin);
    }
    private static (double Audio, double Wall) VerifyPlaybackClock(NativeAudioPlayer audio)
    {
        if (audio.Duration.TotalSeconds < 1) throw new InvalidDataException("The release verification fixture must contain at least one second of audio.");
        audio.Volume = 0; audio.Position = TimeSpan.Zero;
        audio.Play(); var baseline = audio.Position.TotalSeconds; var clock = System.Diagnostics.Stopwatch.StartNew();
        Thread.Sleep(450); audio.Pause(); var wall = clock.Elapsed.TotalSeconds; var advanced = audio.Position.TotalSeconds - baseline;
        if (Math.Abs(advanced - wall) > .14) throw new InvalidDataException($"Playback clock mismatch: {advanced:0.000} audio seconds / {wall:0.000} wall seconds.");
        return (advanced, wall);
    }
    private static void VerifyPlaybackEnd(NativeAudioPlayer audio)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var count = 0;
        EventHandler ended = (_, _) => { Interlocked.Increment(ref count); completed.TrySetResult(true); };
        EventHandler<AudioPlaybackErrorEventArgs> failed = (_, error) => completed.TrySetException(new InvalidDataException(error.ErrorCode + ": " + error.Message));
        audio.PlaybackStopped += ended; audio.PlaybackFailed += failed;
        try
        {
            // 短音频完整播放；长音频显式验证最后一秒。
            audio.Position = audio.Duration.TotalSeconds <= 10 ? TimeSpan.Zero : audio.Duration - TimeSpan.FromSeconds(1);
            audio.Play();
            if (!completed.Task.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Trimmed native playback did not report natural completion.");
            completed.Task.GetAwaiter().GetResult(); if (count != 1) throw new InvalidDataException("Duplicate playback completion events.");
            var buffers = audio.BufferSnapshot;
            if (buffers.ActiveSources != 1 || buffers.LoadedSources - buffers.ReleasedSources != 1 || buffers.MaxDecodeSamples > 1024 * audio.DecodedFormat!.Value.Channels) throw new InvalidDataException("Trimmed decoder lifetime / bounded block regression.");
            audio.Stop();
        }
        finally { audio.PlaybackStopped -= ended; audio.PlaybackFailed -= failed; }
    }
}
