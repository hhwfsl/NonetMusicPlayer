using System.Diagnostics;
using NonetMusicPlayer.Core.Audio;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;

/// <summary>模拟慢速网络解码：实时读取不得等待，寻址、EOF、内存上限和取消必须保持一致。</summary>
internal static class BufferedPcmChecks
{
    public static async Task RunAsync()
    {
        using var source = new Source(40000, 8000);
        using var buffered = new BufferedPcmDataProvider(source, 2, source.Abort);
        await buffered.PrimeAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var output = new float[256]; var consumed = 0;
        while (!buffered.AtEnd)
        {
            var clock = Stopwatch.StartNew(); var ready = buffered.CanRender(output.Length);
            Require(clock.ElapsedMilliseconds < 100, "PCM preflight waits for no decoder lock");
            if (!ready) { var position = buffered.Position; await Task.Delay(1); Require(buffered.Position == position, "Buffering preserves consumed position"); continue; }
            var read = buffered.ReadBytes(output); Require(read > 0 || buffered.AtEnd, "Empty ring is not EOF");
            for (var i = 0; i < read; i++) Require(output[i] == consumed + i, "PCM ring ordering"); consumed += read;
        }
        Require(consumed == source.Length && buffered.ReadBytes(output) == 0, "EOF drains every buffered sample");
        buffered.Seek(1234); await buffered.PrimeAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Require(buffered.Position == 1234 && buffered.ReadBytes(output) == output.Length && output[0] == 1234, "Seek discards old PCM and resets consumed clock");
        Require(source.MaximumRead <= 2048, "Bounded decoder chunks");
        using (var wideSource = new Source(0, 192000))
        using (var wide = new BufferedPcmDataProvider(wideSource, 64, wideSource.Abort))
            Require(wide.BufferBytes <= BufferedPcmDataProvider.MaximumBufferBytes, "High-resolution PCM buffer capped at 8 MiB");
        using var blockedSource = new Source(50000, 8000) { Block = true };
        var blocked = new BufferedPcmDataProvider(blockedSource, 2, blockedSource.Abort);
        Require(blockedSource.Entered.Wait(3000), "Worker entered blocked read");
        var dispose = Task.Run(blocked.Dispose); await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        Require(blocked.IsDisposed && blockedSource.IsDisposed, "Cancellation joins worker before decoder disposal");
        blocked.Dispose(); Console.WriteLine("PASS bounded/background PCM, nonblocking callback, starvation clock, EOF, seek and cancellation");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Source(int length, int rate) : ISoundDataProvider
    {
        private readonly ManualResetEventSlim _abort = new();
        public readonly ManualResetEventSlim Entered = new();
        public bool Block { get; init; }
        public int MaximumRead { get; private set; }
        public int Position { get; private set; }
        public int Length => length;
        public int SampleRate => rate;
        public bool CanSeek => true;
        public SampleFormat SampleFormat => SampleFormat.F32;
        public bool IsDisposed { get; private set; }
        public SoundFormatInfo? FormatInfo => null;
        public event EventHandler<EventArgs>? EndOfStreamReached { add { } remove { } }
        public event EventHandler<PositionChangedEventArgs>? PositionChanged { add { } remove { } }
        public int ReadBytes(Span<float> output)
        {
            Entered.Set(); if (Block) { _abort.Wait(); throw new OperationCanceledException(); }
            MaximumRead = Math.Max(MaximumRead, output.Length);
            // 读取边界处人为延迟，确保消费者仍可独立读取已缓冲的数据。
            if (Position % 8192 == 0) Thread.Sleep(15);
            var count = Math.Min(output.Length, Length - Position);
            for (var i = 0; i < count; i++) output[i] = Position + i; Position += count; return count;
        }
        public void Seek(int offset) => Position = offset;
        public void Abort() => _abort.Set();
        public void Dispose() { IsDisposed = true; _abort.Set(); }
    }
}
