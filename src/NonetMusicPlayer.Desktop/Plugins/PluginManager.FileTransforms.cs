using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>转换发生在扫描之前，文件夹不跟随链接；普通插件及未安装转换器的用户沿用原扫描路径。</summary>
public sealed partial class PluginManager
{
    private IEnumerable<PluginManifest> FileTransformers => Installed.Where(p => p.Enabled && p.Type == "extension"
        && p.ProvidedServices.Contains(FileTransformContract.Service) && p.RequiredCapabilities.Contains(FileTransformContract.Capability)
        && p.Permissions.Contains("file-transform") && p.Permissions.Contains("user-files"));
    public IEnumerable<string> InputFileExtensions => MusicLibraryScanner.SupportedExtensions.Concat(FileTransformers.SelectMany(p => p.FileExtensions)).Distinct(StringComparer.OrdinalIgnoreCase);
    public async Task<string[]> PrepareLocalFilesAsync(IEnumerable<string> input, string purpose, Action<string> warning, CancellationToken token = default)
    {
        var paths = input.ToArray(); var plugins = FileTransformers.OrderBy(p => p.Id).ToArray();
        if (plugins.Length == 0) return paths;
        var accepted = InputFileExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = await Task.Run(() =>
        {
            var found = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var pending = new Stack<(string Path, int Depth)>(paths.Select(p => (Path.GetFullPath(p), 0)));
            while (pending.TryPop(out var item))
            {
                token.ThrowIfCancellationRequested();
                if (item.Depth > 64 || found.Count >= 50000) throw new InvalidDataException(L10n.T("Plugins.FileTransformLimit"));
                if (File.Exists(item.Path)) { if (accepted.Contains(Path.GetExtension(item.Path))) found.Add(item.Path); continue; }
                if (!Directory.Exists(item.Path)) continue;
                if ((File.GetAttributes(item.Path) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var path in Directory.EnumerateFiles(item.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (accepted.Contains(Path.GetExtension(path))) found.Add(path);
                    if (found.Count > 50000) throw new InvalidDataException(L10n.T("Plugins.FileTransformLimit"));
                }
                foreach (var path in Directory.EnumerateDirectories(item.Path)) if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) pending.Push((path, item.Depth + 1));
            }
            return found.ToArray();
        }, token);
        var output = new List<string>(); var failures = new List<string>();
        foreach (var path in files)
        {
            token.ThrowIfCancellationRequested();
            var plugin = plugins.FirstOrDefault(p => p.Enabled && p.FileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
            if (plugin is null) { if (MusicLibraryScanner.SupportedExtensions.Contains(Path.GetExtension(path))) output.Add(path); continue; }
            try
            {
                var response = await Extension(plugin).InvokeProvidedServiceAsync(FileTransformContract.Service,
                    new() { ["method"] = "file.transform", ["inputPath"] = path, ["purpose"] = purpose }, token);
                if (!response.TryGetProperty("success", out var success) || !success.GetBoolean())
                {
                    var message = response.TryGetProperty("message", out var detail) ? detail.GetString() : null;
                    failures.Add(L10n.Format("Plugins.FileTransformFailed", plugin.Name, Path.GetFileName(path), message ?? L10n.T("Common.ActionFailed"))); continue;
                }
                var converted = FileTransformContract.ValidateOutput(path, response.GetProperty("outputPath").GetString() ?? "");
                if (!MusicLibraryScanner.SupportedExtensions.Contains(Path.GetExtension(converted))) throw new InvalidDataException("Unsupported transform output audio.");
                output.Add(converted);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                AppLog.Warning("Plugins", "File transform failed: " + plugin.Id, error);
                failures.Add(L10n.Format("Plugins.FileTransformFailed", plugin.Name, Path.GetFileName(path), L10n.T("Common.ActionFailed")));
            }
        }
        if (failures.Count > 0) warning(string.Join("\n", failures.Take(3)));
        return output.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
    }
}
