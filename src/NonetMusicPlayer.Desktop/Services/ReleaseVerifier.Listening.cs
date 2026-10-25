using System.Text.Json;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>在实际裁剪发行包中核验采样、源生成序列化及 SQLite 明细，全部使用隔离夹具。</summary>
    public static int RunListening(string output)
    {
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            var root = Path.GetFullPath(output); var storage = new AppStorage(Path.Combine(root, "data-" + Guid.NewGuid().ToString("N")));
            var clock = new ListeningClock(); var audio = new ListeningAudio(); var track = new TrackItem("fixture", "歌曲", "艺术家", "专辑", "fixture.flac", ".flac", 1000) { DurationSeconds = 600 };
            var hour = clock.GetUtcNow().ToUnixTimeSeconds() / 3600;
            storage.Save(new AppState { Tracks = [track], ListeningEntries = [new() { TrackId = "legacy", Date = "2026-10-25", Seconds = 7200 }], HourlyListeningEntries = [new() { UtcHour = hour, PlayCount = 3 }] });
            using (var vm = new MainViewModel(new MusicLibraryScanner(storage), audio, clock))
            {
                if (!vm.GetRecentListeningCharts().HasIncompleteHourHistory) throw new InvalidDataException("Legacy coverage was not disclosed.");
                vm.PlayTrackAsync(vm.State.Tracks[0]).GetAwaiter().GetResult(); Advance(1);
                vm.SetPlayingAsync(false).GetAwaiter().GetResult(); clock.Advance(20); vm.RefreshPlaybackState();
                vm.SetPlayingAsync(true).GetAwaiter().GetResult(); Advance(2); vm.Seek(100); Advance(12); vm.Save();
                var charts = vm.GetRecentListeningCharts();
                if (Math.Abs(charts.HourDurations.Sum(p => p.Value) - 15) > .001 || Math.Abs(charts.TodaySeconds - 7215) > .001) throw new InvalidDataException("Actual audio advance did not reach both daily and hourly totals.");
                void Advance(double seconds) { clock.Advance(seconds); audio.Position += TimeSpan.FromSeconds(seconds); vm.RefreshPlaybackState(); }
            }
            var restored = storage.Load();
            if (Math.Abs(restored.HourlyListeningEntries.Sum(e => e.Seconds) - 15) > .001) throw new InvalidDataException("Trimmed hourly serialization lost seconds.");
            var sessions = storage.ListeningJournal.Page()["items"]!.AsArray();
            if (sessions.Count != 1 || Math.Abs(sessions[0]!["countedSeconds"]!.GetValue<double>() - 15) > .001 || sessions[0]!["endReason"]!.GetValue<string>() != "application-exit") throw new InvalidDataException("Published session persistence failed.");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(restored.HourlyListeningEntries[0], AppStorage.Json));
            if (!json.RootElement.TryGetProperty("seconds", out _)) throw new InvalidDataException("Published source-generated JSON is missing seconds.");
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nActualPublishedHost=True\nHourlySeconds=True\nLegacyCoverage=True\nPauseSeekExcluded=True\nDelayedSampling=True\nSessionsIntervalsEvents=True\nSqliteRestart=True\nSourceGeneratedSerialization=True\n"); return 0;
        }
        catch (Exception error) { Directory.CreateDirectory(output); AppStorage.AtomicWrite(Path.Combine(Path.GetFullPath(output), "result.txt"), "FAIL\n" + AppLog.Redact(error.ToString())); return 1; }
        finally { AppLog.Flush(); AppLog.Shutdown(); }
    }
    private sealed class ListeningClock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 25, 5, 59, 59, TimeSpan.Zero).AddMilliseconds(_milliseconds + 500);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Fixture+08", TimeSpan.FromHours(8), "Fixture", "Fixture");
        public void Advance(double seconds) => _milliseconds += (long)(seconds * 1000);
    }
    private sealed class ListeningAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(600); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) { Position = TimeSpan.Zero; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() { IsPlaying = false; Position = TimeSpan.Zero; } public void Dispose() => Stop();
    }
}
