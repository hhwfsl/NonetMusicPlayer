namespace NonetMusicPlayer.Core.Plugins;

/// <summary>仓库坐标只允许账户/仓库两个安全段，不能拼接任意网络地址。</summary>
public sealed record PluginRepository(string Owner, string Name)
{
    public string Coordinate => Owner + "/" + Name;
    public string LatestReleaseUrl => "https://github.com/" + Coordinate + "/releases/latest";
    public static void Validate(string owner, string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(owner, "^[A-Za-z0-9][A-Za-z0-9-]{0,38}$")
            || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}$")
            || name is "." or "..") throw new InvalidDataException("GitHub 仓库需要合法的 repositoryOwner 和 repositoryName。");
    }
    public static PluginRepository Parse(string coordinate)
    {
        var parts = coordinate.Split('/');
        if (parts.Length != 2) throw new InvalidDataException("GitHub 仓库坐标应为 owner/repository。");
        Validate(parts[0], parts[1]); return new(parts[0], parts[1]);
    }
    public static PluginRepository ForUpdate(PluginManifest manifest)
    {
        // 从远程导入的包优先使用实际来源；本地导入按开发者清单构造请求。
        if (!string.IsNullOrEmpty(manifest.OriginRepository)) return Parse(manifest.OriginRepository);
        Validate(manifest.RepositoryOwner, manifest.RepositoryName); return new(manifest.RepositoryOwner, manifest.RepositoryName);
    }
}

public enum PluginInstallKind { New, SameVersion, Upgrade, Downgrade }

/// <summary>安装、拖入和远程更新共用身份判断；数字版本补齐后比较，不依赖包文件名。</summary>
public static class PluginUpdatePolicy
{
    public static PluginInstallKind Evaluate(PluginManifest? installed, PluginManifest candidate)
    {
        candidate.Validate();
        if (installed is null) return PluginInstallKind.New;
        if (installed.Id != candidate.Id || installed.Type != candidate.Type || installed.ContractVersion != candidate.ContractVersion)
            throw new InvalidDataException(PluginMessages.Get("Plugins.UpdateIdentityMismatch"));
        static Version Normalize(string value)
        {
            var version = Version.Parse(value); return new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }
        var comparison = Normalize(candidate.Version).CompareTo(Normalize(installed.Version));
        return comparison == 0 ? PluginInstallKind.SameVersion : comparison > 0 ? PluginInstallKind.Upgrade : PluginInstallKind.Downgrade;
    }
}
