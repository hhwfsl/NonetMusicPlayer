using System.Globalization;
using System.Net;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayer.Core.Network;

public sealed class GitHubRateLimitException : HttpRequestException
{
    internal GitHubRateLimitException(Exception? inner = null)
        : base(LocalizationCatalog.Get(inner is null ? "Update.GitHubApiRateLimited" : "Update.GitHubPublicFeedFailed"), inner, HttpStatusCode.TooManyRequests) { }
}

/// <summary>软件与插件共用 GitHub 匿名配额的冷却时间，后台任务不持续重试。</summary>
internal sealed class GitHubApiCooldown
{
    public static GitHubApiCooldown Shared { get; } = new();
    private long _retryTicks;
    public bool IsActive => Volatile.Read(ref _retryTicks) > DateTimeOffset.UtcNow.UtcTicks;

    public bool Observe(HttpResponseMessage response)
    {
        var exhausted = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0");
        var retry = response.Headers.RetryAfter;
        var limited = response.StatusCode == HttpStatusCode.TooManyRequests
            || response.StatusCode == HttpStatusCode.Forbidden
            && (exhausted || retry is not null || response.ReasonPhrase?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true);
        if (!limited && !(exhausted && response.IsSuccessStatusCode)) return false;

        var now = DateTimeOffset.UtcNow;
        var until = retry?.Date ?? (retry?.Delta is { } delay && delay <= DateTimeOffset.MaxValue - now ? now + delay : now);
        if (exhausted && response.Headers.TryGetValues("X-RateLimit-Reset", out var resets)
            && long.TryParse(resets.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds is >= 0 and <= 253402300799)
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (reset > until) until = reset;
        }
        if (until <= now) until = now.AddMinutes(1);
        var previous = Volatile.Read(ref _retryTicks);
        while (previous < until.UtcTicks)
        {
            var observed = Interlocked.CompareExchange(ref _retryTicks, until.UtcTicks, previous);
            if (observed == previous) break;
            previous = observed;
        }
        return limited;
    }
}
