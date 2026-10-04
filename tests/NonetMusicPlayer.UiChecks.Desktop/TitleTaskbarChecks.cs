using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class TitleTaskbarChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static void Run(MainWindow window, MainViewModel vm)
    {
        var originalLeft = vm.Settings.TitleButtonsOnLeft;
        try
        {
            vm.Settings.TitleButtonsOnLeft = false; vm.ApplySettings(); Pump(window);
            var lights = window.FindControl<StackPanel>("TitleButtons")!;
            Require(lights.Children.Select(c => c.Name).SequenceEqual(["MinimizeWindowButton", "MaximizeWindowButton", "CloseWindowButton"]), "Right title controls use Windows action ordering");
            if (!OperatingSystem.IsWindows()) return;
            vm.Settings.TitleButtonsOnLeft = true; vm.ApplySettings(); Pump(window);
            Require(lights.Children.Select(c => c.Name).SequenceEqual(["CloseWindowButton", "MinimizeWindowButton", "MaximizeWindowButton"]), "Left title controls use macOS action ordering");
            var type = typeof(MainWindow); var edges = ((Grid)window.Content!).Children.OfType<Border>().Where(border => border.Tag is string).ToArray();
            type.GetField("_awaitingMovePointer", PrivateInstance)!.SetValue(window, true);
            foreach (var edge in edges) edge.IsHitTestVisible = false;
            object?[] arguments = [nint.Zero, 0x0020u, nint.Zero, nint.Zero, false];
            var cursorResult = (nint)type.GetMethod("TitleWindowMessage", PrivateInstance)!.Invoke(window, arguments)!;
            Require((bool)arguments[4]! && cursorResult == 1 && edges.All(edge => !edge.IsHitTestVisible), "Native stale WM_SETCURSOR is consumed while resize remains suppressed");
            window.MouseMove(new Point(60, 80)); Pump(window);
            Require(edges.All(edge => edge.IsHitTestVisible), "Actual interior movement restores all resize hit tests");

            using var taskbar = WindowsTaskbarService.Attach(window, vm) ?? throw new InvalidOperationException("Windows thumbnail service unavailable");
            var getIcon = typeof(WindowsTaskbarService).GetMethod("GetIcon", PrivateInstance)!;
            var glyph = typeof(WindowsTaskbarService).GetNestedType("Glyph", BindingFlags.NonPublic)!;
            var handles = Enum.GetValues(glyph).Cast<object>().Select(value => (nint)getIcon.Invoke(taskbar, [value])!).ToArray();
            Require(handles.All(handle => handle != 0) && handles.Distinct().Count() == 4, "Four native 32-bit thumbnail icon resources created");
            var repeated = (nint)getIcon.Invoke(taskbar, [Enum.GetValues(glyph).GetValue(0)])!;
            Require(repeated == handles[0], "Thumbnail icons reuse cached native handles");
            var before = vm.IsPlaying;
            arguments = [nint.Zero, 0x0111u, (nint)((0x1800 << 16) | 0x4C02), nint.Zero, false];
            typeof(WindowsTaskbarService).GetMethod("WindowMessage", PrivateInstance)!.Invoke(taskbar, arguments); Pump(window);
            Require((bool)arguments[4]! && vm.IsPlaying != before, "Thumbnail play/pause click invokes the same music command");
            taskbar.Dispose();
            Require(((System.Collections.IDictionary)typeof(WindowsTaskbarService).GetField("_icons", PrivateInstance)!.GetValue(taskbar)!).Count == 0, "Thumbnail icon ownership releases on disposal");
            Console.WriteLine("PASS TITLE/TASKBAR: platform ordering, native cursor suppression/restoration, HICON create/cache/dispose and thumbnail command routing");
        }
        finally { vm.Settings.TitleButtonsOnLeft = originalLeft; vm.ApplySettings(); Pump(window); }
    }
    private static void Pump(MainWindow window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
