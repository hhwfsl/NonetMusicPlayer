using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Codecs.FFMpeg;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;

using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Streaming;

namespace NonetMusicPlayer.Core.Audio;
/// <summary>按需解码的原生音频后端。桌面与命令行共用设备恢复和解码资源生命周期。</summary>
public sealed class NativeAudioPlayer : IAudioPlayer
{
    private readonly object _gate = new();
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;
    private SoundPlayer? _player;
    private ISoundDataProvider? _data;
    private Stream? _stream;
    private AudioFormat? _decodedFormat;
    private bool _disposed;
    private long _sourceGeneration, _commandGeneration, _lastQueuedEnd = -1, _failedSource = -1;
    private long _loadedSources, _releasedSources;
    private float _volume = .8f;
    private string _deviceName = "Playback.SystemDefault", _activeDeviceName = "";
    private string[] _knownDevices = ["Playback.SystemDefault"];
    private readonly Timer _outputMonitor;
    private readonly bool _probeWhileIdle;
    private double _lastDevicePosition;
    private long _lastDeviceAdvance = System.Diagnostics.Stopwatch.GetTimestamp();
    public NativeAudioPlayer(bool probeWhileIdle = true)
    {
        _probeWhileIdle = probeWhileIdle;
        _outputMonitor = new Timer(CheckOutput, null, 500, 1000);
    }
    public bool IsAvailable => true;
    public long PlaybackGeneration => Volatile.Read(ref _commandGeneration);
    public AudioBufferSnapshot BufferSnapshot
    {
        get { lock (_gate) return new(_loadedSources, _releasedSources, _data is not null ? 1 : 0, (_data as DecodedDataProvider)?.MaxDecodeSamples ?? 0, (_data as DecodedDataProvider)?.DecodeCalls ?? 0); }
    }
    public AudioFormat? DecodedFormat { get { lock (_gate) return _decodedFormat; } }
    public bool IsPlaying { get { lock (_gate) return _player?.State == PlaybackState.Playing; } }
    public TimeSpan Duration { get { lock (_gate) return TimeSpan.FromSeconds(_player?.Duration ?? 0); } }
    public TimeSpan Position
    {
        get { lock (_gate) return TimeSpan.FromSeconds(_player?.Time ?? 0); }
        set
        {
            lock (_gate)
            {
                if (_player is null) return;
                Interlocked.Increment(ref _commandGeneration);
                var running = _device?.IsRunning == true;
                if (running) _device!.Stop();
                try
                {
                    if (!_player.Seek(TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds, 0, Math.Max(0, _player.Duration))), SeekOrigin.Begin)) throw new IOException("此音频源暂时无法跳转，请重试或切换歌曲。");
                }
                catch (Exception e) { AppLog.Error("Audio.Seek", "音频跳转失败", e); throw; }
                finally { if (running) _device!.Start(); }
            }
        }
    }
    public float Volume { get => _volume; set { lock (_gate) { _volume = Math.Clamp(value, 0, 1); if (_player is not null) _player.Volume = _volume; } } }
    public string DeviceName { get => _deviceName; set { lock (_gate) { if (_deviceName == value) return; _deviceName = value; if (_player is not null) RebindOutput(); } } }
    public IReadOnlyList<string> Devices => Volatile.Read(ref _knownDevices);
    /// <summary>显式设备查询才在无播放的 CLI 中初始化后端；无声卡不阻止资料库管理。</summary>
    public void RefreshDevices()
    {
        lock (_gate)
        {
            EnsureEngine(); _engine!.UpdateAudioDevicesInfo();
            Volatile.Write(ref _knownDevices, new[] { "Playback.SystemDefault" }.Concat(_engine.PlaybackDevices.Select(d => d.Name)).Distinct().ToArray());
        }
    }
    public event EventHandler<AudioOutputChangedEventArgs>? OutputDeviceChanged;
    public event EventHandler? OutputDevicesChanged;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<AudioPlaybackErrorEventArgs>? PlaybackFailed;
    private void EnsureEngine() { ObjectDisposedException.ThrowIf(_disposed, this); if (_engine is not null) return; _engine = new MiniAudioEngine(); _engine.RegisterCodecFactory(new FFmpegCodecFactory()); }
    private void EnsureDevice(AudioFormat format)
    {
        EnsureEngine(); if (_device?.Format == format) return;
        ReleaseDevice(); _engine!.UpdateAudioDevicesInfo();
        if (_engine.PlaybackDevices.Length == 0) throw new AudioOutputUnavailableException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Playback.NoAudioOutputDeviceFoundConnectOneAndTry"));
        var device = _engine.PlaybackDevices.FirstOrDefault(d => _deviceName != "Playback.SystemDefault" ? d.Name == _deviceName : d.IsDefault);
        if (_deviceName != "Playback.SystemDefault" && !_engine.PlaybackDevices.Any(d => d.Name == _deviceName)) { _deviceName = "Playback.SystemDefault"; device = _engine.PlaybackDevices.FirstOrDefault(d => d.IsDefault); }
        if (string.IsNullOrEmpty(device.Name)) device = _engine.PlaybackDevices[0];
        // 共享模式下由 MiniAudio 或系统转换到硬件格式；采样时钟必须匹配解码结果，不能假设为 48 kHz 双声道。
        try { _device = _engine.InitializePlaybackDevice(device, format, new MiniAudioDeviceConfig()); _activeDeviceName = device.Name; }
        catch (Exception error) { throw new AudioOutputUnavailableException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Playback.AudioOutputCouldNotBeOpenedTheTrackAnd"), error); }
    }
    public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureEngine(); ReleasePlayer();
            try
            {
                _stream = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? new LoopbackRangeStream(uri) : File.OpenRead(source);
                // 请求格式只是采样类型提示；采样率和声道数仍来自音源，必须检查实际输出。
                var sourceGeneration = _sourceGeneration;
                var decoded = new DecodedDataProvider(_engine!, _stream, e => DecodeFailed(sourceGeneration, e));
                _data = decoded; _decodedFormat = decoded.Format; _loadedSources++; EnsureDevice(decoded.Format);
                _player = new SoundPlayer(_engine!, decoded.Format, _data) { Volume = _volume };
                _player.PlaybackEnded += Ended; _device!.MasterMixer.AddComponent(_player); cancellationToken.ThrowIfCancellationRequested();
                AppLog.Info("Audio.Load", $"已载入音频会话 {sourceGeneration}，{decoded.Format.SampleRate} Hz / {decoded.Format.Channels} 声道，时长 {_player.Duration:0.000} 秒");
            }
            catch (Exception e) { ReleasePlayer(); AppLog.Error("Audio.Load", "音频载入失败", e); throw; }
        }
    }, cancellationToken);
    private void Ended(object? sender, EventArgs e)
    {
        // 实时设备线程不调用宿主代码；宿主切歌可能等待设备停止，直接回调会造成死锁。
        var sourceGeneration = Volatile.Read(ref _sourceGeneration); var commandGeneration = Volatile.Read(ref _commandGeneration);
        if (Volatile.Read(ref _failedSource) == sourceGeneration || Interlocked.Exchange(ref _lastQueuedEnd, commandGeneration) == commandGeneration) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (_gate)
                {
                    if (_disposed || !ReferenceEquals(sender, _player) || _sourceGeneration != sourceGeneration || _commandGeneration != commandGeneration || _failedSource == sourceGeneration) return;
                    // 等待 SoundFlow 完成结束事件并停用组件后，才允许单曲循环重启同一播放器。
                    if (_device?.IsRunning == true) _device.Stop();
                }
                AppLog.Info("Audio.End", $"音频会话 {sourceGeneration} 已完整结束");
                PlaybackStopped?.Invoke(this, new AudioPlaybackEndedEventArgs(commandGeneration));
            }
            catch (Exception error) { AppLog.Error("Audio.End", "播放结束事件处理失败", error); NotifyFailure("audio.completion.failed", "自动续播失败，请重试或手动切换歌曲。", commandGeneration); }
        });
    }
    private void DecodeFailed(long generation, Exception exception)
    {
        if (generation != Volatile.Read(ref _sourceGeneration)) return;
        var commandGeneration = Volatile.Read(ref _commandGeneration);
        Volatile.Write(ref _failedSource, generation);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (_gate)
                {
                    if (_disposed || generation != _sourceGeneration) return;
                    if (_device?.IsRunning == true) _device.Stop();
                    _player?.Pause();
                }
                AppLog.Error("Audio.Decode", $"音频会话 {generation} 的解码失败，已安全停止播放", exception);
                NotifyFailure("audio.decode.failed", "音频解码失败，播放已停止。请重试、切换歌曲或检查音频文件。", commandGeneration);
            }
            catch (Exception error) { AppLog.Error("Audio.Decode", "停止失败的音频会话时发生错误", error); }
        });
    }
    private void NotifyFailure(string code, string message, long generation)
    {
        if (PlaybackFailed is not { } handlers) return;
        foreach (EventHandler<AudioPlaybackErrorEventArgs> handler in handlers.GetInvocationList())
            try { handler(this, new AudioPlaybackErrorEventArgs(code, message, generation)); } catch (Exception e) { AppLog.Error("Audio.Notification", "音频错误通知处理失败", e); }
    }
    public void Play()
    {
        lock (_gate)
        {
            if (_player is null) return; Interlocked.Increment(ref _commandGeneration);
            if (_device is null && _decodedFormat is { } format) { EnsureDevice(format); _device!.MasterMixer.AddComponent(_player); }
            if (_device?.IsRunning == true) _device.Stop();
            if (_failedSource == _sourceGeneration) throw new InvalidOperationException("当前音频解码已失败，请重新载入或切换歌曲。");
            if (_data is DecodedDataProvider { AtEnd: true }) _player.Seek(TimeSpan.Zero);
            _lastDevicePosition = _player.Time; _lastDeviceAdvance = System.Diagnostics.Stopwatch.GetTimestamp();
            try { _player.Play(); if (_device?.IsRunning == false) _device.Start(); }
            catch (Exception error) { _player.Pause(); ReleaseDevice(); throw new AudioOutputUnavailableException(NonetMusicPlayer.Core.Localization.LocalizationCatalog.Get("Playback.AudioOutputCouldNotBeOpenedTheTrackAnd"), error); }
        }
    }
    public void Pause() { lock (_gate) { Interlocked.Increment(ref _commandGeneration); _player?.Pause(); try { if (_device?.IsRunning == true) _device.Stop(); } catch (Exception error) { AppLog.Warning("Audio.Output", "暂停不可用设备失败，保留音频源", error); } } }
    public void Stop() { lock (_gate) { Interlocked.Increment(ref _commandGeneration); _player?.Pause(); try { if (_device?.IsRunning == true) _device.Stop(); } catch (Exception error) { AppLog.Warning("Audio.Output", "停止不可用设备失败", error); ReleaseDevice(); } _player?.Stop(); } }
    private void ReleaseDevice()
    {
        if (_device is null) return;
        try { if (_device.IsRunning) _device.Stop(); } catch (Exception error) { AppLog.Warning("Audio.Output", "停止不可用设备失败", error); }
        if (_player is not null) _device.MasterMixer.RemoveComponent(_player);
        try { _device.Dispose(); } catch (Exception error) { AppLog.Warning("Audio.Output", "释放不可用设备失败", error); }
        _device = null; _activeDeviceName = "";
    }
    private void RebindOutput()
    {
        Interlocked.Increment(ref _commandGeneration); _player?.Pause(); ReleaseDevice();
        if (_decodedFormat is not { } format) return;
        EnsureDevice(format); if (_player is not null) _device!.MasterMixer.AddComponent(_player);
    }
    private void CheckOutput(object? state)
    {
        if (!Monitor.TryEnter(_gate)) return;
        AudioOutputChangedEventArgs? notification = null; var devicesChanged = false;
        try
        {
            if (_disposed || !_probeWhileIdle && _engine is null) return;
            EnsureEngine(); _engine!.UpdateAudioDevicesInfo();
            var devices = new[] { "Playback.SystemDefault" }.Concat(_engine.PlaybackDevices.Select(d => d.Name)).Distinct().ToArray();
            if (!devices.SequenceEqual(_knownDevices)) { Volatile.Write(ref _knownDevices, devices); devicesChanged = true; }
            if (_player is null) return;
            var defaultName = _engine.PlaybackDevices.FirstOrDefault(d => d.IsDefault).Name;
            var disappeared = _device is not null && !_engine.PlaybackDevices.Any(d => d.Name == _activeDeviceName);
            var changedDefault = _device is not null && _deviceName == "Playback.SystemDefault" && !string.IsNullOrEmpty(defaultName) && _activeDeviceName != defaultName;
            var stuck = false;
            if (_player.State == PlaybackState.Playing)
            {
                if (Math.Abs(_player.Time - _lastDevicePosition) > .01) { _lastDevicePosition = _player.Time; _lastDeviceAdvance = System.Diagnostics.Stopwatch.GetTimestamp(); }
                stuck = _device?.IsRunning != true || System.Diagnostics.Stopwatch.GetElapsedTime(_lastDeviceAdvance).TotalSeconds > 3;
            }
            var restored = _device is null && _engine.PlaybackDevices.Length > 0;
            if (!disappeared && !changedDefault && !stuck && !restored) return;
            var position = _player.Time;
            if (disappeared || stuck) _deviceName = "Playback.SystemDefault";
            var available = true;
            try { RebindOutput(); }
            catch (Exception error) { available = false; AppLog.Warning("Audio.Output", "输出切换暂未完成", error); }
            notification = new(_deviceName, position, available, PlaybackGeneration);
        }
        catch (Exception error) { AppLog.Warning("Audio.Output", "后台设备检查失败", error); }
        finally
        {
            Monitor.Exit(_gate);
            if (devicesChanged) OutputDevicesChanged?.Invoke(this, EventArgs.Empty);
            if (notification is not null) OutputDeviceChanged?.Invoke(this, notification);
        }
    }
    private void ReleasePlayer()
    {
        Interlocked.Increment(ref _sourceGeneration); Interlocked.Increment(ref _commandGeneration); Volatile.Write(ref _failedSource, -1);
        // 先停止音频回调，再释放它可能正在读取的解码器。
        try { if (_device?.IsRunning == true) _device.Stop(); }
        catch (Exception error) { AppLog.Warning("Audio.Output", "释放音频源时设备不可用", error); ReleaseDevice(); }
        // 释放资源不同于用户停止；不要对即将释放的解码器归零跳转，断开的音源无法完成额外网络请求。
        if (_player is not null) { _player.PlaybackEnded -= Ended; _player.Pause(); _device?.MasterMixer.RemoveComponent(_player); _player.Dispose(); _player = null; }
        if (_data is not null) { _data.Dispose(); _releasedSources++; } _data = null; _stream?.Dispose(); _stream = null; _decodedFormat = null;
    }
    public void Dispose() { _outputMonitor.Dispose(); lock (_gate) { if (_disposed) return; _disposed = true; ReleasePlayer(); ReleaseDevice(); _engine?.Dispose(); _engine = null; } }

    /// <summary>保留解码器真实 PCM 格式，包括 StreamDataProvider 隐藏的声道数。</summary>
    private sealed class DecodedDataProvider : ISoundDataProvider
    {
        private ISoundDecoder _decoder;
        private readonly SoundFlow.Abstracts.AudioEngine _engine;
        private readonly Stream _stream;
        private readonly Action<Exception> _onFailure;
        private bool _atEnd, _faulted;
        public int MaxDecodeSamples { get; private set; }
        public long DecodeCalls { get; private set; }
        public bool AtEnd => _atEnd;
        public AudioFormat Format { get; }
        public DecodedDataProvider(SoundFlow.Abstracts.AudioEngine engine, Stream stream, Action<Exception> onFailure)
        {
            _stream = stream; _onFailure = onFailure; _engine = engine;
            _decoder = engine.CreateDecoder(stream, out _, AudioFormat.DvdHq);
            // FFmpeg 1.4.0 的 SampleFormat 报告源格式，但重采样器遵循 F32 提示；Decode(Span<float>) 返回 F32 PCM。
            Format = new AudioFormat { Format = SampleFormat.F32, SampleRate = _decoder.SampleRate, Channels = _decoder.Channels, Layout = AudioFormat.GetLayoutFromChannels(_decoder.Channels) };
            if (Format.SampleRate <= 0 || Format.Channels <= 0)
            {
                _decoder.Dispose(); throw new InvalidDataException($"音频解码器返回了无效的 PCM 格式：{Format.SampleRate} Hz / {Format.Channels} ch / {Format.Format}。");
            }
            FormatInfo = new SoundFormatInfo { FormatIdentifier = "decoded", FormatName = "Decoded PCM", ChannelCount = Format.Channels, SampleRate = Format.SampleRate, Duration = _decoder.Length > 0 ? TimeSpan.FromSeconds((double)_decoder.Length / Format.Channels / Format.SampleRate) : TimeSpan.Zero };
        }
        public int Position { get; private set; }
        public int Length => _decoder.Length;
        public bool CanSeek => _stream.CanSeek;
        public SampleFormat SampleFormat => Format.Format;
        public int SampleRate => _decoder.SampleRate;
        public bool IsDisposed { get; private set; }
        public SoundFormatInfo? FormatInfo { get; }
        public event EventHandler<EventArgs>? EndOfStreamReached;
        public event EventHandler<PositionChangedEventArgs>? PositionChanged;
        public int ReadBytes(Span<float> buffer)
        {
            if (IsDisposed || _atEnd || _faulted) { buffer.Clear(); return 0; }
            try
            {
                // 解码器可能在读到尾部包时失败。采用有界小块读取限制尾部损失，不缓存整首歌；SoundFlow 支持短读取并自行填充。
                var target = buffer[..Math.Min(buffer.Length, 1024 * Format.Channels)];
                MaxDecodeSamples = Math.Max(MaxDecodeSamples, target.Length); DecodeCalls++;
                var count = _decoder.Decode(target); Position += count;
                PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
                if (count == 0)
                {
                    _atEnd = true;
                    if (Length > 0 && Position + Math.Max(buffer.Length, Format.SampleRate * Format.Channels / 4) < Length)
                    {
                        _faulted = true; _onFailure(new InvalidDataException("音频数据提前结束，文件可能不完整，或插件流已中断。"));
                    }
                    else EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                }
                return count;
            }
            catch (Exception e)
            {
                buffer.Clear(); _atEnd = true;
                // 部分 FLAC 尾部有损坏或冗余数据，FFmpeg 会报告错误而非 EOF。仅距声明末尾 250 ms 内容许正常结束，更早的损坏仍必须报错。
                if (Length > 0 && Position >= Math.Max(0, Length - Format.SampleRate * Format.Channels / 4))
                {
                    var position = Position; var length = Length;
                    ThreadPool.QueueUserWorkItem(_ => AppLog.Warning("Audio.Decode", $"已到音频末尾，忽略尾部解析异常（{position}/{length} 样本）", e));
                    return 0;
                }
                _faulted = true; _onFailure(e); return 0;
            }
        }
        public void Seek(int offset)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            // 新解码器已在起点，避免多余的初始原生跳转；部分文件能顺序解码，却拒绝该跳转。
            if (offset == 0 && Position == 0 && !_atEnd && !_faulted) return;
            if (!CanSeek) throw new IOException("音频源不支持跳转到此位置。");
            if (offset == 0)
            {
                // FFmpeg 1.4.0 原生跳转可能丢失首包；归零时重新创建解码器，以保留首帧并恢复尾部失败的会话。
                _decoder.Dispose(); _stream.Position = 0;
                _decoder = _engine.CreateDecoder(_stream, out _, AudioFormat.DvdHq);
                if (_decoder.SampleRate != Format.SampleRate || _decoder.Channels != Format.Channels) throw new InvalidDataException("重新载入的音频格式已改变，请切换歌曲后重试。");
            }
            else if (!_decoder.Seek(offset)) throw new IOException("音频源不支持跳转到此位置。");
            Position = offset; _atEnd = false; _faulted = false; PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
        }
        public void Dispose() { if (IsDisposed) return; IsDisposed = true; _decoder.Dispose(); }
    }
}

public readonly record struct AudioBufferSnapshot(long LoadedSources, long ReleasedSources, int ActiveSources, int MaxDecodeSamples, long DecodeCalls);
