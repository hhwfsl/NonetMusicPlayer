using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>实际精简发行包中验证背景循环、最小化恢复与缓冲边界；不读取使用数据。</summary>
    public static int RunBackground(string input, string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        MainWindow? window = null;
        try
        {
            var storage = new AppStorage(Path.Combine(root, "data")); Environment.SetEnvironmentVariable("NONET_DATA_DIR", storage.Root);
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            using var audio = new ConfigurationAudio(); using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
            vm.Settings.CloseToTray = false; vm.Settings.ConfirmClose = false;
            window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 }; window.Show(); Pump();
            var imported = AppBackgroundService.ImportDynamicAsync(storage, Path.GetFullPath(input)); Until(() => imported.IsCompleted); vm.Settings.BackgroundImagePath = imported.GetAwaiter().GetResult(); vm.ApplySettings();
            var surface = window.GetVisualDescendants().OfType<BackgroundMediaView>().Single(); Exception? error = null; surface.PlaybackFailed += value => error = value;
            Until(() => surface.FramesPresented > 28 || error is not null); if (error is not null) throw error;
            if (surface.PixelBytes is <= 0 or > 1280L * 720 * 4) throw new InvalidDataException("Background buffer exceeds bounds.");
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1000, 700))) { bitmap.Render(window); bitmap.Save(Path.Combine(root, "background.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            window.WindowState = WindowState.Minimized; Pump(); if (surface.PixelBytes != 0) throw new InvalidDataException("Minimized background retains pixels.");
            var count = surface.FramesPresented; window.WindowState = WindowState.Normal; Until(() => surface.FramesPresented > count + 2);
            vm.Settings.BackgroundImagePath = null; vm.ApplySettings(); window.Close(); window = null;
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nActualPublishedHost=True\nActualPackagedDecoder=True\nLoop=True\nBoundedPixels=True\nMinimizeResume=True\nAudioOutput=False\n"); return 0;
        }
        catch (Exception error) { AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + AppLog.Redact(error.ToString())); return 1; }
        finally { window?.Close(); AppLog.Flush(); AppLog.Shutdown(); }
        void Pump() { Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); }
        void Until(Func<bool> condition) { var until = DateTime.UtcNow.AddSeconds(20); while (!condition() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); } if (!condition()) throw new TimeoutException("Published background timeout."); Pump(); }
    }
}
