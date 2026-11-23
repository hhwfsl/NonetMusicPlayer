using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Localization;
using NonetMusicPlayer.Core.Updates;

namespace NonetMusicPlayer.Core.Runtime;

public sealed partial class HeadlessPlayerSession
{
    private static readonly ReleaseUpdateService CliUpdates = new(product: ReleaseProduct.Cli);
    public string? SoftwareVersion { get; init; }

    private async Task<JsonObject> CheckSoftwareUpdateAsync(bool install, CancellationToken token)
    {
        var version = SoftwareVersion ?? throw new InvalidOperationException("The current CLI version is unavailable.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var release = await CliUpdates.CheckAsync(version, cancellationToken: timeout.Token);
        if (!install) return new() { ["current"] = version, ["available"] = release?.Version, ["canInstall"] = release?.Asset is not null };
        if (release is null) throw new InvalidOperationException(LocalizationCatalog.Get("Update.LatestOrNoRelease"));
        var prepared = await CliUpdates.DownloadAsync(release, DataDirectory, cancellationToken: token);
        try
        {
            // 保存并退出后由同一版本的隔离助手安全替换程序文件。
            token.ThrowIfCancellationRequested();
            Save();
            var backup = BackupDirectory;
            using var helper = UpdateInstaller.LaunchHelper(prepared, DataDirectory, backup);
            ExitRequested = true;
            return new() { ["version"] = release.Version, ["scheduled"] = true, ["resultPath"] = Path.Combine(prepared.StageRoot, "result.txt") };
        }
        catch
        {
            ReleaseUpdateService.Discard(prepared);
            throw;
        }
    }
}
