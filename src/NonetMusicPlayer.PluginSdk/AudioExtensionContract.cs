namespace NonetMusicPlayer.Core.Plugins;

/// <summary>稳定的交错 Float32 PCM 格式，不暴露解码器、设备句柄或播放时钟。</summary>
public readonly record struct ExtensionPcmFormat(int SampleRate, int Channels);

/// <summary>可选可信托管音效工厂；实时采样不能通过 JSON-RPC 传输。</summary>
public interface INonetAudioExtension
{
    INonetPcmProcessor CreateProcessor();
}

/// <summary>实时处理必须快速、同步且无文件/网络/宿主回调；Native AOT 原生库可由可信托管桥接调用。</summary>
public interface INonetPcmProcessor : IDisposable
{
    void Process(Span<float> samples, ExtensionPcmFormat format);
}

/// <summary>音频线程与插件卸载之间的使用租约，撤销后不再进入处理器，等待正在执行的回调结束。</summary>
public sealed class ExtensionPcmLease(INonetPcmProcessor processor)
{
    private int _enabled = 1, _active;
    public bool Enabled => Volatile.Read(ref _enabled) == 1;
    public bool TryEnter()
    {
        if (!Enabled) return false;
        Interlocked.Increment(ref _active);
        if (Enabled) return true;
        Interlocked.Decrement(ref _active); return false;
    }
    public void Exit() => Interlocked.Decrement(ref _active);
    public void Revoke() => Interlocked.Exchange(ref _enabled, 0);
    public void Process(Span<float> samples, ExtensionPcmFormat format) => processor.Process(samples, format);
    public async Task ReleaseAsync()
    {
        Revoke();
        // 非合作处理器无法安全强制析构；等待只发生在后台，不阻塞界面或把 DLL 提前卸载。
        while (Volatile.Read(ref _active) != 0) await Task.Delay(20).ConfigureAwait(false);
        processor.Dispose();
    }
}
