using System.Buffers;
using System.Diagnostics;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Core.Plugins;
using SoundFlow.Abstracts;

namespace NonetMusicPlayer.Core.Audio;

/// <summary>可选音效链只处理当次小缓冲。异常、非法样本或超时回退原缓冲并禁用该处理器。</summary>
public sealed class ExtensionAudioPipeline : SoundModifier
{
    private ExtensionPcmLease[] _processors = [];
    public event EventHandler? ProcessorBypassed;
    public int SampleRate { get; set; } = 48000;
    public void SetProcessors(IEnumerable<ExtensionPcmLease> processors) => Volatile.Write(ref _processors, processors.Take(8).ToArray());
    public override float ProcessSample(float sample, int channel) => sample;
    public override void Process(Span<float> buffer, int channels)
    {
        var processors = Volatile.Read(ref _processors);
        if (processors.Length == 0 || buffer.IsEmpty || channels <= 0) return;
        var saved = ArrayPool<float>.Shared.Rent(buffer.Length);
        try
        {
            foreach (var processor in processors)
            {
                if (!processor.TryEnter()) continue;
                buffer.CopyTo(saved); var started = Stopwatch.GetTimestamp();
                try
                {
                    processor.Process(buffer, new(SampleRate, channels));
                    var budget = Math.Max(.002, buffer.Length / (double)(SampleRate * channels) * .75);
                    if (Stopwatch.GetElapsedTime(started).TotalSeconds > budget) throw new InvalidDataException("PCM processor exceeded real-time budget.");
                    for (var i = 0; i < buffer.Length; i++)
                    {
                        if (!float.IsFinite(buffer[i])) throw new InvalidDataException("PCM processor returned invalid samples.");
                        buffer[i] = Math.Clamp(buffer[i], -1, 1);
                    }
                }
                catch (Exception error)
                {
                    saved.AsSpan(0, buffer.Length).CopyTo(buffer); processor.Revoke();
                    // 只在首次故障时后台记录，实时线程不执行日志 IO。
                    ThreadPool.QueueUserWorkItem(_ => { AppLog.Warning("PluginAudio", "Processor bypassed; original audio preserved.", error); try { ProcessorBypassed?.Invoke(this, EventArgs.Empty); }
                        catch (Exception notificationError) { AppLog.Warning("PluginAudio", "Bypass notification failed.", notificationError); } });
                }
                finally { processor.Exit(); }
            }
        }
        finally { ArrayPool<float>.Shared.Return(saved); }
    }
}

public interface IExtensionAudioHost
{
    void SetExtensionProcessors(IReadOnlyList<ExtensionPcmLease> processors);
    event EventHandler? ProcessorBypassed;
}
