using Avalonia.Controls;
using Avalonia.Input;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

public sealed class ShortcutCaptureButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    private readonly Action<string> _changed;
    private string _gesture;
    private Key? _capturedKey;
    public bool IsCapturing { get; private set; }
    public ShortcutCaptureButton(string gesture, Action<string> changed)
    {
        _gesture = gesture; _changed = changed; UpdateLabel();
        Click += (_, _) => { IsCapturing = !IsCapturing; UpdateLabel(); Focus(); };
        LostFocus += (_, _) => { IsCapturing = false; UpdateLabel(); };
    }
    public void SetGesture(string gesture) { _gesture = gesture; IsCapturing = false; UpdateLabel(); }
    private void UpdateLabel() => Content = IsCapturing ? L10n.T("Settings.PressShortcutEscCancels") : string.IsNullOrEmpty(_gesture) ? L10n.T("Common.NotSetClickToRecord") : _gesture;
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsCapturing) { base.OnKeyDown(e); return; }
        if (e.Key == Key.Tab) { IsCapturing = false; UpdateLabel(); base.OnKeyDown(e); return; }
        e.Handled = true;
        _capturedKey = e.Key;
        if (e.Key == Key.Escape) { IsCapturing = false; UpdateLabel(); return; }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None) return;
        _gesture = ShortcutService.Canonical(e.Key, e.KeyModifiers); IsCapturing = false; UpdateLabel(); _changed(_gesture);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_capturedKey == e.Key) { _capturedKey = null; e.Handled = true; return; }
        base.OnKeyUp(e);
    }
}
