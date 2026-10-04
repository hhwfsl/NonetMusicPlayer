using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NonetMusicPlayer.Core.Persistence;

namespace NonetMusicPlayer.Core.Plugins;

public sealed record PluginReleaseAsset(string Name, Uri DownloadUrl, long Size = 0, string? Digest = null);
public sealed class DownloadedPlugin(string path) : IDisposable
{
    public string Path { get; } = path;
    public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
}

/// <summary>仅下载发行附件，不克隆、编译、上传或执行源码；限制地址、大小和重定向。</summary>
public sealed class PluginReleaseDownloader : IDisposable
{
    public const long MaximumBytes = 128L * 1024 * 1024;
    private readonly HttpClient _http;
    public PluginReleaseDownloader(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NonetMusicPlayer/0.2.0");
    }
    public async Task<IReadOnlyList<PluginReleaseAsset>> ResolveAsync(string link, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new InvalidDataException("请输入公开 GitHub Release 的 HTTPS 链接。");
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || segments[2] != "releases" || segments.Take(2).Any(s => !System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z0-9_.-]{1,100}$"))) throw new InvalidDataException("链接需要指向 GitHub Release，而不是仓库首页或源码归档。");
        if (segments.Length >= 6 && segments[3] == "download")
        {
            var name = Uri.UnescapeDataString(segments[^1]); ValidateName(name); return [new(name, uri)];
        }
        var endpoint = segments.Length == 4 && segments[3] == "latest" ? "latest" : segments.Length >= 5 && segments[3] == "tag" ? "tags/" + string.Join('/', segments.Skip(4)) : throw new InvalidDataException("请选择 Release 版本页、latest 页或 .impp 附件链接。");
        using var response = await GetAsync(new Uri($"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/{endpoint}"), true, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("Release 元数据过大。");
        using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false); using var bounded = new MemoryStream(); await CopyBounded(input, bounded, 2 * 1024 * 1024, null, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bounded.ToArray());
        if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 200) throw new InvalidDataException("Release 未提供有效的附件列表。");
        var result = new List<PluginReleaseAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? ""; if (!name.EndsWith(".impp", StringComparison.OrdinalIgnoreCase)) continue; ValidateName(name);
            var download = new Uri(asset.GetProperty("browser_download_url").GetString()!); ValidateDownloadUri(download);
            var size = asset.GetProperty("size").GetInt64(); if (size is <= 0 or > MaximumBytes) continue;
            result.Add(new(name, download, size, asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null));
        }
        if (result.Count == 0) throw new InvalidDataException("此 Release 没有可安装的 .impp 附件，或附件超过 128 MB。");
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        return result.OrderByDescending(asset => asset.Name.Contains(rid, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public async Task<DownloadedPlugin> DownloadAsync(PluginReleaseAsset asset, string pluginsFolder, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateName(asset.Name); ValidateDownloadUri(asset.DownloadUrl);
        var folder = System.IO.Path.Combine(pluginsFolder, ".downloads"); PluginPathPolicy.RejectLinkedAncestors(folder); Directory.CreateDirectory(folder);
        var path = System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + ".impp");
        try
        {
            using var response = await GetAsync(asset.DownloadUrl, false, cancellationToken).ConfigureAwait(false);
            var length = response.Content.Headers.ContentLength; if (length > MaximumBytes) throw new InvalidDataException("插件下载超过 128 MB。");
            var total = length is > 0 ? length : asset.Size > 0 ? asset.Size : (long?)null;
            using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await CopyBounded(input, output, MaximumBytes, count => progress?.Report(total is > 0 ? Math.Min(99, count * 100d / total.Value) : 0), cancellationToken).ConfigureAwait(false);
            var actual = new FileInfo(path).Length;
            if (actual == 0 || asset.Size > 0 && actual != asset.Size || length is > 0 && actual != length) throw new InvalidDataException("插件下载不完整，请重试。");
            if (asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            {
                using var input = File.OpenRead(path); var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
                if (!hash.Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("插件 SHA-256 校验失败。");
            }
            progress?.Report(100); return new DownloadedPlugin(path);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, bool api, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (api) { if (uri.Scheme != "https" || uri.Host != "api.github.com") throw new InvalidDataException("不安全的 Release API 重定向。"); }
            else ValidateDownloadUri(uri);
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } target) { uri = target.IsAbsoluteUri ? target : new Uri(uri, target); response.Dispose(); continue; }
            if (!response.IsSuccessStatusCode) { var status = response.StatusCode; response.Dispose(); throw new HttpRequestException(status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests ? "GitHub 访问受限，请稍后重试或使用文件导入。" : "无法下载插件，HTTP " + (int)status); }
            return response;
        }
        throw new InvalidDataException("插件下载重定向次数过多。");
    }
    private static void ValidateDownloadUri(Uri uri)
    {
        if (uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !(uri.Host == "github.com" || uri.Host == "githubusercontent.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal))) throw new InvalidDataException("插件下载只允许 GitHub 的 HTTPS 附件地址。");
    }
    private static void ValidateName(string name) { if (name.Length > 200 || name != System.IO.Path.GetFileName(name) || name.Contains('\\') || name.Any(char.IsControl) || !name.EndsWith(".impp", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("插件附件名称不合法。"); }
    private static async Task CopyBounded(Stream source, Stream target, long maximum, Action<long>? report, CancellationToken token)
    {
        var buffer = new byte[65536]; long count = 0; int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { count += read; if (count > maximum) throw new InvalidDataException("下载内容超过大小限制。"); await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); report?.Invoke(count); }
    }
    public void Dispose() => _http.Dispose();
}
