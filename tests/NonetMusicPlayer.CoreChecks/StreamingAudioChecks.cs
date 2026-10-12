using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NonetMusicPlayer.Core.Audio;

/// <summary>真实原生音频后端经慢速回环 Range 播放 96 kHz/24 位无损 PCM，覆盖周期性网络等待。</summary>
internal static class StreamingAudioChecks
{
    public static async Task RunAsync()
    {
        await using var server = new SlowMediaServer();
        using var audio = new NativeAudioPlayer { Volume = 0 };
        Exception? failure = null; var outputChanges = 0;
        audio.PlaybackFailed += (_, e) => failure = new InvalidOperationException(e.ToString());
        audio.OutputDeviceChanged += (_, _) => Interlocked.Increment(ref outputChanges);
        await audio.LoadAsync(server.Url).WaitAsync(TimeSpan.FromSeconds(20));
        Require(audio.DecodedFormat?.SampleRate == 96000 && audio.CloudBufferBytes is > 0 and <= BufferedPcmDataProvider.MaximumBufferBytes,"Lossless native stream uses bounded PCM buffer");
        audio.Play(); var clock = Stopwatch.StartNew();
        while(clock.Elapsed < TimeSpan.FromSeconds(24)) { await Task.Delay(100); Require(failure is null && audio.IsPlaying,"Network latency must not stop or fail playback"); }
        Require(audio.Position.TotalSeconds > 23 && outputChanges == 0,"Periodic slow Range does not stall music clock or trigger device recovery");
        var snapshot=audio.BufferSnapshot;Require(snapshot.MaxDecodeSamples <= 2048 && snapshot.DecodeCalls > 100,"Cloud is incrementally decoded");
        audio.Pause();var paused=audio.Position;await Task.Delay(150);Require(audio.Position==paused,"Pause keeps consumed rather than prefetched position");
        audio.Position=TimeSpan.FromSeconds(3);audio.Play();await Task.Delay(1200);Require(audio.Position.TotalSeconds is >3.8 and <4.4,"Seek resumes from requested position");
        audio.Pause();audio.Position=TimeSpan.FromSeconds(34);var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.PlaybackStopped+=(_,_)=>ended.TrySetResult();audio.Play();await ended.Task.WaitAsync(TimeSpan.FromSeconds(6));
        Require(audio.Position.TotalSeconds>=35.9&&failure is null,"Natural end drains prefetched PCM without false decoder failure");
        Console.WriteLine($"PASS native 96kHz/24-bit streaming, 24s stable clock, {server.DelayedRanges} delayed ranges, bounded {audio.CloudBufferBytes} bytes, pause/seek/EOF");
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private sealed class SlowMediaServer : IAsyncDisposable
    {
        private readonly TcpListener _listener=new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource _stop=new();
        private readonly byte[] _wave=Wave();
        private readonly Task _accept;
        private int _gets,_delayed;
        public int DelayedRanges=>Volatile.Read(ref _delayed);
        public string Url { get; }
        public SlowMediaServer() { _listener.Start();Url=$"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/audio?token={new string('A',64)}";_accept=AcceptAsync(); }
        private async Task AcceptAsync()
        {
            try { while(!_stop.IsCancellationRequested) { var client=await _listener.AcceptTcpClientAsync(_stop.Token);_=HandleAsync(client); } }
            catch(OperationCanceledException) { }
        }
        private async Task HandleAsync(TcpClient client)
        {
            using(client)try
            {
                var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);var first=await reader.ReadLineAsync(_stop.Token);
                long start=0,end=_wave.Length-1;var range=false;
                while(await reader.ReadLineAsync(_stop.Token) is { Length:>0 } header)
                    if(header.StartsWith("Range: bytes=",StringComparison.OrdinalIgnoreCase)) { var parts=header[13..].Split('-');start=long.Parse(parts[0]);end=long.Parse(parts[1]);range=true; }
                if(first!.StartsWith("GET")&&Interlocked.Increment(ref _gets)%4==0) { Interlocked.Increment(ref _delayed);await Task.Delay(1200,_stop.Token); }
                var headers=$"HTTP/1.1 {(range?"206 Partial Content":"200 OK")}\r\nContent-Length: {end-start+1}\r\nContent-Type: audio/wav\r\nAccept-Ranges: bytes\r\nConnection: close\r\n"+(range?$"Content-Range: bytes {start}-{end}/{_wave.Length}\r\n":"")+"\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers),_stop.Token);
                if(first.StartsWith("GET"))await stream.WriteAsync(_wave.AsMemory((int)start,(int)(end-start+1)),_stop.Token);
            }
            catch(Exception e) when(e is IOException or OperationCanceledException or SocketException) { }
        }
        private static byte[] Wave()
        {
            const int rate=96000,seconds=36,bytes=rate*seconds*6;using var memory=new MemoryStream(bytes+44);using var writer=new BinaryWriter(memory);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(bytes+36);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(rate);writer.Write(rate*6);writer.Write((short)6);writer.Write((short)24);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(bytes);
            writer.Write(new byte[bytes]);return memory.ToArray(); // 测试夹具为静音，生产后端仍按块读取。
        }
        public async ValueTask DisposeAsync() { _stop.Cancel();_listener.Stop();await _accept;_stop.Dispose(); }
    }
}
