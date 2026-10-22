using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Network;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

/// <summary>真实 ViewModel 验证最后一次播放请求获胜，不依赖私人插件或用户音频。</summary>
internal static class PlaybackSwitchChecks
{
    public static void Run(string output)
    {
        AppBuilder.Configure<NonetMusicPlayer.Desktop.App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions {UseHeadlessDrawing=false}).SetupWithoutStarting();
        var root=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using var audio=new SwitchingAudio();using var vm=new MainViewModel(new MusicLibraryScanner(new AppStorage(Path.Combine(root,"data"))),audio);
        var tracks=new[] {Song("slow"),Song("middle"),Song("last")};var notices=0;vm.UserNotification+=(_,_)=>notices++;
        var first=vm.PlayTrackAsync(tracks[0],tracks);PumpUntil(()=>audio.Loads.Count>0);
        var clock=Stopwatch.StartNew();var second=vm.PlayRelativeAsync(1);var third=vm.PlayRelativeAsync(1);
        Wait(Task.WhenAll(first,second,third));
        Check(clock.ElapsedMilliseconds<1500 && vm.CurrentTrack?.Id=="last" && vm.IsPlaying,"Rapid next must select the last requested song without waiting for the old network timeout.");
        Check(audio.Cancelled==1 && audio.Loads.SequenceEqual(["slow","last"]) && notices==0,"Cancelled and queued requests must not play, skip songs, or report errors.");
        var id=Guid.NewGuid().ToString("N");using var cert=LanLibraryServer.Certificate(Path.Combine(root,"phone.pfx"));LanLibraryClient? reverse=null;LanBlackoutRelay? reverseRelay=null;
        using var server=new LanLibraryServer(cert,id,"Slow optional assets",()=>new(id,"Phone","one",[new("s","Remote","Fixture","",60000,true,true)],[new("p","List","",["s"])],1,"mobile"),async(_,_)=>{await Task.Delay(2500);return null;},(_,_)=>Task.FromResult(true));
        server.AcceptReverse=async(access,token)=>{reverseRelay=new(access.Origin.Port);reverse=await LanLibraryClient.FromApprovedAccessAsync(access with {Origin=new UriBuilder(access.Origin){Port=reverseRelay.Port}.Uri},token);return true;};server.Start();
        using var relay=new LanBlackoutRelay(server.Port);
        Wait(vm.ConnectLanAsync(server.Invitation("127.0.0.1").Replace("port="+server.Port,"port="+relay.Port)));vm.Navigate("lan:"+id+":p");
        clock.Restart();Wait(vm.PlayTrackAsync(vm.VisibleTracks.Single(),vm.VisibleTracks.ToArray()));
        Check(vm.IsPlaying && clock.ElapsedMilliseconds<1000,"Slow cover/lyrics must not delay audio start.");
        Wait(vm.PlayTrackAsync(tracks[2],tracks));Check(vm.CurrentTrack?.Id=="last" && vm.IsPlaying,"Late assets cannot replace the new selected track.");
        var remote=vm.VisibleTracks.Single();Wait(vm.PlayTrackAsync(remote,[remote]));audio.Position=TimeSpan.FromSeconds(17);vm.RefreshPlaybackState();
        relay.Blocked=true;reverseRelay!.Blocked=true;
        PumpUntil(()=>!vm.LanPeers.Single().Reachable,30000);
        Check(vm.LanPeers.Count==1 && vm.VisibleTracks.Count==1 && !vm.IsPlaying && Math.Abs(vm.PlaybackPosition-17)<.1,"Unavailable transport keeps playlists, pauses and retains progress.");
        relay.Blocked=false;reverseRelay.Blocked=false;
        PumpUntil(()=>vm.IsPlaying,30000);
        Check(vm.CurrentTrack?.Id==remote.Id && Math.Abs(vm.PlaybackPosition-17)<.1,"Transport recovery resumes the same song from its retained position.");
        reverse?.Dispose();reverseRelay.Dispose();Console.WriteLine("PASS playback requests: cancellation, rapid next ordering, last-wins selection, no cancellation errors, nonblocking assets and retained-position network recovery.");
    }
    private static TrackItem Song(string id)=>new(id,id,"Fixture","",id,".wav",0){DurationSeconds=60};
    private static void PumpUntil(Func<bool> condition,int timeout=10000) {var until=Environment.TickCount64+timeout;while(!condition()){if(Environment.TickCount64>until)throw new TimeoutException();Dispatcher.UIThread.RunJobs();Thread.Sleep(5);}}
    private static void Wait(Task task){PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
    private sealed class SwitchingAudio:IAudioPlayer
    {
        public List<string> Loads {get;}=[];public int Cancelled;
        public bool IsAvailable=>true;public bool IsPlaying {get;private set;}public TimeSpan Position {get;set;}public TimeSpan Duration=>TimeSpan.FromSeconds(60);public float Volume {get;set;}
        public event EventHandler? PlaybackStopped {add{}remove{}}
        public async Task LoadAsync(string source,CancellationToken cancellationToken=default){Loads.Add(source);if(source=="slow")try{await Task.Delay(Timeout.Infinite,cancellationToken);}catch(OperationCanceledException){Cancelled++;throw;}}
        public void Play()=>IsPlaying=true;public void Pause()=>IsPlaying=false;public void Stop()=>IsPlaying=false;public void Dispose(){}
    }
}
