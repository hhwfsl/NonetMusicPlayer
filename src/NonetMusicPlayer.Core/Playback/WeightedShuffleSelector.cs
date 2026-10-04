namespace NonetMusicPlayer.Core.Playback;

/// <summary>
/// 带历史降权的随机选曲器。历史次数只影响权重，不会把某首歌永久排除。
/// 同一歌单中的重复引用按歌曲 ID 去重；有其他候选时绝不返回当前歌曲。
/// </summary>
public sealed class WeightedShuffleSelector
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly Random _random;

    /// <param name="random">可注入固定种子的随机源，以便重复验证概率与边界条件。</param>
    public WeightedShuffleSelector(Random? random = null) => _random = random ?? Random.Shared;

    /// <summary>返回当前历史下的正权重；次数越多，权重越小。</summary>
    public double Weight(string id) => 1d / (1d + _counts.GetValueOrDefault(id));

    /// <summary>选择一个 ID 并记录本次随机命中；空候选返回 null。</summary>
    /// <remarks>次数上限用于避免整型溢出，不会导致权重变成零。</remarks>
    public string? Choose(IEnumerable<string> ids, string? currentId)
    {
        var all = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (all.Length == 0) return null;
        var candidates = all.Length == 1 ? all : all.Where(id => id != currentId).ToArray();
        var sum = candidates.Sum(Weight);
        var draw = _random.NextDouble() * sum;
        var selected = candidates[^1];
        foreach (var id in candidates)
        {
            draw -= Weight(id);
            if (draw < 0) { selected = id; break; }
        }
        _counts[selected] = Math.Min(1_000_000, _counts.GetValueOrDefault(selected) + 1);
        return selected;
    }

    /// <summary>移除已不在曲库中的历史，避免长期播放积累无效 ID。</summary>
    public void Retain(IEnumerable<string> ids)
    {
        var keep = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var id in _counts.Keys.Where(id => !keep.Contains(id)).ToArray()) _counts.Remove(id);
    }
}
