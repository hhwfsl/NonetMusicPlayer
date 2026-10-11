using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>有界、静音的视频帧管道；只启动随应用分发的解码器，不执行 Shell 或网络输入。</summary>
public static class BackgroundVideoDecoder
{
    public const int MaximumWidth = 1280, MaximumHeight = 720, FramesPerSecond = 24;
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "VideoDecoder", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    public static ProcessStartInfo StartInfo(string path, bool loop, bool paced, string? executable = null)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException(L10n.T("Background.MediaMissing"));
        var info = new ProcessStartInfo(executable ?? ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe", "-threads", "2", "-filter_threads", "1" }) info.ArgumentList.Add(argument);
        if (loop) { info.ArgumentList.Add("-stream_loop"); info.ArgumentList.Add("-1"); }
        if (paced) info.ArgumentList.Add("-re");
        // 强制本地容器，避免伪装成媒体文件的播放列表引用其他文件或网络资源。
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        { ".mp4" or ".m4v" or ".mov" => "mov", ".mkv" or ".webm" => "matroska", ".avi" => "avi", ".gif" => "gif", ".webp" => "webp_pipe", ".png" => "apng", _ => throw new InvalidDataException(L10n.T("Background.UnsupportedMedia")) };
        foreach (var argument in new[] { "-f", format, "-i", path, "-map", "0:v:0", "-an", "-sn", "-dn", "-threads", "2", "-vf", "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease,fps=24", "-c:v", "pam", "-pix_fmt", "rgba" }) info.ArgumentList.Add(argument);
        if (!loop) { info.ArgumentList.Add("-frames:v"); info.ArgumentList.Add("1"); }
        foreach (var argument in new[] { "-f", "image2pipe", "pipe:1" }) info.ArgumentList.Add(argument);
        return info;
    }
    public static async Task ValidateAsync(string path, CancellationToken token = default, string? executable = null)
    {
        if (AnimatedRasterDecoder.Handles(path))
        { await Task.Run(() => { token.ThrowIfCancellationRequested(); using var reader = new AnimatedRasterDecoder(path); using var frame = reader.ReadFrame(0); }, token); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = Process.Start(StartInfo(path, false, false, executable)) ?? throw new IOException(L10n.T("Background.DecoderUnavailable"));
        using var kill = timeout.Token.Register(() => Kill(process));
        var errors = DrainErrorsAsync(process.StandardError, timeout.Token);
        try
        {
            using var frame = await ReadFrameAsync(process.StandardOutput.BaseStream, timeout.Token);
            if (frame is null) throw new InvalidDataException(L10n.T("Background.UnsupportedMedia"));
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new InvalidDataException(L10n.T("Background.UnsupportedMedia"));
            await errors;
        }
        finally { Kill(process); try { await errors; } catch (OperationCanceledException) { } }
    }
    public static void Kill(Process process) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
    public static async Task DrainErrorsAsync(StreamReader reader, CancellationToken token)
    {
        // 持续排空 stderr，但不缓存媒体内容、元数据或无限增长的诊断输出。
        var buffer = new char[1024]; while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { }
    }
    public static async Task<VideoFrame?> ReadFrameAsync(Stream input, CancellationToken token)
    {
        var one = new byte[1]; var line = new StringBuilder(); var lines = new List<string>(); var bytes = 0;
        while (true)
        {
            if (await input.ReadAsync(one, token) == 0) { if (bytes == 0) return null; throw new EndOfStreamException(); }
            if (++bytes > 512) throw new InvalidDataException("Invalid video frame header.");
            if (one[0] == '\n') { var value = line.ToString().TrimEnd('\r'); lines.Add(value); line.Clear(); if (value == "ENDHDR") break; }
            else line.Append((char)one[0]);
        }
        int Field(string name) => int.Parse(lines.Single(l => l.StartsWith(name + " ", StringComparison.Ordinal))[(name.Length + 1)..], CultureInfo.InvariantCulture);
        if (lines[0] != "P7" || Field("DEPTH") != 4 || Field("MAXVAL") != 255) throw new InvalidDataException("Invalid video pixel format.");
        var width = Field("WIDTH"); var height = Field("HEIGHT");
        if (width is < 1 or > MaximumWidth || height is < 1 or > MaximumHeight) throw new InvalidDataException("Video frame exceeds bounds.");
        var buffer = ArrayPool<byte>.Shared.Rent(checked(width * height * 4));
        try { await input.ReadExactlyAsync(buffer.AsMemory(0, width * height * 4), token); return new(width, height, buffer); }
        catch { ArrayPool<byte>.Shared.Return(buffer); throw; }
    }
}
public sealed class VideoFrame(int width, int height, byte[] pixels) : IDisposable
{
    private int _disposed;
    public int Width { get; } = width; public int Height { get; } = height; public byte[] Pixels { get; } = pixels;
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) ArrayPool<byte>.Shared.Return(Pixels); }
}
