using Avalonia.Input;

namespace NonetMusicPlayer.Desktop.Services;

public sealed record ShortcutAction(string Id, string Name, string DefaultGesture);
public static class ShortcutService
{
    public static readonly ShortcutAction[] Actions =
    [
        new("playPause", "Playback.PlayPause", "Space"), new("previous", "Common.Previous", "Ctrl+Left"), new("next", "Common.Next", "Ctrl+Right"),
        new("seekBack", "Common.SeekBackwardSeconds", "Left"), new("seekForward", "Common.SeekForwardSeconds", "Right"),
        new("favorite", "Common.LikeUnlikeCurrentTrack", "Ctrl+D"), new("addMusic", "Library.AddMusic", "Ctrl+O"),
        new("search", "Library.SearchMusicLibrary", "Ctrl+L"), new("layout", "Settings.SettingsLayoutConfiguration", "Ctrl+E"),
        new("batch", "Playlists.EnterExitTrackSelection", "Ctrl+B"), new("selectAll", "Playlists.TrackSelectionSelectAll", "Ctrl+A"),
        new("clearSelection", "Playlists.TrackSelectionClearSelection", "Ctrl+Shift+A"), new("undo", "Common.UndoLastAction", "Ctrl+Z"),
        new("moveUp", "Playlists.TrackSelectionMoveUp", "Alt+Up"), new("moveDown", "Playlists.TrackSelectionMoveDown", "Alt+Down"),
        new("clearSearch", "Playlists.ClearSearchExitSelection", "Escape"), new("help", "Common.OpenUserManual", "F1")
    ];
    public static Dictionary<string, string> DefaultBindings => Actions.ToDictionary(a => a.Id, a => a.DefaultGesture);
    public static string Get(IReadOnlyDictionary<string, string> bindings, ShortcutAction action) => bindings.TryGetValue(action.Id, out var value) ? value : action.DefaultGesture;
    private static KeyModifiers Normalize(KeyModifiers modifiers) => OperatingSystem.IsMacOS() && modifiers.HasFlag(KeyModifiers.Meta)
        ? (modifiers & ~KeyModifiers.Meta) | KeyModifiers.Control : modifiers;
    public static string? Match(IReadOnlyDictionary<string, string> bindings, Key key, KeyModifiers modifiers)
    {
        foreach (var action in Actions)
        {
            var text = Get(bindings, action); if (string.IsNullOrWhiteSpace(text)) continue;
            try { var gesture = KeyGesture.Parse(text); if (gesture.Key == key && Normalize(gesture.KeyModifiers) == Normalize(modifiers)) return action.Id; }
            catch (FormatException) { /* Older/externally edited invalid bindings fall back through the settings editor. */ }
        }
        return null;
    }
    public static string Canonical(Key key, KeyModifiers modifiers) => new KeyGesture(key, modifiers).ToString();
    public static void Validate(IReadOnlyDictionary<string, string> bindings)
    {
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in Actions)
        {
            var value = Get(bindings, action); if (string.IsNullOrWhiteSpace(value)) continue;
            KeyGesture gesture;
            try { gesture = KeyGesture.Parse(value); }
            catch (Exception e) when (e is FormatException or ArgumentException) { throw new InvalidDataException(L10n.Format("Settings.TheShortcutForIsInvalid", L10n.T(action.Name)), e); }
            if (gesture.Key is Key.None or Key.Tab or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
                || gesture.Key == Key.Enter && gesture.KeyModifiers == KeyModifiers.None
                || gesture.Key == Key.F4 && gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) throw new InvalidDataException("Settings.ThisKeyIsReservedForSystemOrFocusOperations");
            var normalized = Canonical(gesture.Key, Normalize(gesture.KeyModifiers));
            if (used.TryGetValue(normalized, out var previous)) throw new InvalidDataException(L10n.Format("Settings.AndUseTheSameShortcut", L10n.T(action.Name), L10n.T(previous), value));
            used[normalized] = action.Name;
        }
    }
}
