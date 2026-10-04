using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using NonetMusicPlayer.Desktop.Services;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

/// <summary>Audible PCM fixtures expose sample-rate/channel mistakes hidden by silent 48 kHz stereo tests.</summary>
internal static class AudioChecks
{
    public static void Run(string output)
    {
        var folder = Path.Combine(Path.GetFullPath(output), "audio-fixtures"); Directory.CreateDirectory(folder);
        AppLog.Initialize(Path.Combine(Path.GetFullPath(output), "audio-test-data"));
        VerifyIncrementalConstructor(folder);
        using var audio = new NativeAudioPlayer { Volume = 0 };
        foreach (var (extension, rate, channels, tolerance) in new[] { ("wav", 44100, 1, .025), ("wav", 44100, 2, .025), ("wav", 48000, 1, .025), ("wav", 48000, 2, .025), ("wav", 96000, 2, .025), ("flac", 44100, 2, .025), ("mp3", 44100, 2, .15) })
        {
            var path = Path.Combine(folder, $"tone-{rate}-{channels}ch.{extension}");
            if (extension == "wav") WriteTone(path, rate, channels, 4, 440); else WriteCompressedTone(path, extension, rate, channels);
            audio.LoadAsync(path).GetAwaiter().GetResult();
            var duration = audio.Duration.TotalSeconds;
            Require(audio.DecodedFormat?.SampleRate == rate && audio.DecodedFormat?.Channels == channels, "Player must use the actual PCM sample rate and channel count");
            Console.WriteLine($"AUDIO {extension.ToUpperInvariant()} {rate} Hz / {channels} ch: duration = {duration:0.000} s (expected 4.000 s)");
            Require(Math.Abs(duration - 4) < tolerance, $"Native duration for {rate} Hz / {channels} channels: {duration:0.000} s != 4 s");
            VerifyToneDecoder(path, rate, channels);
            audio.Position = TimeSpan.FromSeconds(.5); Require(Math.Abs(audio.Position.TotalSeconds - .5) < .025, "Source-rate seek");
            audio.Play(); var baseline = audio.Position.TotalSeconds; var clock = Stopwatch.StartNew(); Thread.Sleep(1100); audio.Pause();
            var elapsed = clock.Elapsed.TotalSeconds; var advanced = audio.Position.TotalSeconds - baseline;
            Console.WriteLine($"  native clock: {advanced:0.000} audio seconds / {elapsed:0.000} wall seconds");
            Require(Math.Abs(advanced - elapsed) < .14, $"Native playback clock runs too fast or slow for {rate} Hz / {channels} channels");
            Require(!audio.IsPlaying, "Native pause"); audio.Position = TimeSpan.FromSeconds(2); Require(Math.Abs(audio.Position.TotalSeconds - 2) < .025, "Native seek");
            audio.Play(); Thread.Sleep(30); audio.Stop();
            Require(!audio.IsPlaying && audio.Position.TotalSeconds < .025, "Native stop resets position");
        }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler ended = (_, _) =>
        {
            try { audio.LoadAsync(Path.Combine(folder, "tone-44100-1ch.wav")).GetAwaiter().GetResult(); completion.TrySetResult(true); }
            catch (Exception e) { completion.TrySetException(e); }
        };
        audio.PlaybackStopped += ended; audio.Position = TimeSpan.FromSeconds(3.9); audio.Play();
        Require(completion.Task.Wait(TimeSpan.FromSeconds(5)), "PlaybackEnded must allow loading the next track with a different device format without deadlock");
        completion.Task.GetAwaiter().GetResult(); audio.PlaybackStopped -= ended;
        Require(audio.DecodedFormat?.SampleRate == 44100 && audio.DecodedFormat?.Channels == 1 && !audio.IsPlaying, "End callback safely loads the next format");
        var retained = audio.BufferSnapshot; audio.Position = TimeSpan.FromSeconds(2);
        audio.DeviceName = "Unavailable test output " + Guid.NewGuid();
        Require(audio.DeviceName == "Playback.SystemDefault" && !audio.IsPlaying && Math.Abs(audio.Position.TotalSeconds - 2) < .025 && audio.BufferSnapshot.LoadedSources == retained.LoadedSources, "Unavailable output falls back and retains the same decoder and position");
        audio.Play(); Thread.Sleep(120); audio.Pause(); Require(audio.Position.TotalSeconds > 2.05, "Native playback resumes retained decoder after output rebind");
        Console.WriteLine("PASS AUDIO OUTPUT: unavailable-device fallback, decoder retention, paused position and actual native resume");
        VerifyFullPlaybackCycles(folder);
        VerifyZeroSeekPreservesOpening(folder);
        VerifyDecodeFailures(folder);
        LogChecks.Run(output);
        Console.WriteLine("PASS AUDIO: non-silent WAV / FLAC / MP3, 44.1 / 48 / 96 kHz, mono / stereo, pitch, duration, real-time clock, pause, seek, volume, output rebind and end-callback track switch");
    }
    internal static void VerifyZeroSeekPreservesOpening(string folder)
    {
        foreach (var file in new[] { "tone-44100-2ch.flac", "tone-44100-2ch.mp3" })
        {
            var path = Path.Combine(folder, file);
            using var engine = new MiniAudioEngine(); engine.RegisterCodecFactory(new FFmpegCodecFactory());
            using var stream = File.OpenRead(path); using var decoder = engine.CreateDecoder(stream, out _, AudioFormat.DvdHq);
            var expected = new float[2048]; var count = decoder.Decode(expected);
            using var audio = new NativeAudioPlayer { Volume = 0 }; audio.LoadAsync(path).GetAwaiter().GetResult();
            var data = (SoundFlow.Interfaces.ISoundDataProvider)typeof(NativeAudioPlayer).GetField("_data", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(audio)!;
            var actual = new float[2048]; audio.Position = TimeSpan.Zero;
            Require(data.ReadBytes(actual) == count && expected.AsSpan(0, count).SequenceEqual(actual.AsSpan(0, count)), "Fresh zero-seek must preserve the first decoded audio packet");
            audio.Position = TimeSpan.Zero;
            Require(data.ReadBytes(actual) == count && expected.AsSpan(0, count).SequenceEqual(actual.AsSpan(0, count)), "Running-source rewind preserves the first decoded audio packet");
            audio.Stop();
            Require(data.ReadBytes(actual) == count && expected.AsSpan(0, count).SequenceEqual(actual.AsSpan(0, count)), "Stop preserves the opening packet for later playback");
        }
        Console.WriteLine("PASS AUDIO opening: fresh zero-seek, rewind and Stop preserve exact FLAC / MP3 first PCM block");
    }
    internal static void VerifyIncrementalConstructor(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "long-stream-probe.wav"); WriteTone(path, 44100, 2, 120, 440);
        using var engine = new MiniAudioEngine(); engine.RegisterCodecFactory(new FFmpegCodecFactory());
        using var observed = new ObservedReadStream(File.OpenRead(path));
        using var decoder = engine.CreateDecoder(observed, out _, AudioFormat.DvdHq);
        var constructorRead = observed.BytesRead;
        Require(constructorRead < observed.Length / 4 && constructorRead < 2 * 1024 * 1024, "FFmpeg construction probes metadata without preloading complete audio");
        var samples = new float[2048]; var count = decoder.Decode(samples);
        Require(count > 0 && observed.BytesRead < observed.Length / 4, "PCM decoding is demand-driven rather than whole-track buffered");
        Console.WriteLine($"AUDIO incremental stream: source={observed.Length} bytes, constructorReads={constructorRead}, maximumReadRequest={observed.MaxReadRequest}, firstBlock={count} samples");
    }
    private sealed class ObservedReadStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public int MaxReadRequest { get; private set; }
        public override int Read(Span<byte> buffer) { MaxReadRequest = Math.Max(MaxReadRequest, buffer.Length); var count = inner.Read(buffer); BytesRead += count; return count; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
    private static void VerifyDecodeFailures(string folder)
    {
        var good = File.ReadAllBytes(Path.Combine(folder, "tone-44100-2ch.flac"));
        var tail = Path.Combine(folder, "tail-bytes.flac"); File.WriteAllBytes(tail, good.Concat(Enumerable.Repeat((byte)0xa5, 1024)).ToArray());
        using var audio = new NativeAudioPlayer { Volume = 0 };
        var tailComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var failures = 0; var endings = 0;
        EventHandler ended = (_, _) => { Interlocked.Increment(ref endings); tailComplete.TrySetResult(true); };
        EventHandler<AudioPlaybackErrorEventArgs> failed = (_, _) => { Interlocked.Increment(ref failures); };
        audio.PlaybackStopped += ended; audio.PlaybackFailed += failed;
        audio.LoadAsync(tail).GetAwaiter().GetResult(); audio.Play();
        Require(tailComplete.Task.Wait(TimeSpan.FromSeconds(8)), "Malformed FLAC tail must finish instead of terminating the native callback");
        Require(failures == 0 && endings == 1, "Tail parsing issue must preserve one natural completion");
        audio.PlaybackStopped -= ended; audio.PlaybackFailed -= failed;
        var truncated = Path.Combine(folder, "truncated.flac"); File.WriteAllBytes(truncated, good.AsSpan(0, good.Length / 2).ToArray());
        var decodeFailure = new TaskCompletionSource<AudioPlaybackErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFailures = 0;
        EventHandler<AudioPlaybackErrorEventArgs> throwing = (_, _) => { throw new InvalidOperationException("Simulated failing event subscriber; token=should-never-be-written"); };
        EventHandler<AudioPlaybackErrorEventArgs> failure = (_, error) => { Interlocked.Increment(ref callbackFailures); decodeFailure.TrySetResult(error); };
        var earlyEndings = 0; EventHandler early = (_, _) => Interlocked.Increment(ref earlyEndings);
        audio.PlaybackFailed += throwing; audio.PlaybackFailed += failure; audio.PlaybackStopped += early;
        audio.LoadAsync(truncated).GetAwaiter().GetResult(); audio.Play();
        Require(decodeFailure.Task.Wait(TimeSpan.FromSeconds(8)), "Early truncated FLAC must report a safe decode failure");
        Require(decodeFailure.Task.Result.ErrorCode == "audio.decode.failed" && callbackFailures == 1 && earlyEndings == 0 && !audio.IsPlaying, "Decode failure must notify once, stop and not silently auto-advance");
        audio.PlaybackFailed -= throwing; audio.PlaybackFailed -= failure; audio.PlaybackStopped -= early;
        audio.LoadAsync(Path.Combine(folder, "tone-48000-2ch.wav")).GetAwaiter().GetResult(); audio.Play(); Thread.Sleep(50); audio.Stop();
        Require(!audio.IsPlaying, "A failed track must not prevent later normal playback");
        Console.WriteLine("PASS AUDIO safety: malformed FLAC tail, truncated stream, one failure notification, subscriber exceptions and playback recovery");
    }
    private static void VerifyFullPlaybackCycles(string folder)
    {
        using var audio = new NativeAudioPlayer { Volume = 0 };
        var sources = new[] { "tone-44100-1ch.wav", "tone-48000-2ch.wav", "tone-44100-2ch.flac", "tone-44100-2ch.mp3", "tone-96000-2ch.wav" };
        var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var endedCount = 0;
        EventHandler ended = (_, _) =>
        {
            try
            {
                var count = Interlocked.Increment(ref endedCount); Console.WriteLine($"AUDIO complete natural playback / automatic next {count}/10");
                if (count == 10) { complete.TrySetResult(true); return; }
                audio.LoadAsync(Path.Combine(folder, sources[count % sources.Length])).GetAwaiter().GetResult(); audio.Play();
            }
            catch (Exception e) { complete.TrySetException(e); }
        };
        audio.PlaybackStopped += ended; audio.LoadAsync(Path.Combine(folder, sources[0])).GetAwaiter().GetResult(); audio.Play();
        Require(complete.Task.Wait(TimeSpan.FromSeconds(55)), "Ten fully decoded automatic-next tracks must complete without a native crash or duplicate EOF");
        complete.Task.GetAwaiter().GetResult(); audio.PlaybackStopped -= ended; Require(endedCount == 10, "One completion notification per track");
        var repeated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var repeatCount = 0;
        EventHandler repeat = (_, _) =>
        {
            try
            {
                var count = Interlocked.Increment(ref repeatCount); Console.WriteLine($"AUDIO natural same-track repeat {count}/4");
                if (count == 4) { repeated.TrySetResult(true); return; }
                audio.Position = TimeSpan.Zero; audio.Play();
            }
            catch (Exception e) { repeated.TrySetException(e); }
        };
        audio.LoadAsync(Path.Combine(folder, sources[3])).GetAwaiter().GetResult(); audio.PlaybackStopped += repeat; audio.Play();
        Require(repeated.Task.Wait(TimeSpan.FromSeconds(25)), "Single-track repeat must finish four full MP3 decodes without crashing");
        repeated.Task.GetAwaiter().GetResult(); audio.PlaybackStopped -= repeat; Require(repeatCount == 4, "One event per single-track repeat");
        audio.LoadAsync(Path.Combine(folder, sources[3])).GetAwaiter().GetResult(); audio.Play();
        for (var i = 0; i < 30; i++) { audio.Position = TimeSpan.FromSeconds((i % 6) * .45); Thread.Sleep(12); Require(audio.IsPlaying, "Frequent seek preserves playback"); }
        audio.Pause(); Require(audio.Position.TotalSeconds < audio.Duration.TotalSeconds, "Seek position remains inside actual duration");
        Console.WriteLine("PASS AUDIO lifecycle: ten full cross-format continuations, four full MP3 same-track loops and thirty running seeks");
    }
    private static void VerifyToneDecoder(string path, int rate, int channels)
    {
        using var engine = new MiniAudioEngine(); engine.RegisterCodecFactory(new FFmpegCodecFactory());
        using var stream = File.OpenRead(path); using var provider = new StreamDataProvider(engine, AudioFormat.DvdHq, stream);
        Require(provider.SampleRate == rate && provider.FormatInfo?.ChannelCount == channels, "Decoder must report its real output rate/channels");
        provider.Seek(rate * channels / 4); // Exclude encoder priming / delay when counting the MP3 tone.
        var buffer = new float[(int)(rate * channels * 1.5)]; var count = 0;
        while (count < buffer.Length) { var read = provider.ReadBytes(buffer.AsSpan(count)); if (read == 0) break; count += read; }
        Require(count == buffer.Length, "Decode a non-silent second of PCM");
        var crossings = 0; var squared = 0d;
        for (var frame = rate / 2; frame < count / channels; frame++)
        {
            var current = buffer[frame * channels]; var previous = buffer[(frame - 1) * channels];
            if (previous <= 0 && current > 0) crossings++;
            squared += current * current;
        }
        Require(Math.Abs(crossings - 440) <= 2 && Math.Sqrt(squared / rate) > .05, $"Decoded non-silent 440 Hz tone has wrong pitch: {crossings} Hz");
    }
    internal static void WriteCompressedTone(string path, string extension, int rate, int channels, int seconds = 4)
    {
        using var engine = new MiniAudioEngine(); engine.RegisterCodecFactory(new FFmpegCodecFactory());
        using var stream = File.Create(path);
        using (var encoder = engine.CreateEncoder(stream, extension, new AudioFormat { Format = SampleFormat.F32, SampleRate = rate, Channels = channels, Layout = AudioFormat.GetLayoutFromChannels(channels) }))
        {
            var samples = new float[4096 * channels];
            for (var start = 0; start < rate * seconds; start += 4096)
            {
                var frames = Math.Min(4096, rate * seconds - start);
                for (var frame = 0; frame < frames; frame++) for (var channel = 0; channel < channels; channel++) samples[frame * channels + channel] = (float)(Math.Sin(2 * Math.PI * 440 * (start + frame) / rate) * .18);
                Require(encoder.Encode(samples.AsSpan(0, frames * channels)) == frames * channels, "Encode complete compressed tone fixture");
            }
        }
        if (extension == "flac")
        {
            // The codec's encoder AVIO has no seek callback, so it cannot back-patch FLAC's
            // total_samples field. Fill the fixture's known sample count, as normal files do.
            stream.Position = 0; var header = new byte[8]; stream.ReadExactly(header);
            Require(Encoding.ASCII.GetString(header, 0, 4) == "fLaC" && (header[4] & 0x7f) == 0 && header[7] == 34, "Expected FLAC STREAMINFO block");
            stream.Position = 18; var packed = new byte[8]; stream.ReadExactly(packed);
            BinaryPrimitives.WriteUInt64BigEndian(packed, (BinaryPrimitives.ReadUInt64BigEndian(packed) & 0xfffffff000000000UL) | (ulong)(uint)(rate * seconds));
            stream.Position = 18; stream.Write(packed);
        }
    }
    internal static void WriteTone(string path, int rate, int channels, int seconds, double frequency)
    {
        using var writer = new BinaryWriter(File.Create(path)); var frames = rate * seconds; var size = frames * channels * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(size + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size);
        for (var frame = 0; frame < frames; frame++) for (var channel = 0; channel < channels; channel++) writer.Write((short)(Math.Sin(2 * Math.PI * frequency * frame / rate) * 6000));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
