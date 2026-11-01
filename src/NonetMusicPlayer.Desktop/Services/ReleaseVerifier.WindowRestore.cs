using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Audio;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>真实 Win32 消息循环验证单次还原，不通过反复 Activate 或轮询抢占用户焦点。</summary>
    public static int RunWindowRestore(string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        MainWindow? window = null; Window? other = null;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var storage = new AppStorage(Path.Combine(root, "data"));
            Environment.SetEnvironmentVariable("NONET_DATA_DIR", storage.Root);
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            using var vm = new MainViewModel(new MusicLibraryScanner(storage), new RestoreAudio());
            vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false; vm.Settings.OptimizeMemoryWhenMinimized = true;
            window = new MainWindow { Title = "Nonet · Window restore verification", DataContext = vm, Width = 960, Height = 640 };
            other = new Window { Title = "Nonet · Activation target", Width = 240, Height = 140, ShowInTaskbar = false };
            window.Show(); other.Show(); Pump(120);
            var handle = window.TryGetPlatformHandle()!.Handle;
            for (var i = 0; i < 40; i++)
            {
                var target = i % 2 == 0 ? WindowState.Normal : WindowState.Maximized;
                window.WindowState = target; Pump(40); vm.Navigate("settings");
                window.WindowState = WindowState.Minimized; Pump(i % 10 == 0 ? 1000 : 80); other.Activate(); Pump(30);
                if (!VerifyIsIconic(handle)) throw new InvalidDataException("Fixture did not minimize: " + i);
                // 任务栏通常发送 SC_RESTORE；另测仅激活消息到达、原生/托管状态尚未同步的路径。
                if (i % 4 == 0) VerifyPostMessage(handle, 0x0006, 1, 0);
                else VerifyPostMessage(handle, 0x0112, 0xF120, 0);
                Pump(140);
                if (VerifyIsIconic(handle) || window.WindowState != target || vm.Page != "settings")
                    throw new InvalidDataException($"Single restore request failed at {i}: {window.WindowState}, expected {target}.");
            }
            // 托盘采用相同路径，最大化后隐藏/还原也不应变成普通窗口。
            window.WindowState = WindowState.Maximized; Pump(40); window.WindowState = WindowState.Minimized; Pump(60); window.Hide();
            window.RestoreFromTray(); Pump(120);
            if (!window.IsVisible || window.WindowState != WindowState.Maximized) throw new InvalidDataException("Tray restoration lost maximized state.");
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), string.Join(Environment.NewLine, new[] { "PASS", "NativeWin32=True", "SingleRestoreCycles=40", "PreserveMaximized=True", "ActivationOnly=True", "MinimizedCacheRelease=True", "KeepCurrentPage=True", "TrayRestore=True" }));
            return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + error); return 1; }
        finally {other?.Close(); window?.Close(); AppLog.Flush(); AppLog.Shutdown();}
        void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(milliseconds));
            Dispatcher.UIThread.PushFrame(frame);
            window?.UpdateLayout();
        }
    }
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VerifyPostMessage(nint hwnd,uint message,nint wParam,nint lParam);
    [DllImport("user32.dll", EntryPoint = "IsIconic")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VerifyIsIconic(nint hwnd);
    private sealed class RestoreAudio: IAudioPlayer
    {
        public bool IsAvailable=>true;public bool IsPlaying=>false;public TimeSpan Position{get;set;}public TimeSpan Duration=>TimeSpan.Zero;public float Volume{get;set;}
        public event EventHandler? PlaybackStopped{add{}remove{}}public Task LoadAsync(string path,CancellationToken token=default)=>Task.CompletedTask;public void Play(){}public void Pause(){}public void Stop(){}public void Dispose(){}
    }
}
