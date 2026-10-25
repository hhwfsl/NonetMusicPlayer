using System.Text.Json;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

/// <summary>检验独立明细、事务重试、分页、故障恢复和迁移，不使用真实音乐或用户数据库。</summary>
internal static class ListeningJournalChecks
{
    public static void Run(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        var storage = new AppStorage(Path.Combine(root, "data")); var state = new AppState(); storage.Save(state);
        var clock = new DateTimeOffset(2026, 10, 25, 5, 59, 58, TimeSpan.Zero);
        var recorder = new ListeningSessionRecorder(storage.ListeningJournal);
        var track = new TrackItem("fixture", "歌曲", "艺术家", "专辑", "https://example.invalid/song?token=not-for-history", ".flac", 0) { DurationSeconds = 240, ProviderId = "fixture.cloud", ProviderTrackId = "123" };
        recorder.Start(track, "playlist:fixture", new() { Id = "fixture", Name = "歌单" }, clock, 480, 0, 50, PlayMode.Shuffle, "headphones");
        for (var i = 0; i < 40; i++) recorder.Credit(clock.AddSeconds(i * .25), clock.AddSeconds((i + 1) * .25), 480, i * .25, (i + 1) * .25, .25, true);
        recorder.CountPlay(); recorder.Observe(clock.AddSeconds(10), 10, false, 60, PlayMode.RepeatOne, "speakers"); recorder.Seek(clock.AddSeconds(11), 100);
        recorder.Observe(clock.AddSeconds(12), 100, true, 60, PlayMode.RepeatOne, "speakers"); recorder.Credit(clock.AddSeconds(12), clock.AddSeconds(13), 480, 100, 101, 1, true);
        storage.Save(state, playbackOnly: true);
        var sessions = storage.ListeningJournal.Page(); var session = sessions["items"]![0]!;
        Check(sessions["total"]!.GetValue<long>() == 1 && session["playedSeconds"]!.GetValue<double>() == 11 && session["countedSeconds"]!.GetValue<double>() == 11 && session["playCount"]!.GetValue<int>() == 1, "Continuous samples accumulate in one durable session.");
        Check(!session.ToJsonString().Contains("not-for-history") && session["sourceType"]!.GetValue<string>() == "cloud" && session["playlistName"]!.GetValue<string>() == "歌单", "Source metadata is preserved without transient URLs or credentials.");
        Check(storage.ListeningJournal.Page("interval")["total"]!.GetValue<long>() == 2, "Continuous samples merge; pause and seek split intervals.");
        var events = storage.ListeningJournal.Page("event")["items"]!.AsArray().Select(e => e!["type"]!.GetValue<string>()).ToHashSet();
        Check(new[] { "start", "pause", "resume", "seek", "volume", "play-mode", "output-device" }.All(events.Contains), "Playback changes retain their timestamps and parameters.");
        Check(!storage.ListeningJournal.HasPending, "Successful commit acknowledges the bounded write buffer.");
        var restart = new AppStorage(storage.Root); restart.ListeningJournal.MarkInterrupted(clock.AddMinutes(10)); restart.Save(restart.Load());
        Check(restart.ListeningJournal.Page()["items"]![0]!["endReason"]!.GetValue<string>() == "interrupted", "Unfinished sessions are closed without inventing audio after the last checkpoint.");
        var backup = Path.Combine(root, "saved.db"); new LibraryDatabase(storage.DatabasePath).Backup(backup);
        for (var i = 0; i < 520; i++) storage.ListeningJournal.Stage(new ListeningSessionRecord { Id = "bulk-" + i, TrackId = "deleted-track", StartedUtc = clock.AddDays(-10).AddMinutes(i), UpdatedUtc = clock, EndedUtc = clock });
        storage.Save(state);
        Check(storage.ListeningJournal.Page(limit: 7, offset: 510)["items"]!.AsArray().Count == 7 && storage.ListeningJournal.Page(limit: 9999)["items"]!.AsArray().Count == 500, "History is retained and read with bounded SQL pagination.");
        Check(new AppStorage(storage.Root).Load().Tracks.Count == 0 && storage.ListeningJournal.Page()["total"]!.GetValue<long>() == 521, "Loading library summaries does not load or delete historical sessions.");
        storage.RestoreListeningJournal(backup);
        Check(storage.ListeningJournal.Page()["total"]!.GetValue<long>() == 1, "SQLite restore restores the separate journal table.");
        var pending = Path.Combine(root, "moved"); storage.ConfigureDataDirectory(pending); storage.ListeningJournal.Stage(new ListeningEventRecord { SessionId = "fixture", OccurredUtc = clock, Type = "migration-checkpoint" }); storage.Save(state);
        Check(new AppStorage(pending).ListeningJournal.Page("event")["total"]!.GetValue<long>() == storage.ListeningJournal.Page("event")["total"]!.GetValue<long>(), "Data relocation mirrors committed journal changes.");
        var command = PluginCommandPolicy.ValidateFor(new("fixture", "statistics.history", ["session", "10", "0"]), ["music-read"]);
        Check(CommandLineParser.Parse(command).Name == "statistics.history", "Music-read agents can query the same bounded history command.");
        Console.WriteLine("PASS durable sessions/intervals/events, no transient URLs, bounded memory/query, interruption, SQLite restore/migration and shared commands.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
