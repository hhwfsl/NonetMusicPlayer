using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;
public sealed class PluginsView : UserControl, IDisposable
{
    private readonly CancellationTokenSource _downloadCancellation = new();
    private bool _disposed;
    public PluginsView(MainWindow owner, MainViewModel vm)
    {
        async Task Install(string path)
        {
            if (_disposed) return;
            try
            {
                    var manifest = await Task.Run(() => PluginManager.Inspect(path));
                    var details = L10n.Format("Plugins.AuthorTypeVersionPermissionsInstalledPluginsAreDisabledBy", manifest.Author, TypeName(manifest.Type), manifest.Version, manifest.Permissions.Count == 0 ? L10n.T("Common.None") : string.Join(" / ", manifest.Permissions.Select(PermissionName)), manifest.Description);
                    if (_disposed || !await PlayerDialog.Confirm(owner, L10n.Format("Common.Install", manifest.Name), details, L10n.T("Common.TrustAndInstall"))) return;
                    if (vm.IsBusy) throw new InvalidOperationException(L10n.T("Plugins.WaitForTheCurrentOperationBeforeInstallingAPlugin"));
                    vm.IsBusy = true;
                    try { await Task.Run(() => vm.Plugins.Install(path)); }
                    finally { vm.IsBusy = false; }
                    vm.StatusText = L10n.T("Plugins.PluginInstalled"); owner.ShowPage();
            }
            catch (Exception e) { vm.ReportError(L10n.T("Plugins.PluginInstallationFailed"), e); }
        }
        var install = Ui.AsyncButton(L10n.T("Plugins.InstallPlugin"), async () => { var files = await owner.OpenFilesAsync(L10n.T("Plugins.ChoosePluginPackage"), ["*.impp"], false); if (files.Length > 0) await Install(files[0]); }, true);
        install.HorizontalAlignment = HorizontalAlignment.Left;
        var remote = Ui.AsyncButton(L10n.T("Plugins.ImportFromGitHubRelease"), async () =>
        {
            var link = await PlayerDialog.Prompt(owner, L10n.T("Plugins.ImportFromGitHubRelease"), L10n.T("Plugins.EnterTheHTTPSURLOfAPublicReleaseLatest"), acceptText: L10n.T("Common.Import")); if (string.IsNullOrWhiteSpace(link)) return;
            if (_disposed) return;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_downloadCancellation.Token);
            using var notification = owner.ShowPluginDownload(cancellation.Cancel);
            try
            {
                using var downloader = new PluginReleaseDownloader();
                var assets = await downloader.ResolveAsync(link, cancellation.Token); var selected = assets[0];
                if (assets.Count > 1)
                {
                    var name = await PlayerDialog.Choose(owner, L10n.T("Plugins.ChoosePluginAsset"), L10n.T("Plugins.ChooseAnImppPackageForThisOperatingSystem"), assets.Select(asset => asset.Name).ToArray());
                    if (name is null) return; selected = assets.First(asset => asset.Name == name);
                }
                cancellation.Token.ThrowIfCancellationRequested(); notification.Downloading(selected.Name);
                using var package = await downloader.DownloadAsync(selected, vm.Storage.PluginsFolder, notification, cancellation.Token);
                notification.Complete(selected.Name);
                await Install(package.Path);
            }
            catch (OperationCanceledException) { if (!_disposed) vm.ReportWarning(L10n.T("Plugins.PluginDownloadCanceledOrTimedOut")); }
            catch (Exception error) { vm.ReportError(L10n.T("Plugins.PluginDownloadFailed"), error); }
        });
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal }; install.Margin = new(0, 0, 10, 6); remote.Margin = new(0, 0, 0, 6); toolbar.Children.Add(install); toolbar.Children.Add(remote);
        var panel = Ui.Stack(toolbar);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { if (e.DataTransfer.TryGetFiles()?.Any(file => file.TryGetLocalPath() is { } path && Path.GetExtension(path).Equals(".impp", StringComparison.OrdinalIgnoreCase)) == true) { e.DragEffects = DragDropEffects.Copy; e.Handled = true; } });
        AddHandler(DragDrop.DropEvent, async (_, e) => { var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().Where(path => Path.GetExtension(path).Equals(".impp", StringComparison.OrdinalIgnoreCase)).ToArray() ?? []; if (paths.Length == 0) return; e.Handled = true; foreach (var path in paths) { if (_disposed) break; await Install(path); } });
        if (vm.Plugins.Installed.Count == 0) panel.Children.Add(Ui.Text(L10n.T("Plugins.NoPluginsInstalled"), 14, true));
        foreach (var plugin in vm.Plugins.Installed)
        {
            var enabled = Ui.Toggle(plugin.Enabled, value =>
            {
                if (value) { vm.Plugins.SetEnabled(plugin, true); vm.ApplyFilter(); vm.ApplySettings(); vm.StatusText = L10n.T("Plugins.PluginEnabled"); }
                else vm.DisablePlugin(plugin);
                owner.RefreshPluginNavigation();
            });
            enabled.Name = "PluginEnabled"; enabled.VerticalAlignment = VerticalAlignment.Center; enabled.VerticalContentAlignment = VerticalAlignment.Center; enabled.Margin = new(0); enabled.Padding = new(0);
            enabled.Classes.Add("plugin-enabled");
            Avalonia.Automation.AutomationProperties.SetName(enabled, plugin.Name + " · " + L10n.T("Common.On"));
            ToolTip.SetTip(enabled, L10n.T(plugin.Enabled ? L10n.T("Plugins.DisablePlugin") : L10n.T("Plugins.EnablePlugin")));
            var author = string.IsNullOrWhiteSpace(plugin.Author) ? L10n.T("Common.UnknownAuthor") : plugin.Author;
            var info = Ui.Stack(Ui.RawText(plugin.Name, 17), Ui.RawText($"{author} · {plugin.Version} · {TypeName(plugin.Type)}", 12, true));
            info.Spacing = 5;
            if (!string.IsNullOrWhiteSpace(plugin.Description)) info.Children.Add(Ui.RawText(plugin.Description, 12, true));
            if (plugin.Permissions.Count > 0) info.Children.Add(Ui.RawText(L10n.T("Plugins.Permissions") + ": " + string.Join(" / ", plugin.Permissions.Select(PermissionName)), 11, true));
            info.Margin = new Thickness(0, 0, 16, 0);
            var actions = new Grid { ColumnDefinitions = new("Auto,Auto,Auto,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center }; actions.Children.Add(enabled);
            var configureButton = Ui.AsyncButton("", async () => await owner.OpenPluginConfigurationAsync(plugin)); configureButton.Name = "PluginConfigure";
            configureButton.Content = Icon(IconKind.Settings); ToolTip.SetTip(configureButton, L10n.T("Plugins.PluginConfiguration")); Avalonia.Automation.AutomationProperties.SetName(configureButton, plugin.Name + " · " + L10n.T("Plugins.PluginConfiguration")); Grid.SetColumn(configureButton, 1); configureButton.VerticalAlignment = VerticalAlignment.Center; actions.Children.Add(configureButton);
            if (plugin.Type == "provider")
            {
                var more = Ui.Button("", () => { }); more.Name = "PluginMore";
                more.Content = Icon(IconKind.More); ToolTip.SetTip(more, L10n.T("Common.More")); Avalonia.Automation.AutomationProperties.SetName(more, L10n.T("Common.More"));
                var menu = new ContextMenu();
                var configure = new MenuItem { Header = L10n.T("Common.Configure"), Icon = Icon(IconKind.Settings) };
                configure.Click += async (_, _) =>
                {
                    await owner.OpenPluginConfigurationAsync(plugin);
                };
                var refresh = new MenuItem { Header = L10n.T("Common.LoadCatalog"), Icon = Icon(IconKind.Library) };
                refresh.Click += async (_, _) => await vm.RefreshProviderAsync(plugin);
                menu.ItemsSource = new[] { configure, refresh }; more.Click += (_, _) => owner.OpenMenu(more, menu); Grid.SetColumn(more, 2); more.VerticalAlignment = VerticalAlignment.Center; actions.Children.Add(more);
            }
            var uninstall = Ui.AsyncButton("", async () =>
            {
                var deleteFiles = await PlayerDialog.Uninstall(owner, L10n.Format("Common.Uninstall097DD9", plugin.Name)); if (deleteFiles is null) return;
                try { vm.DisablePlugin(plugin); vm.Plugins.Uninstall(plugin, deleteFiles.Value); vm.ApplySettings(); owner.ShowPage(); vm.StatusText = L10n.T("Plugins.PluginUninstalled"); }
                catch (Exception e) { vm.ReportError(L10n.T("Common.UninstallFailed"), e); }
            });
            uninstall.Name = "PluginUninstall"; uninstall.Content = Icon(IconKind.Trash);
            ToolTip.SetTip(uninstall, L10n.T("Common.Uninstall")); Avalonia.Automation.AutomationProperties.SetName(uninstall, plugin.Name + " · " + L10n.T("Common.Uninstall"));
            Grid.SetColumn(uninstall, 3); uninstall.VerticalAlignment = VerticalAlignment.Center; actions.Children.Add(uninstall);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; grid.Children.Add(info); Grid.SetColumn(actions, 1); grid.Children.Add(actions);
            panel.Children.Add(new Border { Child = grid, Padding = new Thickness(20), CornerRadius = new CornerRadius(12), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new Thickness(1), Background = Ui.Brush("SurfaceBrush") });
        }
        Content = Ui.Scroll(panel);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _downloadCancellation.Cancel(); _downloadCancellation.Dispose(); }
    private static VectorIcon Icon(IconKind kind) => new() { Kind = kind, Width = 18, Height = 18, Brush = Ui.Brush("TextPrimaryBrush") };
    private static string TypeName(string type) => L10n.T(type switch { "provider" => L10n.T("Common.Provider"), "theme" => L10n.T("Settings.Theme"), "widget" => L10n.T("Common.Cards"), _ => L10n.T("Common.Page") });
    private static string PermissionName(string name) => L10n.T(name switch { "player-control" => L10n.T("Playback.PlaybackControl"), "navigation" => L10n.T("Common.NavigationAndSearch"), "statistics" => L10n.T("Statistics.ListeningStatistics"), "desktop-widget" => L10n.T("Common.DesktopWidget"), "network" => L10n.T("Common.Network"), "process" => L10n.T("Common.NativeProcess"), "filesystem" => L10n.T("Common.FileSystem"), _ => name });
}
