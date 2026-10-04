using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop;

public sealed partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private void BackgroundException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Application", "Unobserved background task exception", e.Exception);
        Dispatcher.UIThread.Post(() => _mainViewModel?.ReportError(L10n.T("Common.BackgroundTaskFailed"), e.Exception)); e.SetObserved();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppLog.DefaultRootResolver = AppStorage.ResolveRoot;
        // 仅调整字符串的宽度呈现方式，命令、无障碍及选择语义保持不变。
        DataTemplates.Add(new Avalonia.Controls.Templates.FuncDataTemplate<string>((text, _) =>
        {
            // 换行和省略由样式控制；提示完整换行，按钮文字沿用省略样式。
            return new Avalonia.Controls.TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        }));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            TaskScheduler.UnobservedTaskException += BackgroundException;
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                // 只恢复预期的用户操作错误，不掩盖程序缺陷或致命异常。
                if (e.Exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    _mainViewModel?.ReportError(L10n.T("Common.ActionFailed"), e.Exception); e.Handled = true;
                }
                else AppLog.Error("UI", "Unhandled dispatcher exception", e.Exception);
            };
            _mainViewModel = new MainViewModel(
                new MusicLibraryScanner(),
                new NativeAudioPlayer());

            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
            var initialized = false;
            desktop.MainWindow.Opened += async (_, _) =>
            {
                // 托盘恢复会再次触发 Opened；启动参数及启动警告只处理一次，避免重复播放文件。
                if (initialized) return; initialized = true;
                _mainViewModel.PublishStartupNotifications();
                if (_mainViewModel.Storage.RecoveryMessage is { } warning) _mainViewModel.ReportWarning(warning);
                Program.Instance?.Receive(files => Dispatcher.UIThread.Post(async () =>
                {
                    ((MainWindow)desktop.MainWindow).RestoreFromTray();
                    if (files.Length > 0) await _mainViewModel.PlayTemporaryFilesAsync(files);
                }));
                if (desktop.Args is { Length: > 0 } files) await _mainViewModel.PlayTemporaryFilesAsync(files);
            };
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += async (_, activation) =>
                {
                    if (activation is FileActivatedEventArgs fileActivation)
                    {
                        ((MainWindow)desktop.MainWindow).RestoreFromTray();
                        await _mainViewModel.PlayTemporaryFilesAsync(fileActivation.Files.Select(file => file.TryGetLocalPath()).OfType<string>());
                    }
                };

            desktop.Exit += (_, _) => { TaskScheduler.UnobservedTaskException -= BackgroundException; try { _mainViewModel.Dispose(); } finally { AppLog.Info("Application", "Normal shutdown"); AppLog.Flush(); AppLog.Shutdown(); } };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

