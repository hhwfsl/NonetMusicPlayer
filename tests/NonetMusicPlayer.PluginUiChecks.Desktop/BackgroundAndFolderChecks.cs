using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class BackgroundAndFolderChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.GetFullPath(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var storage = new AppStorage(Path.Combine(root, "data"));
        var png = Path.Combine(root, "background.png");
        using (var image = new RenderTargetBitmap(new PixelSize(48, 48))) image.Save(png, PngBitmapEncoderOptions.Default);
        var imported = AppBackgroundService.Import(storage, png);
        Require(imported != png && File.Exists(png), "Static import preserves source.");
        var gif = Path.Combine(root, "animation.gif"); File.WriteAllBytes(gif, Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        Reject(() => AppBackgroundService.Import(storage, gif));
        var video = Path.Combine(root, "video.mp4"); File.WriteAllText(video, "not a supported background"); Reject(() => AppBackgroundService.ValidateSource(video));
        Require(!AppBackgroundService.MediaPatterns.Any(p => p is "*.gif" or "*.mp4"), "Picker offers static formats only.");
        var install = Path.Combine(root, "installation"); var retired = Path.Combine(install, "VideoDecoder"); Directory.CreateDirectory(retired);
        foreach (var file in new[] { "ffmpeg.exe", "ffmpeg", "LICENSE.txt", "NOTICE.txt" }) File.WriteAllText(Path.Combine(retired, file), "retired fixture");
        var unknown = Path.Combine(retired, "keep.txt"); File.WriteAllText(unknown, "unknown file");
        var audioLibrary = Path.Combine(install, "soundflow-ffmpeg.dll"); File.WriteAllText(audioLibrary, "audio fixture");
        Require(RetiredBackgroundCleanup.Run(install) && File.Exists(unknown) && File.Exists(audioLibrary) && !File.Exists(Path.Combine(retired,"ffmpeg.exe")), "Cleanup removes only retired decoder, preserving unknown files and audio library.");
        File.Delete(unknown); Require(RetiredBackgroundCleanup.Run(install) && !Directory.Exists(retired), "Empty retired folder removed without recursive deletion.");
        var state = new AppState(); state.Settings.BackgroundImagePath = video; state.Settings.Accent = "#507EC0"; storage.Save(state);
        using var audio = new FakeAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        Require(vm.Settings.BackgroundImagePath is null && vm.Settings.Accent == "#507EC0" && File.Exists(video), "Retired setting safely disabled without changing other settings or source.");
        vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 760 }; window.Show(); Pump();
        vm.Settings.BackgroundImagePath = imported; vm.ApplySettings();
        var surface = window.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "AppBackgroundImage");
        Require(surface.Source is not null && AppBackgroundService.DecodedBytes is > 0 and <= 4L*1600*1600, "Static Image surface and bounded cache retained.");
        var folderA = Path.Combine(root,"歌单 A"); var folderB = Path.Combine(root,"Playlist B"); var nested=Path.Combine(folderA,"nested"); var empty=Path.Combine(root,"Empty");
        Directory.CreateDirectory(nested); Directory.CreateDirectory(folderB); Directory.CreateDirectory(empty);
        Wave(Path.Combine(folderA,"a.wav")); Wave(Path.Combine(nested,"b.wav")); Wave(Path.Combine(folderB,"c.wav"));
        File.WriteAllText(Path.Combine(folderA,"a.lrc"),"[00:00.000]fixture lyrics",new UTF8Encoding(false));
        var created = Wait(vm.CreatePlaylistsFromFoldersAsync([folderA,folderB,empty,folderA]));
        Require(created.Count == 3 && created[0].TrackIds.Count == 2 && created[1].TrackIds.Count == 1 && created[2].TrackIds.Count == 0 && vm.State.Tracks.Count == 3, "Folder playlist feature retained.");
        Require(vm.State.Tracks.Any(t => vm.Lyrics.Read(t.Id)?.Contains("fixture lyrics") == true), "Companion lyrics retained.");
        var prompt = PlayerDialog.CreatePlaylist(window); Pump(); var dialog=window.OwnedWindows.OfType<PlayerDialog>().Single();
        dialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="CreatePlaylistFromFolders").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
        Require(prompt.IsCompleted && prompt.Result is { FromFolders:true }, "Folder dialog retained.");
        vm.Navigate("settings"); Pump(); Require(!vm.Settings.StartOnLogin, "Startup remains off by default.");
        using(var frame=window.CaptureRenderedFrame()!) frame.Save(Path.Combine(root,"settings.png"),PngBitmapEncoderOptions.Default);
        window.Close(); Require(File.Exists(png) && File.Exists(gif), "Sources preserved.");
        Console.WriteLine("PASS static backgrounds, rejected animation/video, retired decoder cleanup, settings migration, bounded cache, folder playlists and startup defaults.");
    }
    private static void Reject(Action action) { try { action(); throw new Exception("Unsupported background accepted."); } catch (InvalidDataException) { } }
    private static void Wave(string file)
    {
        using var writer=new BinaryWriter(File.Create(file)); const int count=16000;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(count+36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count); writer.Write(new byte[count]);
    }
    private static void Pump()=>Dispatcher.UIThread.RunJobs();
    private static T Wait<T>(Task<T> task) { var until=DateTime.UtcNow.AddSeconds(20); while(!task.IsCompleted&&DateTime.UtcNow<until) { Pump(); Thread.Sleep(5); } Require(task.IsCompleted,"UI timeout"); return task.GetAwaiter().GetResult(); }
    private static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
    private sealed class FakeAudio:IAudioPlayer
    {
        public bool IsAvailable=>true; public bool IsPlaying=>false; public TimeSpan Position{get;set;} public TimeSpan Duration=>TimeSpan.FromSeconds(1); public float Volume{get;set;}
        public event EventHandler? PlaybackStopped { add{} remove{} }
        public Task LoadAsync(string source,CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public void Play(){} public void Pause(){} public void Stop(){} public void Dispose(){}
    }
}
