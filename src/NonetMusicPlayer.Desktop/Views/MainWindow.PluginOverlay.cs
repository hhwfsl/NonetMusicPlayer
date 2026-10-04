using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Plugins;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private Border? _pluginOverlay;
    private TaskCompletionSource<bool>? _pluginConfigCompletion;
    public bool HasPluginConfigurationOverlay => _pluginOverlay?.IsVisible == true;
    public Task<bool> OpenPluginConfigurationAsync(PluginManifest plugin)
    {
        if (_vm is null) return Task.FromResult(false);
        if (_pluginConfigCompletion is not null) return _pluginConfigCompletion.Task;
        var completion = _pluginConfigCompletion = new TaskCompletionSource<bool>();
        void Close(bool saved)
        {
            _pluginOverlay!.IsVisible = false; _pluginOverlay.Child = null; _pluginConfigCompletion = null; completion.TrySetResult(saved);
            if (saved) { RefreshPluginNavigation(); if (_vm.Page == "plugin:" + plugin.Id) ShowPage(); }
            FocusManager?.Focus(null);
        }
        try
        {
            if (_pluginOverlay is null)
            {
                _pluginOverlay = new Border { Name = "PluginConfigOverlay", ZIndex = 1000, Background = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)), Padding = new(24) };
                ((Grid)Content!).Children.Add(_pluginOverlay);
                Closed += (_, _) => _pluginConfigCompletion?.TrySetResult(false);
            }
            var form = new PluginConfigView(this, _vm, plugin, Close) { MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            _pluginOverlay.Child = form; _pluginOverlay.IsVisible = true; Dispatcher.UIThread.Post(() => form.Focus(), DispatcherPriority.Loaded);
        }
        catch { _pluginConfigCompletion = null; completion.TrySetResult(false); throw; }
        return completion.Task;
    }
}
