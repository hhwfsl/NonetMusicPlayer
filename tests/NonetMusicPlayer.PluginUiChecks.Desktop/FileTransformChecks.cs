using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>转换接口使用合成 WAV，验证先转换再导入/播放，不验收任何平台的加密格式。</summary>
internal static class FileTransformChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null) AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var manifest = new PluginManifest { Id = "fixture.file-transform", Name = "File transform", Type = "extension", Version = "1.0.0", ContractVersion = 2, Runtime = "managed", ExtensionClass = "FileTransformFixture", PageEntry = "page.json",
            ProvidedServices = [FileTransformContract.Service], FileExtensions = [".enc"], RequiredCapabilities = [FileTransformContract.Capability], Permissions = ["in-process", "user-files", "file-transform"], EntryPoints = new() { [PluginPlatformPolicy.CurrentRid] = "fixture.dll" } };
        manifest.Validate(); FileTransformContract.ValidateExtensions([".abc"]);
        var rejected = false; try { FileTransformContract.ValidateExtensions(["../bad"]); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Unsafe extension rejected");
        var package = Path.Combine(root, "fixture.impp");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var text = new StreamWriter(zip.CreateEntry("manifest.json").Open())) text.Write(JsonSerializer.Serialize(manifest));
            using (var text = new StreamWriter(zip.CreateEntry("page.json").Open())) text.Write("{\"schemaVersion\":2,\"root\":{\"type\":\"stack\"}}");
            zip.CreateEntryFromFile(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0/NonetMusicPlayer.ExtensionFixture.dll")), "fixture.dll");
        }
        var input = Path.Combine(root, "sample.enc"); File.WriteAllBytes(input, [.. "ENC00"u8.ToArray(), .. Wave()]); var original = SHA256.HashData(File.ReadAllBytes(input));
        var bad = Path.Combine(root, "bad.enc"); File.WriteAllBytes(bad, [1, 2, 3]);
        var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root, "data"))), audio);
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm }; window.Show(); Pump();
        var plugin = vm.Plugins.Install(package); plugin.ManagedExecutionConsent = true; vm.Plugins.SetEnabled(plugin, true);
        Check(vm.Plugins.InputFileExtensions.Contains(".enc"), "Picker includes enabled extension");
        var playlist = vm.CreatePlaylist("Transform fixture"); var imported = Wait(vm.ImportAsync([input, bad], playlist: playlist));
        Check(imported.Count == 1 && playlist.TrackIds.Contains(imported[0].Id) && imported[0].FilePath.EndsWith(".enc.wav", StringComparison.Ordinal), "Transform precedes metadata and playlist insertion");
        Wait(vm.PlayTemporaryFilesAsync([input])); Check(audio.Path?.EndsWith(".enc.wav", StringComparison.Ordinal) == true, "Temporary playback receives ordinary converted audio");
        Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "Input unchanged");
        vm.Plugins.SetEnabled(plugin, false); Check(!vm.Plugins.InputFileExtensions.Contains(".enc"), "Disabled plugin does not claim files");
        window.Close(); Console.WriteLine("PASS generic file transformation, source preservation, playlist insertion, temporary playback, rejection and disabled fallback");
        File.Copy(package, Path.Combine(Path.GetFullPath(output), "fixture.impp"), true); File.Copy(input, Path.Combine(Path.GetFullPath(output), "sample.enc"), true);
    }
    private static byte[] Wave()
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory); var size = 96000;
        writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]); return memory.ToArray();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static T Wait<T>(Task<T> task) { var until = DateTime.UtcNow.AddSeconds(20); while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { var until = DateTime.UtcNow.AddSeconds(20); while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } task.GetAwaiter().GetResult(); }
    private static void Check(bool value, string label) { if (!value) throw new InvalidDataException(label); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public string? Path; public bool IsAvailable => true; public bool IsPlaying { get; private set; } public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.FromSeconds(1); public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string path, CancellationToken token = default) { Path = path; return Task.CompletedTask; }
        public void Play() => IsPlaying = true; public void Pause() => IsPlaying = false; public void Stop() => IsPlaying = false; public void Dispose() { }
    }
}
