using System.Security.Cryptography;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

public static class AppFontService
{
    public const long MaximumBytes = 32 * 1024 * 1024;
    private const string Fallback = "avares://Avalonia.Fonts.Inter/Assets#Inter, Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC";
    private static readonly Dictionary<string, string> Registered = new(StringComparer.Ordinal);
    public static string Import(AppStorage storage, string source)
    {
        var file = new FileInfo(source); var extension = file.Extension.ToLowerInvariant();
        if (extension is not (".ttf" or ".otf" or ".ttc")) throw new InvalidDataException(L10n.T("Settings.ChooseATTFOTFOrTTCFontFile"));
        if (file.Length <= 0 || file.Length > MaximumBytes) throw new InvalidDataException(L10n.T("Settings.FontFilesMustBeNonEmptyAndNoLarger"));
        using var input = file.OpenRead(); var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var relative = Path.Combine("Fonts", hash + extension); var target = Path.Combine(storage.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var created = !File.Exists(target);
        if (created) File.Copy(file.FullName, target);
        try { Register(target); return relative; }
        catch { if (created) File.Delete(target); throw; }
    }
    public static string FilePath(AppStorage storage, string relative)
    {
        var folder = Path.Combine(storage.Root, "Fonts"); var full = Path.GetFullPath(Path.Combine(storage.Root, relative));
        if (!DataDirectoryService.Contains(folder, full)) throw new InvalidDataException(L10n.T("Settings.CustomFontsMustBeStoredInTheDataDirectory"));
        return full;
    }
    public static FontFamily Resolve(AppStorage storage, AppSettings settings)
    {
        var family = settings.FontFilePath is { Length: > 0 } path ? Register(FilePath(storage, path)) : settings.FontFamily;
        return new FontFamily((string.IsNullOrEmpty(family) ? "" : family + ", ") + Fallback);
    }
    private static string Register(string path)
    {
        if (Registered.TryGetValue(path, out var existing)) return existing;
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumBytes) throw new InvalidDataException(L10n.T("Settings.FontFileIsMissingEmptyOrExceedsMB"));
        var key = new Uri("fonts:User" + MusicLibraryScanner.StableId(path));
        var collection = new EmbeddedFontCollection(key, new Uri(file.FullName));
        if (collection.Count == 0) throw new InvalidDataException(L10n.T("Settings.InvalidOrUnsupportedFontFile"));
        FontManager.Current.AddFontCollection(collection);
        var result = key.AbsoluteUri + "#" + collection[0].Name; Registered[path] = result; return result;
    }
}
