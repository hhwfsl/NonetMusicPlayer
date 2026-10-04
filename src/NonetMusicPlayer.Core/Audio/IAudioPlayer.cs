namespace NonetMusicPlayer.Core.Audio;

/// <summary>无界面的音频契约；后端事件可来自工作线程，调用方负责切回自己的调度器。</summary>
public interface IAudioPlayer : IDisposable
{
    bool IsAvailable { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    float Volume { get; set; }
    long PlaybackGeneration => 0;
    string DeviceName { get => "Playback.SystemDefault"; set { } }
    IReadOnlyList<string> Devices => ["Playback.SystemDefault"];
    event EventHandler<AudioOutputChangedEventArgs>? OutputDeviceChanged { add { } remove { } }
    event EventHandler? OutputDevicesChanged { add { } remove { } }

    event EventHandler? PlaybackStopped;
    event EventHandler<AudioPlaybackErrorEventArgs>? PlaybackFailed { add { } remove { } }

    Task LoadAsync(string filePath, CancellationToken cancellationToken = default);
    void Play();
    void Pause();
    void Stop();
}

public sealed class AudioOutputChangedEventArgs(string deviceName, double position, bool available, long generation) : EventArgs
{
    public string DeviceName { get; } = deviceName;
    public double Position { get; } = position;
    public bool Available { get; } = available;
    public long PlaybackGeneration { get; } = generation;
}
public sealed class AudioOutputUnavailableException(string message, Exception? inner = null) : IOException(message, inner);

public sealed class AudioPlaybackEndedEventArgs(long playbackGeneration) : EventArgs
{
    public long PlaybackGeneration { get; } = playbackGeneration;
}

public sealed class AudioPlaybackErrorEventArgs(string errorCode, string message, long playbackGeneration = 0) : EventArgs
{
    public string ErrorCode { get; } = errorCode;
    public string Message { get; } = message;
    public long PlaybackGeneration { get; } = playbackGeneration;
}

