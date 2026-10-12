using Avalonia.Threading;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>正式精简包的只读联网探针；数据位于显式隔离目录，不改用户资料库。</summary>
    public static int RunMusicSource(string package,string query,string output)
    {
        var root=Path.GetFullPath(output);Directory.CreateDirectory(root);
        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            var storage=new AppStorage(Path.Combine(root,"data"));
            using var audio=new NativeAudioPlayer();using var vm=new MainViewModel(new MusicLibraryScanner(storage),audio);
            vm.Settings.CloseToTray=false;vm.Settings.ConfirmClose=false;vm.Volume=0;
            var window=new MainWindow { DataContext=vm };
            var plugin=vm.Plugins.Install(Path.GetFullPath(package));vm.Plugins.SetEnabled(plugin,true);
            Wait(vm.SearchMusicAsync(plugin.Id,query));
            Wait(vm.LoadMusicSearchArtworkAsync(vm.VisibleTracks.Take(12)));
            var track=vm.VisibleTracks.FirstOrDefault()??throw new InvalidDataException("No music source results.");
            if(vm.State.Tracks.Count!=0)throw new InvalidDataException("Search incorrectly stored tracks.");
            var searchArtwork=track.HasArtwork;
            var list=vm.CreatePlaylist("Verification cloud tracks");vm.AddToPlaylist(list,[track]);
            Wait(vm.PlayTrackAsync(track,[track]));if(!vm.IsPlaying||audio.Duration.TotalSeconds<=1)throw new InvalidDataException("Source playback failed.");
            if(audio.CloudBufferBytes<=0||audio.CloudBufferBytes>NonetMusicPlayer.Core.Audio.BufferedPcmDataProvider.MaximumBufferBytes)throw new InvalidDataException("Published cloud PCM buffer unavailable or unbounded.");
            VerifyPlaybackClock(audio);audio.Position=TimeSpan.FromSeconds(2);
            if(Math.Abs(audio.Position.TotalSeconds-2)>.3)throw new InvalidDataException("Streaming seek failed.");
            vm.Save();var persisted=storage.Load().Tracks.Single();
            if(persisted.ProviderId!=plugin.Id||persisted.ProviderTrackId!=track.ProviderTrackId||persisted.FilePath.Length!=0)throw new InvalidDataException("Stable cloud identity failed.");
            vm.DisablePlugin(plugin);vm.Plugins.SetEnabled(plugin,true);
            var resolved=Wait(vm.Plugins.ResolveAsync(persisted));using(var stream=new LoopbackRangeStream(new Uri(resolved))) { var bytes=new byte[32];stream.ReadExactly(bytes);stream.Seek(1024,SeekOrigin.Begin);stream.ReadExactly(bytes); }
            vm.DisablePlugin(plugin);window.Close();
            AppStorage.AtomicWrite(Path.Combine(root,"result.txt"),"PASS\nPublishedHost=True\nSearch=True\nSearchArtwork="+searchArtwork+"\nStableCloudPlaylist=True\nArtwork="+File.Exists(track.CoverPath)+"\nLyrics="+(vm.Lyrics.ExistingPath(track.Id)is not null)+"\nStreamingDecode=True\nSeek=True\nProcessRestart=True\n");return 0;
        }
        catch(Exception error){AppStorage.AtomicWrite(Path.Combine(root,"result.txt"),"FAIL\n"+AppLog.Redact(error.GetType().Name+": "+error.Message));return 1;}
    }
    private static T Wait<T>(Task<T> task){while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}return task.GetAwaiter().GetResult();}
    private static void Wait(Task task){while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}task.GetAwaiter().GetResult();}
}
