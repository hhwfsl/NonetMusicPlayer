using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Network;

namespace NonetMusicPlayer.Core.Updates;

public sealed partial class ReleaseUpdateService
{
    private const string ReleaseTagPath = "/hhwfsl/NonetMusicPlayer/releases/tag/";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private async Task<UpdateRelease?> CheckPublicFeedAsync(string current, string rid, CancellationToken token)
    {
        try
        {
            var feed = await ReadPublicTagsAsync(token);
            var candidates = product == ReleaseProduct.Desktop
                ? feed.Where(tag => CompareVersions(tag.Version, current) > 0)
                    .OrderByDescending(tag => tag.Version, Comparer<string>.Create(CompareVersions)).ToArray()
                : feed;
            var readChecksum = false;
            foreach (var tag in candidates)
            {
                var checksumText = await ReadReleaseChecksumsAsync(tag.Tag, token);
                if (checksumText is null) continue;
                readChecksum = true;
                var matches = ParseChecksums(checksumText, tag.Version, rid);
                var asset = matches.OrderByDescending(item => item.Version, Comparer<string>.Create(CompareVersions)).FirstOrDefault();
                if (asset.Name is null) continue;
                // CLI 版号来自实际资产，同一合并 Release 的桌面标签不代表 CLI 版号。
                var compared = CompareVersions(asset.Version, current);
                if (compared > 0)
                    return new(asset.Version, PublicNotes(tag.Tag), new GitHubAsset
                    {
                        Name = asset.Name, BrowserDownloadUrl = AssetLink(tag.Tag, asset.Name), Digest = "sha256:" + asset.Hash
                    }, rid);
                if (compared == 0) return null;
            }
            if (product == ReleaseProduct.Desktop && candidates.Length > 0)
                return new(candidates[0].Version, PublicNotes(candidates[0].Tag), null, rid);
            if (product == ReleaseProduct.Cli && !readChecksum)
                throw new InvalidDataException("No public release checksum could be read.");
            return null;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or XmlException or InvalidDataException)
        {
            throw new GitHubRateLimitException(error);
        }
    }

    private async Task<(string Tag, string Version)[]> ReadPublicTagsAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Repository + "/releases.atom");
        request.Headers.UserAgent.ParseAdd("NonetMusicPlayer");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is not { } final || !IsPublicHttps(final)
            || final.Host != "github.com" || final.AbsolutePath != "/hhwfsl/NonetMusicPlayer/releases.atom")
            throw new InvalidDataException("Untrusted release feed.");
        var bytes = await ReadBoundedAsync(response.Content, 1024 * 1024, token);
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != Atom + "feed") throw new InvalidDataException("Invalid GitHub release feed.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tags = new List<(string Tag, string Version)>();
        foreach (var entry in root.Elements(Atom + "entry").Take(20))
        {
            var href = entry.Elements(Atom + "link").FirstOrDefault(link => (string?)link.Attribute("rel") == "alternate")?.Attribute("href")?.Value;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var url) || !IsPublicHttps(url) || url.Host != "github.com" || url.Query.Length != 0
                || !url.AbsolutePath.StartsWith(ReleaseTagPath, StringComparison.Ordinal)) continue;
            var tag = Uri.UnescapeDataString(url.AbsolutePath[ReleaseTagPath.Length..]);
            var version = NormalizeVersion(tag.StartsWith("cli-v", StringComparison.Ordinal) ? tag[4..] : tag);
            if (version is null || !(tag == version || tag == "v" + version || tag == "desktop-v" + version
                || product == ReleaseProduct.Cli && tag == "cli-v" + version) || !seen.Add(tag)) continue;
            tags.Add((tag, version));
        }
        if (tags.Count == 0 && root.Elements(Atom + "entry").Any()) throw new InvalidDataException("No recognized release tags in the public feed.");
        return tags.ToArray();
    }

    private async Task<string?> ReadReleaseChecksumsAsync(string tag, CancellationToken token)
    {
        using var response = await GetReleaseAssetAsync(AssetLink(tag, "SHA256SUMS.txt"), token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return Encoding.UTF8.GetString(await ReadBoundedAsync(response.Content, 128 * 1024, token));
    }

    private List<(string Version, string Name, string Hash)> ParseChecksums(string text, string tagVersion, string rid)
    {
        var result = new List<(string Version, string Name, string Hash)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length < 67 || !char.IsWhiteSpace(line[64]) || !line[..64].All(Uri.IsHexDigit)) continue;
            var name = line[64..].TrimStart().TrimStart('*');
            var version = AssetVersion(tagVersion, name, rid);
            var prefix = product == ReleaseProduct.Cli ? "NonetMusicPlayerCli-" : "NonetMusicPlayer.Desktop-";
            if (version is null || name != prefix + version + "-" + rid + ".zip") continue;
            if (!seen.Add(name)) throw new InvalidDataException("Duplicate file in the public release checksum list.");
            result.Add((version, name, line[..64]));
        }
        return result;
    }

    private static string PublicNotes(string tag) => LocalizationCatalog.Format("Update.PublicReleaseNotes", Repository + "/releases/tag/" + Uri.EscapeDataString(tag));
    private static string AssetLink(string tag, string name) => Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name);

    private async Task<HttpResponseMessage> GetReleaseAssetAsync(string url, CancellationToken token)
    {
        if (!TrustedAssetUrl(url)) throw new InvalidDataException("Untrusted release asset URL.");
        var next = new Uri(url);
        for (var hop = 0; hop < 6; hop++)
        {
            using var request = Request(next.AbsoluteUri);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.RequestMessage?.RequestUri is not { } final || !IsAssetHost(final))
            {
                response.Dispose();
                throw new InvalidDataException("Unexpected download redirect.");
            }
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || !Uri.TryCreate(final, location, out var target) || !IsAssetHost(target))
                throw new InvalidDataException("Unexpected download redirect.");
            next = target;
        }
        throw new InvalidDataException("Too many release redirects.");
    }

    private static bool IsAssetHost(Uri uri) => IsPublicHttps(uri) && (TrustedAssetUrl(uri.AbsoluteUri)
        || uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com");
}
