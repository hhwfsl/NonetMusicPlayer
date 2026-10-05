using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>封闭原生页面规范的通用渲染器，不针对特定插件标识编写逻辑。</summary>
public sealed class PluginPageView : UserControl, IDisposable, IPluginKeyboardScope
{
    private readonly PluginManager _manager;
    private readonly string _pluginId;
    private readonly List<IDisposable> _resources = [];
    public bool IsDisposed { get; private set; }
    public PluginPageView(PluginManager manager, PluginManifest manifest, bool includeTitle = true)
    {
        var page = manager.LoadPage(manifest);
        _manager = manager; _pluginId = manifest.Id;
        var panel = Ui.Stack(); panel.Margin = new(0, 0, 18, 0);
        if (includeTitle) panel.Children.Add(Ui.RawText(page.Title, 26));
        if (page.Description.Length > 0) panel.Children.Add(Ui.RawText(page.Description, 13, true));
        foreach (var widget in page.Widgets)
        {
            if (widget.Type == "text") panel.Children.Add(Ui.Card(widget.Title, Ui.Text(widget.Text, 13, true)));
            else if (widget.Type == "lyrics-search")
            {
                var search = new LyricsSearchControl(manager, manifest); _resources.Add(search);
                panel.Children.Add(Ui.Card(widget.Title, search));
            }
            else if (widget.Type == "lyrics-timing")
            {
                var editor = new LyricsTimingControl(manifest.Id); _resources.Add(editor);
                panel.Children.Add(Ui.Card(widget.Title, Ui.Text(widget.Text, 13, true), editor));
            }
            else if (widget.Type == "snake" && widget.Snake is { } options)
            {
                var game = new SnakeGameControl(options); _resources.Add(game);
                panel.Children.Add(Ui.Card(widget.Title, Ui.Text(widget.Text, 13, true), game));
            }
            else if (widget.Type is "actions" or "pet")
            {
                var actions = new WrapPanel();
                foreach (var item in widget.Actions ?? [])
                {
                    var button = Ui.Button(item.Label, () =>
                    {
                        if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) throw new InvalidOperationException(L10n.T("Playback.ThePlayerIsNotConnected"));
                        PluginHostActions.Execute(manager, manifest, vm, item.Action);
                    });
                    button.Margin = new Avalonia.Thickness(0, 0, 8, 8); button.Tag = item.Action.Kind; actions.Children.Add(button);
                }
                if (widget.Pet is { } petOptions)
                {
                    var pet = new PluginPetControl(petOptions); _resources.Add(pet);
                    panel.Children.Add(Ui.Card(widget.Title, Ui.Text(widget.Text, 13, true), pet, actions));
                }
                else panel.Children.Add(Ui.Card(widget.Title, Ui.Text(widget.Text, 13, true), actions));
            }
            else if (widget.Type == "listening-summary")
            {
                var summary = Ui.Text("", 16); var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                void Refresh()
                {
                    if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
                    var statistics = vm.GetStatistics(); summary.Text = L10n.Format("Statistics.TodayMinTotalHoursPlays", (statistics.TodaySeconds / 60).ToString("0", L10n.Culture), (statistics.TotalSeconds / 3600).ToString("0.0", L10n.Culture), statistics.TotalPlays);
                }
                timer.Tick += (_, _) => Refresh(); AttachedToVisualTree += (_, _) => Refresh(); timer.Start();
                _resources.Add(new TimerResource(timer)); panel.Children.Add(Ui.Card(widget.Title, Ui.Text(widget.Text, 13, true), summary));
            }
        }
        Content = Ui.Scroll(panel);
        _manager.UiPluginUnavailable += PluginUnavailable;
    }
    private void PluginUnavailable(string id)
    {
        if (id != _pluginId) return;
        if (Dispatcher.UIThread.CheckAccess()) Dispose(); else Dispatcher.UIThread.Post(Dispose);
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { Dispose(); base.OnDetachedFromVisualTree(e); }
    public void Dispose()
    {
        if (IsDisposed) return; IsDisposed = true;
        _manager.UiPluginUnavailable -= PluginUnavailable;
        foreach (var resource in _resources) resource.Dispose(); _resources.Clear(); IsEnabled = false;
    }
    private sealed class TimerResource(DispatcherTimer timer) : IDisposable { public void Dispose() => timer.Stop(); }
}
