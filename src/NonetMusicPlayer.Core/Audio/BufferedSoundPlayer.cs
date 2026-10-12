using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace NonetMusicPlayer.Core.Audio;

/// <summary>暂时缺少 PCM 时仅输出静音，SoundFlow 的样本时钟和 EOF 处理仍保持真实语义。</summary>
internal sealed class BufferedSoundPlayer(AudioEngine engine, AudioFormat format, BufferedPcmDataProvider data)
    : SoundPlayerBase(engine, format, data)
{
    public override string Name { get; set; } = "Buffered music stream";
    protected override void GenerateAudio(Span<float> output, int channels)
    {
        if (!data.CanRender(output.Length)) { output.Clear(); return; }
        base.GenerateAudio(output, channels);
    }
}
