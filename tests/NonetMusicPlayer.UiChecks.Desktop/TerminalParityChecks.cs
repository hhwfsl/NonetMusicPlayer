using Avalonia.Controls;
using Avalonia.Threading;
using System.IO.Compression;
using System.Text.Json;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>终端命令操作实际桌面视图模型，验证模式切换、队列和设置不会分叉。</summary>
internal static class TerminalParityChecks
{
    public static void Run(string output)
    {
        output = Path.Combine(output, "case-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var storage = new AppStorage(Path.Combine(output, "terminal-" + Guid.NewGuid().ToString("N")));
        using var audio = new ApplicationIntegrationChecks.SilentAudioPlayer();
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio); vm.Settings.CloseToTray = vm.Settings.ConfirmClose = false;
        var tracks = Enumerable.Range(0, 8).Select(i => new TrackItem("terminal-" + i, "歌曲 " + i, "艺术家", "专辑", "fixture-" + i, ".wav", 1) { DurationSeconds = 263 }).ToArray();
        vm.State.Tracks.AddRange(tracks); var playlist = vm.CreatePlaylist("命令歌单"); vm.AddToPlaylist(playlist, tracks); vm.Navigate("playlist:" + playlist.Id);
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump(150);
        CommandResult Run(string command)
        {
            var task = window.Commands.ExecuteAsync(command); while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            var result = task.GetAwaiter().GetResult(); Require(result.Success, command + ": " + result.Message); return result;
        }
        Run("queue play " + playlist.Id + " " + tracks[2].Id); Run("player seek 42"); var current = vm.CurrentTrack;
        var original = vm.PlaybackQueue.Select(t => t.Id).ToArray(); Run("queue move " + original[0] + " 5");
        Require(vm.PlaybackQueue[4].Id == original[0] && vm.CurrentTrack == current && audio.Position.TotalSeconds == 42 && playlist.TrackIds.SequenceEqual(original), "Queue command edits live order without source or transport changes");
        Run("queue show"); Require(window.IsPlaybackQueueOpen, "Queue show opens actual popover"); Run("queue toggle"); Require(!window.IsPlaybackQueueOpen, "Toggle closes popover"); Run("queue show"); Run("queue hide"); Require(!window.IsPlaybackQueueOpen, "Hide closes popover");
        Run("player mode shuffle"); Run("queue select " + tracks[4].Id); Require(vm.PlaybackQueue[0].Id == tracks[4].Id, "Terminal manual selection regenerates shuffle");
        Run("settings set playMode 1"); Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(playlist.TrackIds), "Settings command updates queue when leaving shuffle");
        Require(Run("playlist tracks " + playlist.Id).Data!.AsArray().Count == 8, "List playlist contents");
        Require(Run("album list").Data!.AsArray().Count == 1 && Run("artist tracks '艺术家'").Data!.AsArray().Count == 8, "Album and artist browsing");
        Run("music sources"); Require(Run("music query local '歌曲 3'").Data!["items"]!.AsArray().Count == 1, "Terminal local source search returns current result snapshot"); Run("music more"); Run("music results");
        Require(Run("statistics hours").Data!.AsArray().Count == 24, "Hourly listening query"); Run("statistics history");
        var lyric = Path.Combine(output, "terminal.lrc"); File.WriteAllText(lyric, "[00:01.00]第一行\n[00:02.00]第二行"); Run("lyrics import " + Quote(lyric));
        Require(Run("lyrics reload").Data!.AsArray().Count == 2, "Reload uses actual lyric parser"); Run("lyrics export " + Quote(Path.Combine(output, "terminal-export.lrc"))); Run("lyrics clear -y"); Require(vm.CurrentTrack!.LyricsDisabled && vm.LyricLines.Count == 0 && File.Exists(lyric), "Terminal lyric unlink preserves source files");
        Run("lyrics import " + Quote(lyric)); Require(!vm.CurrentTrack.LyricsDisabled, "Reimport re-enables lyrics");
        Run("playlist export " + Quote(Path.Combine(output, "terminal.m3u")) + " " + playlist.Id);
        Require(File.ReadAllText(Path.Combine(output, "terminal.m3u")).Contains("fixture-0"), "Explicit playlist export is independent of current filtered page");
        Run("layout edit true"); Run("layout edit false"); Run("window default-size"); Run("network status"); Run("network peers"); Run("network requests");
        var manifest = new PluginManifest { Id = "fixture.terminal-command", Name = "Nested command", Version = "1.0.0", ContractVersion = 2, Type = "extension", Runtime = "declarative", PageEntry = "page.json", Permissions = ["player-control"], RequiredCapabilities = ["runtime.declarative.v1", "commands.v1"] };
        var package = Path.Combine(output, "nested-command.impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, AppStorage.Json));
            using (var writer = new StreamWriter(zip.CreateEntry("page.json").Open())) writer.Write("""{"schemaVersion":2,"root":{"type":"text","text":"Fixture"},"actions":{"volume":{"service":"commands","arguments":{"operation":"player.volume","arguments":["37"]}}}}""");
        }
        var plugin = vm.Plugins.Install(package); vm.Plugins.SetEnabled(plugin, true);
        var nested = window.Commands.ExecuteAsync("plugins invoke " + plugin.Id + " volume -y"); var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!nested.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Require(nested.IsCompleted && nested.Result.Success && vm.Volume == 37, "Terminal extension action can call a validated nested command without waiting on its own router lock");
        Run("player stop"); Require(!vm.IsPlaying && audio.Position == TimeSpan.Zero, "Stop command uses desktop stop behavior");
        var unknown = window.Commands.ExecuteAsync("unknown command"); while (!unknown.IsCompleted) Dispatcher.UIThread.RunJobs(); Require(unknown.Result.Operation == "command.unknown", "Unknown command output preserved");
        window.Close(); Pump(50);
        Console.WriteLine("PASS desktop terminal parity: live queue, popup, mode/settings, search, album/artist, lyrics, stats, export, layout and window defaults.");
    }
    private static string Quote(string text) => "'" + text + "'";
    private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    private static void Pump(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds); while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
    }
}
