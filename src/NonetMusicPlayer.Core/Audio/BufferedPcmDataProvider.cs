using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;

namespace NonetMusicPlayer.Core.Audio;

/// <summary>云端音频使用有界 PCM 环形缓冲；只有后台生产线程调用解码器和网络流。</summary>
public sealed class BufferedPcmDataProvider : ISoundDataProvider
{
    public const int MaximumBufferBytes = 8 * 1024 * 1024;
    private readonly ISoundDataProvider _source;
    private readonly Action _abortReads;
    private readonly object _bufferGate = new(), _decoderGate = new();
    private readonly float[] _ring;
    private readonly int _channels, _resumeSamples, _initialSamples, _length;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _work = new(0, 1);
    private readonly Task _producer;
    private TaskCompletionSource _ready = NewReady();
    private int _readIndex, _writeIndex, _count, _position;
    private bool _eof, _ended, _buffering = true, _disposed;
    private static TaskCompletionSource NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public BufferedPcmDataProvider(ISoundDataProvider source, int channels, Action abortReads)
    {
        _source = source; _channels = channels; _abortReads = abortReads; _length = source.Length;
        if (channels is < 1 or > 64 || source.SampleRate <= 0) throw new InvalidDataException("Invalid PCM format.");
        var samples = Math.Min(Math.Max(4096L * channels, 4L * source.SampleRate * channels), MaximumBufferBytes / sizeof(float));
        _ring = new float[(int)(samples / channels * channels)];
        _resumeSamples = (int)Math.Min(_ring.Length, (long)source.SampleRate * channels);
        // 首播只需四分之一秒 PCM；中途饥饿仍等待一秒，兼顾响应速度与连续播放稳定性。
        _initialSamples = Math.Max(channels, _resumeSamples / 4 / channels * channels);
        _producer = Task.Run(ProduceAsync);
    }
    public int BufferBytes => _ring.Length * sizeof(float);
    internal ISoundDataProvider Source => _source;
    public bool AtEnd { get { lock (_bufferGate) return _eof && _count == 0; } }
    public bool IsBuffering { get { lock (_bufferGate) return _buffering && !_eof; } }
    public int Position => Volatile.Read(ref _position);
    public int Length => _length;
    public bool CanSeek => _source.CanSeek;
    public SampleFormat SampleFormat => _source.SampleFormat;
    public int SampleRate => _source.SampleRate;
    public bool IsDisposed => Volatile.Read(ref _disposed);
    public SoundFormatInfo? FormatInfo => _source.FormatInfo;
    public event EventHandler<EventArgs>? EndOfStreamReached;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public Task PrimeAsync(CancellationToken token = default) { lock (_bufferGate) return _ready.Task.WaitAsync(token); }
    /// <summary>缓冲不足时跳过本次设备回调，不返回虚假的 EOF，也不消耗歌曲时间。</summary>
    public bool CanRender(int samples)
    {
        lock (_bufferGate)
        {
            if (_disposed) return false;
            if (_eof) return true;
            if (_count < samples) _buffering = true;
            if (_buffering && _count < Math.Max(samples, _resumeSamples)) return false;
            _buffering = false; return true;
        }
    }
    public int ReadBytes(Span<float> output)
    {
        int count; bool end;
        lock (_bufferGate)
        {
            if (_disposed) { output.Clear(); return 0; }
            count = Math.Min(output.Length, _count);
            var first = Math.Min(count, _ring.Length - _readIndex);
            _ring.AsSpan(_readIndex, first).CopyTo(output);
            _ring.AsSpan(0, count - first).CopyTo(output[first..]);
            _readIndex = (_readIndex + count) % _ring.Length; _count -= count; _position += count;
            end = count == 0 && _eof && !_ended; if (end) _ended = true;
        }
        Wake(); if (count > 0) PositionChanged?.Invoke(this, new(Position));
        if (end) EndOfStreamReached?.Invoke(this, EventArgs.Empty);
        return count;
    }
    private void Wake() { try { if (_work.CurrentCount == 0) _work.Release(); } catch (SemaphoreFullException) { } }
    private async Task ProduceAsync()
    {
        var chunk = new float[1024 * _channels];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var wait = false;
                lock (_decoderGate)
                {
                    int room;
                    lock (_bufferGate) { room = _ring.Length - _count; wait = _eof || room < _channels; }
                    if (!wait)
                    {
                        // 不在环形缓冲锁内进行解码，实时线程永远不会等待网络读取。
                        var read = _source.ReadBytes(chunk.AsSpan(0, Math.Min(chunk.Length, room) / _channels * _channels));
                        lock (_bufferGate)
                        {
                            if (_disposed) break;
                            if (read == 0) _eof = true;
                            else
                            {
                                var first = Math.Min(read, _ring.Length - _writeIndex);
                                chunk.AsSpan(0, first).CopyTo(_ring.AsSpan(_writeIndex));
                                chunk.AsSpan(first, read - first).CopyTo(_ring);
                                _writeIndex = (_writeIndex + read) % _ring.Length; _count += read;
                            }
                            if (_eof || _count >= _resumeSamples || !_ready.Task.IsCompleted && _count >= _initialSamples) { _buffering = false; _ready.TrySetResult(); }
                        }
                    }
                }
                if (wait) await _work.WaitAsync(_stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { lock (_bufferGate) { _eof = true; _ready.TrySetException(error); } }
    }
    public void Seek(int offset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_decoderGate)
        {
            _source.Seek(offset);
            lock (_bufferGate)
            {
                _readIndex = _writeIndex = _count = 0; _position = offset; _eof = _ended = false; _buffering = true; _ready = NewReady();
            }
        }
        Wake(); PositionChanged?.Invoke(this, new(offset));
    }
    public void Dispose()
    {
        lock (_bufferGate) { if (_disposed) return; _disposed = true; _ready.TrySetCanceled(); }
        _stop.Cancel(); _abortReads();
        // 先取消网络并结束生产线程，才能释放仍可能被 FFmpeg 使用的流和解码器。
        _producer.GetAwaiter().GetResult(); _source.Dispose(); _stop.Dispose(); _work.Dispose();
    }
}
