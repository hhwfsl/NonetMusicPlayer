namespace NonetMusicPlayer.Core.Plugins;

/// <summary>歌词进程只接收元数据与候选标识，音频路径及文件写入留在宿主。</summary>
public sealed record LyricsQuery(string Title, string Artist = "", string Album = "", double DurationSeconds = 0);
public sealed record LyricsCandidate(string Source, string Id, string Title, string Artist, string Album, double DurationSeconds,
    string Token = "", double Score = 0)
{
    public override string ToString() => $"{Title} — {Artist} · {Source} · {DurationSeconds / 60:0}:{DurationSeconds % 60:00}";
}
public sealed record LyricsSearchResult(IReadOnlyList<LyricsCandidate> Candidates, IReadOnlyList<string> Warnings);
public sealed record LyricsFetchResult(string Text, string Format, string Source, bool WordTimed, string Warning = "");
