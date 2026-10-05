using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>只读取公开 Release；下载包必须同时通过 GitHub 摘要和包内逐文件摘要校验。</summary>
public sealed class ReleaseUpdateService(HttpClient? client = null)
{
    public const string Repository = "https://github.com/hhwfsl/NonetMusicPlayer";
    public const string ManifestName = "NonetMusicPlayer.update.json";
    private const long DownloadLimit = 512L * 1024 * 1024, ExpandedLimit = 2L * 1024 * 1024 * 1024;
    private readonly HttpClient _http = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
    public static string CurrentVersion => typeof(ReleaseUpdateService).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0];
    public static string PlatformRid => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>按版本与平台筛选桌面 Release；不存在的仓库视为尚无可用发布。</summary>
    public async Task<UpdateRelease?> CheckAsync(string current, string? rid = null, CancellationToken cancellationToken = default)
    {
        rid ??= PlatformRid;
        using var request = Request("https://api.github.com/repos/hhwfsl/NonetMusicPlayer/releases?per_page=50");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(response.Content, 4 * 1024 * 1024, cancellationToken);
        var releases = JsonSerializer.Deserialize(bytes, UpdateJsonContext.Default.GitHubReleaseArray) ?? [];
        var candidates = releases.Where(r => !r.Draft && r.TagName is { Length: <= 100 } && r.Assets?.Any(a => DesktopAsset(a.Name, rid)) == true)
            .Select(r => (Release: r, Version: NormalizeVersion(r.TagName))).Where(r => r.Version is not null)
            .OrderByDescending(r => r.Version, Comparer<string?>.Create(CompareVersions));
        foreach (var candidate in candidates)
        {
            if (CompareVersions(candidate.Version, current) <= 0) continue;
            var asset = candidate.Release.Assets!.FirstOrDefault(a => DesktopAsset(a.Name, rid) && a.Size is > 0 and <= DownloadLimit && ValidDigest(a.Digest) && TrustedAssetUrl(a.BrowserDownloadUrl));
            return new(candidate.Version!, candidate.Release.Body is { Length: <= 100_000 } notes ? notes : "", asset, rid);
        }
        return null;
    }
    private static bool DesktopAsset(string? name, string rid) => name is not null && name.StartsWith("NonetMusicPlayer.Desktop-", StringComparison.OrdinalIgnoreCase) && name.EndsWith("-" + rid + ".zip", StringComparison.OrdinalIgnoreCase);
    private static bool ValidDigest(string? value) => value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && value[7..].All(Uri.IsHexDigit);
    private static bool TrustedAssetUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/hhwfsl/NonetMusicPlayer/releases/download/", StringComparison.Ordinal);
    private static HttpRequestMessage Request(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("NonetMusicPlayer/" + CurrentVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28"); return request;
    }
    /// <summary>流式下载到独立暂存目录，验证摘要后解包；取消或失败仅清理本次暂存。</summary>
    public async Task<PreparedUpdate> DownloadAsync(UpdateRelease release, string dataRoot, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var asset = release.Asset ?? throw new InvalidDataException(L10n.T("Update.NoPackage"));
        if (!TrustedAssetUrl(asset.BrowserDownloadUrl) || !ValidDigest(asset.Digest)) throw new InvalidDataException("Untrusted update asset");
        var folder = Path.Combine(Path.GetFullPath(dataRoot), "Updates", Guid.NewGuid().ToString("N"));
        DataDirectoryService.RejectLinkedAncestors(folder); Directory.CreateDirectory(folder);
        var archivePath = Path.Combine(folder, "download.zip");
        try
        {
            using var request = Request(asset.BrowserDownloadUrl!);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); response.EnsureSuccessStatusCode();
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || finalUri.Scheme != "https" || !(finalUri.Host == "github.com" || finalUri.Host == "release-assets.githubusercontent.com" || finalUri.Host == "objects.githubusercontent.com")) throw new InvalidDataException("Unexpected download redirect");
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size) throw new InvalidDataException("Update size mismatch");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count; if (total > DownloadLimit || total > asset.Size) throw new InvalidDataException("Update exceeds declared size");
                    hash.AppendData(buffer, 0, count); await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken); progress?.Report((double)total / asset.Size);
                }
                if (total != asset.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(asset.Digest![7..]))) throw new InvalidDataException("Update SHA-256 mismatch");
            }
            return await Task.Run(() => ExtractVerified(archivePath, folder, release.Version, release.Rid, cancellationToken), cancellationToken);
        }
        catch
        {
            // 只清理本次操作创建的专用暂存目录，不触及安装目录及用户数据。
            DataDirectoryService.RejectLinkedAncestors(folder); Directory.Delete(folder, true); throw;
        }
    }
    /// <summary>取消恰好发生在解压完成之后时，也回收本次 GUID 暂存，绝不触及安装目录。</summary>
    public static void Discard(PreparedUpdate update)
    {
        var stage = Path.GetFullPath(update.StageRoot);
        if (Path.GetFileName(Path.GetDirectoryName(stage)) != "Updates" || !Guid.TryParseExact(Path.GetFileName(stage), "N", out _)
            || Path.GetFullPath(update.Payload) != Path.Combine(stage, "payload")) throw new InvalidDataException("Invalid update stage");
        DataDirectoryService.RejectLinkedAncestors(stage);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
    }
    /// <summary>校验安全相对路径、平台清单和逐文件摘要，不允许更新包携带用户数据。</summary>
    public static PreparedUpdate ExtractVerified(string archivePath, string stageRoot, string version, string rid, CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 2000) throw new InvalidDataException("Too many update entries");
        var manifestEntry = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("Update manifest missing");
        if (manifestEntry.Length > 512 * 1024) throw new InvalidDataException("Update manifest too large");
        using var input = manifestEntry.Open(); var manifest = JsonSerializer.Deserialize(input, UpdateJsonContext.Default.UpdateManifest) ?? throw new InvalidDataException("Invalid update manifest");
        if (manifest.Schema != 1 || manifest.Version != version || manifest.Rid != rid || manifest.Files.Count is < 1 or > 1900) throw new InvalidDataException("Update manifest does not match release");
        var payload = Path.Combine(stageRoot, "payload"); DataDirectoryService.RejectLinkedAncestors(payload); Directory.CreateDirectory(payload);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/') && e.FullName != ManifestName))
        {
            cancellationToken.ThrowIfCancellationRequested(); ValidateRelative(entry.FullName);
            if (!seen.Add(entry.FullName) || !manifest.Files.TryGetValue(entry.FullName, out var digest) || digest.Length != 64 || !digest.All(Uri.IsHexDigit) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("Unlisted, duplicate or linked update file");
            total += entry.Length; if (total > ExpandedLimit) throw new InvalidDataException("Expanded update exceeds size limit");
            var target = Path.Combine(payload, entry.FullName.Replace('/', Path.DirectorySeparatorChar)); DataDirectoryService.RejectLinkedAncestors(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, false);
            using var stream = File.OpenRead(target); if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(digest))) throw new InvalidDataException("Update file SHA-256 mismatch");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, (UnixFileMode)(entry.ExternalAttributes >> 16 & 0x1FF) | UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        if (seen.Count != manifest.Files.Count) throw new InvalidDataException("Update files missing");
        var executable = rid.StartsWith("osx-") ? "Contents/MacOS/Nonet" : rid.StartsWith("win-") ? "Nonet.exe" : "Nonet";
        if (!manifest.Files.ContainsKey(executable)) throw new InvalidDataException("Desktop executable missing");
        return new(stageRoot, payload, manifest, executable);
    }
    /// <summary>拒绝路径穿越、重解析文件、设备名及受保护的数据目录。</summary>
    public static void ValidateRelative(string relative)
    {
        var parts = relative.Split('/');
        if (relative.Length is < 1 or > 240 || relative.Contains('\\') || Path.IsPathRooted(relative) || parts.Any(p => p.Length == 0 || p is "." or ".." || p.TrimEnd(' ', '.') != p || p.Any(c => c < 32 || ":*?<>|\"".Contains(c)))) throw new InvalidDataException("Unsafe update path");
        var reserved = new[] { "Data", "Plugins", "Lyrics", "Artwork", "Backgrounds", "Backups", "Logs", "Fonts", "Updates", "Nonet.bootstrap.json" };
        if (parts.Any(p => reserved.Contains(p, StringComparer.OrdinalIgnoreCase) || p.EndsWith(".bootstrap.json", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Update cannot contain user data");
        if (parts.Any(p => p.Split('.')[0].ToUpperInvariant() is "CON" or "NUL" or "AUX" or "PRN" || System.Text.RegularExpressions.Regex.IsMatch(p, "^(COM|LPT)[1-9](\\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) throw new InvalidDataException("Reserved update path");
    }
    internal static string? NormalizeVersion(string? value)
    {
        value = value?.Replace("desktop-v", "", StringComparison.OrdinalIgnoreCase).TrimStart('v');
        if (value is null || value.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?$")) return null;
        // Release 标签是外部输入，超出整数范围的版本号不应导致整个检查失败。
        return Version.TryParse(value.Split('-', 2)[0], out _) ? value : null;
    }
    /// <summary>比较主版本与数字型预发布段，稳定版高于同版本的预发布版。</summary>
    public static int CompareVersions(string? left, string? right)
    {
        if (NormalizeVersion(left) is not { } l || NormalizeVersion(right) is not { } r) return 0;
        var a = l.Split('-', 2); var b = r.Split('-', 2); var result = Version.Parse(a[0]).CompareTo(Version.Parse(b[0])); if (result != 0) return result;
        if (a.Length != b.Length) return a.Length == 1 ? 1 : -1;
        if (a.Length == 1) return 0;
        var x = a[1].Split('.'); var y = b[1].Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var xn = long.TryParse(x[i], out var xi); var yn = long.TryParse(y[i], out var yi);
            result = xn && yn ? xi.CompareTo(yi) : xn != yn ? xn ? -1 : 1 : string.CompareOrdinal(x[i], y[i]); if (result != 0) return result;
        }
        return x.Length.CompareTo(y.Length);
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        await using var source = await content.ReadAsStreamAsync(token); using var destination = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0) { if (destination.Length + count > limit) throw new InvalidDataException("Release response too large"); destination.Write(buffer, 0, count); }
        return destination.ToArray();
    }
}
public sealed record UpdateRelease(string Version, string Notes, GitHubAsset? Asset, string Rid);
public sealed record PreparedUpdate(string StageRoot, string Payload, UpdateManifest Manifest, string Executable);
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
}
public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}
public sealed class UpdateManifest
{
    public int Schema { get; set; } = 1;
    public string Version { get; set; } = "";
    public string Rid { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(GitHubRelease[]))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(UpdateApplyPlan))]
internal partial class UpdateJsonContext : JsonSerializerContext;
