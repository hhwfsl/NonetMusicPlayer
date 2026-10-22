using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Core.Streaming;

/// <summary>真实 HTTP/原生后端验证首包直读与取消；不把网络延迟伪装成音频转换成本。</summary>
internal static class RemoteStartupChecks
{
    public static async Task RunAsync()
    {
        using var server=new Fixture();using var stop=new CancellationTokenSource();
        using (var stream=new LoopbackRangeStream(new Uri(server.Url("drip")),stop.Token))
        {
            var clock=Stopwatch.StartNew();var first=new byte[4];
            await Task.Run(()=>stream.ReadExactly(first)).WaitAsync(TimeSpan.FromSeconds(2));
            Require(Encoding.ASCII.GetString(first)=="RIFF" && clock.ElapsedMilliseconds<1500,"First bytes must not wait for the rest of a one-megabyte response.");
            stream.ReadExactly(new byte[32768-4]);
            var blocked=Task.Run(()=>stream.ReadByte());await Task.Delay(100);clock.Restart();stop.Cancel();
            try {await blocked.WaitAsync(TimeSpan.FromSeconds(2));throw new InvalidDataException("Read ignored cancellation");} catch(OperationCanceledException){}
            Require(clock.ElapsedMilliseconds<1500,"An obsolete audio read must cancel before its network timeout.");
        }
        using var audio=new NativeAudioPlayer {Volume=0};using var request=new CancellationTokenSource();
        var loading=audio.LoadAsync(server.Url("slow-head"),request.Token);
        await server.HeadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));var timer=Stopwatch.StartNew();request.Cancel();
        try {await loading.WaitAsync(TimeSpan.FromSeconds(2));throw new InvalidDataException("Load ignored cancellation");}catch(OperationCanceledException){}
        Require(timer.ElapsedMilliseconds<1500,"Cancelled native initialization must release the loader promptly.");
        timer.Restart();await audio.LoadAsync(server.Url("fast")).WaitAsync(TimeSpan.FromSeconds(5));
        Require(audio.Duration.TotalSeconds>1 && audio.CloudBufferBytes<=BufferedPcmDataProvider.MaximumBufferBytes,"Latest source loads with bounded incremental decoding.");
        Console.WriteLine($"PASS remote startup: progressive first chunk, in-flight read/HEAD cancellation, native loader reuse; fixture startup {timer.ElapsedMilliseconds} ms.");
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    private sealed class Fixture:IDisposable
    {
        private readonly TcpListener _listener=new(IPAddress.Loopback,0);private readonly CancellationTokenSource _stop=new();
        private readonly byte[] _wave=Wave();public TaskCompletionSource HeadStarted {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url(string path)=>$"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/{path}?token={new string('A',64)}";
        public Fixture(){_listener.Start();_=AcceptAsync();}
        private async Task AcceptAsync(){try{while(!_stop.IsCancellationRequested){var client=await _listener.AcceptTcpClientAsync(_stop.Token);_=ServeAsync(client);}}catch(Exception e)when(e is OperationCanceledException or SocketException or ObjectDisposedException){}}
        private async Task ServeAsync(TcpClient client)
        {
            using(client)try
            {
                var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                var first=await reader.ReadLineAsync(_stop.Token) ?? "";long start=0,end=_wave.Length-1;var range=false;
                while(await reader.ReadLineAsync(_stop.Token) is {Length:>0} header)
                    if(header.StartsWith("Range: bytes=",StringComparison.OrdinalIgnoreCase)){var parts=header[13..].Split('-');start=long.Parse(parts[0]);end=Math.Min(long.Parse(parts[1]),_wave.Length-1);range=true;}
                if(first.Contains("/slow-head")){HeadStarted.TrySetResult();await Task.Delay(5000,_stop.Token);}
                var headers=$"HTTP/1.1 {(range?"206 Partial Content":"200 OK")}\r\nContent-Length: {end-start+1}\r\nContent-Type: audio/wav\r\nConnection: close\r\n"+(range?$"Content-Range: bytes {start}-{end}/{_wave.Length}\r\n":"")+"\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers),_stop.Token);
                if(first.StartsWith("HEAD"))return;
                var count=(int)(end-start+1);
                if(first.Contains("/drip")){var initial=Math.Min(32768,count);await stream.WriteAsync(_wave.AsMemory((int)start,initial),_stop.Token);await Task.Delay(5000,_stop.Token);start+=initial;count-=initial;}
                if(count>0)await stream.WriteAsync(_wave.AsMemory((int)start,count),_stop.Token);
            }
            catch(Exception e)when(e is IOException or SocketException or OperationCanceledException or ObjectDisposedException){}
        }
        private static byte[] Wave()
        {
            const int bytes=48000*2*12;using var output=new MemoryStream();using var writer=new BinaryWriter(output);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(bytes+36);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(48000);writer.Write(96000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(bytes);writer.Write(new byte[bytes]);return output.ToArray();
        }
        public void Dispose(){_stop.Cancel();_listener.Stop();}
    }
}
