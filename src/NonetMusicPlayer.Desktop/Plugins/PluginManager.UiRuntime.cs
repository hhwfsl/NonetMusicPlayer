using Avalonia.Controls;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager
{
    private Window? _uiHost;
    private MainViewModel? _uiVm;
    private readonly Dictionary<string, PluginPetWindow> _pets = [];
    public IReadOnlyList<PluginPetWindow> ActivePetWindows => _pets.Values.Where(p => !p.IsDisposed).ToArray();
    public void AttachHost(Window host, MainViewModel vm)
    {
        if (_uiHost == host && _uiVm == vm) return;
        DisposeUiRuntime(); _uiHost = host; _uiVm = vm; AttachExtensionEvents(); host.Closed += HostClosed; host.Opened += HostOpened;
        if (!host.IsVisible) return;
        foreach (var plugin in Installed.Where(p => p.Enabled && p.Type == "extension" && p.Permissions.Contains("audio-processing"))) _ = StartAudioExtensionAsync(plugin);
        foreach (var plugin in Installed.Where(p => p.Enabled && p.Type == "ui")) RefreshUiRuntime(plugin);
    }
    public void DetachHost(Window host) { if (_uiHost == host) DisposeUiRuntime(); }
    private void HostClosed(object? sender, EventArgs e) => DisposeUiRuntime();
    private void HostOpened(object? sender, EventArgs e)
    {
        foreach (var plugin in Installed.Where(p => p.Enabled && p.Type == "extension" && p.Permissions.Contains("audio-processing"))) _ = StartAudioExtensionAsync(plugin);
        foreach (var plugin in Installed.Where(p => p.Enabled && p.Type == "ui")) RefreshUiRuntime(plugin);
    }
    private void RefreshUiRuntime(PluginManifest manifest)
    {
        if (!manifest.Enabled || manifest.Type != "ui")
        {
            if (_pets.Remove(manifest.Id, out var prior)) prior.Close();
            return;
        }
        if (_uiHost is null || _uiVm is null || !_uiHost.IsVisible || _pets.ContainsKey(manifest.Id)) return;
        PluginPetWindow? created = null;
        try
        {
            var page = ReadUiPage(manifest); var widget = page.Widgets.FirstOrDefault(w => w.Pet?.Floating == true);
            if (widget is null) return;
            if (_pets.Count >= 8) throw new InvalidOperationException(L10n.T("Common.AtMostDesktopPetsCanBeShownAtOnce"));
            created = new PluginPetWindow(this, manifest, _uiVm, widget, page.Flows ?? []);
            _pets.Add(manifest.Id, created); created.Closed += (_, _) => _pets.Remove(manifest.Id);
            created.Show(_uiHost);
        }
        catch (Exception error)
        {
            _pets.Remove(manifest.Id);
            if (created is not null)
            {
                created.Dispose();
                try { created.Close(); } catch (Exception closeError) { AppLog.Warning("PluginUI", "桌宠启动失败后的窗口关闭失败", closeError); }
            }
            _uiVm?.ReportError(L10n.T("Common.CouldNotStartDesktopPet"), error);
        }
    }
    private void DisposeUiRuntime()
    {
        DetachExtensionEvents();
        if (_uiHost is not null) { _uiHost.Closed -= HostClosed; _uiHost.Opened -= HostOpened; }
        _uiHost = null; _uiVm = null;
        foreach (var pet in _pets.Values.ToArray()) pet.Close(); _pets.Clear();
    }
}
