using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Diagnostics;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

/// <summary>使用独立插件清单验证真实卡片排版，不加载用户插件或访问网络。</summary>
internal static class DesktopUxChecks
{
    public static void Run(string output, string? styleFile)
    {
        var storage = new AppStorage(Path.Combine(output, "data-" + Guid.NewGuid().ToString("N")));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new ApplicationIntegrationChecks.SilentAudioPlayer());
        vm.Settings.CloseToTray = vm.Settings.ConfirmClose = false;
        vm.Plugins.Installed.Add(new() { Id = "fixture.readable", Name = "歌词搜索与导入 · 一个较长的插件名称", Author = "Nonet fixture", Version = "1.2.3", Type = "lyrics", Description = "搜索歌曲歌词、导入时补全缺失内容，并提供下载和确认后的标签写入。", Permissions = ["network", "filesystem"] });
        vm.Plugins.Installed.Add(new() { Id = "fixture.long", Name = "LongPluginNameWithoutSpacesForWrapping", Author = "A longer author name", Version = "10.20.30", Type = "extension", Description = "A longer description should remain readable at the smallest window size and with large touch controls." });
        var window = new MainWindow { DataContext = vm, Width = 960, Height = 720, ShowActivated = false, ShowInTaskbar = false };
        window.PrepareInitialAppearance(); window.Show(); Pump(window);
        try
        {
            if (styleFile is not null) { using var input = File.OpenRead(styleFile); window.ApplyInterfaceLayers("ux-fixture", [StyleSheetContract.Read(input)]); }
            var version = window.FindControl<TextBlock>("ApplicationVersionLabel")!;
            Require(!version.IsVisible && version.Parent is not null, "Legacy version anchor remains available but hidden");
            var brand = window.FindControl<StackPanel>("TitleBrand")!;
            var name = window.FindControl<TextBlock>("ApplicationNameLabel")!;
            var badge = brand.Children.OfType<Button>().Single(b => b.Name == "NewVersionBadge");
            badge.IsVisible = true; Pump(window);
            var nameEnd = name.TranslatePoint(new(name.Bounds.Width, 0), brand)!.Value.X;
            Require(Math.Abs(badge.TranslatePoint(default, brand)!.Value.X - nameEnd - brand.Spacing) < 1, "New badge immediately follows application name");
            var layoutWarnings = 0;
            vm.UserNotification += (_, notice) => { if (notice.IsWarning && notice.Message.Contains("播放栏")) layoutWarnings++; };
            foreach (var width in new[] { 1360, 1000, 960, 800, 640 })
            foreach (var touch in new[] { false, true })
            {
                window.Width = width; window.Height = width == 640 ? 480 : 720;
                vm.Settings.TouchMode = touch; vm.Settings.FontSize = touch ? 22 : 14; vm.ApplySettings(); vm.Navigate("plugins"); Pump(window);
                var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PluginCard").ToArray();
                Require(cards.Length == 2, "Both fixture plugins are displayed");
                foreach (var card in cards)
                {
                    var info = card.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PluginInfo");
                    var actions = card.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.Name == "PluginActions");
                    Require(info.Bounds.Width >= Math.Min(240, card.Bounds.Width - 40) - 1, "Plugin information retains readable width");
                    Require(actions.Children.Count == 6, "All original plugin operations remain available");
                    var rectangles = actions.Children.Select(c => new Rect(c.TranslatePoint(default, actions)!.Value, c.Bounds.Size)).ToArray();
                    foreach (var rect in rectangles) Require(rect.X >= -1 && rect.Y >= -1 && rect.Right <= actions.Bounds.Width + 1 && rect.Bottom <= actions.Bounds.Height + 1, "Action stays within its multi-row container");
                    for (var i = 0; i < rectangles.Length; i++) for (var j = i + 1; j < rectangles.Length; j++) Require(!rectangles[i].Intersects(rectangles[j]), "Plugin operations do not overlap");
                    var rowCounts = rectangles.GroupBy(r => Math.Round(r.Y / 10)).Select(g => g.Count()).ToArray();
                    Require(rowCounts.Max() - rowCounts.Min() <= 1, "Action rows are balanced without an orphan button");
                    if (card.Bounds.Width - 40 < 560) Require(actions.TranslatePoint(default, info)!.Value.Y >= info.Bounds.Height, "Narrow card places actions below the full-width information");
                }
                if (!touch && width is 960 or 640) { using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(output, $"plugins-{width}.png"), PngBitmapEncoderOptions.Default); }
            }
            Require(layoutWarnings == 0, "Valid layouts down to the restored 640x480 minimum never trigger a fallback notice");
            if (styleFile is null)
            {
                // 入口允许位于滚动区域内，验证滚动后能完整到达，不能要求所有导航同时显示。
                var settings = window.GetVisualDescendants().OfType<Button>().Single(b => b.Tag as string == "settings");
                settings.BringIntoView(); Pump(window);
                Control viewport = settings.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() ?? (Control)window.FindControl<Border>("SidebarPanel")!;
                var position = settings.TranslatePoint(default, viewport)!.Value;
                Require(settings.IsEffectivelyVisible && position.Y >= -1 && position.Y + settings.Bounds.Height <= viewport.Bounds.Height + 1, "Settings remains reachable at the restored minimum size");
            }
            vm.Navigate("terminal"); Pump(window);
            window.Terminal.Configure(new(1000, 14, TerminalLogLevel.Error));
            foreach (var input in new[] { "nonsense", "nonet nonsense", "nonet player nonexistent", "other thing", "--oops" })
            {
                Wait(window, window.Terminal.ClearAsync()); AppLog.Flush();
                window.Terminal.SetDraft(input); Wait(window, window.Terminal.SubmitAsync()); AppLog.Flush(); Pump(window);
                var transcript = window.Terminal.Snapshot.Transcript;
                Require(transcript.Contains("\n未知命令" + Environment.NewLine) && !transcript.Contains("Exception") && !transcript.Contains("Player command failed"), "Unknown input produces plain output even with Error-only log filtering");
                Require(window.Commands.Journal.Last() is { Success: false, Operation: "command.unknown" }, "Unknown command remains an unsuccessful structured result");
            }
            var invalid = Wait(window, window.Commands.ExecuteAsync("player volume bananas"));
            Require(!invalid.Success && invalid.Operation != "command.unknown", "Known command validation still reports a real error");
            Require(Wait(window, window.Commands.ExecuteAsync("player status")).Success, "A valid command works after unknown input");
            Console.WriteLine("PASS hidden title version, adjacent New badge, readable multi-row plugin cards, plain unknown commands and retained validation" + (styleFile is null ? "" : ", external style"));
        }
        finally { window.ExitApplication(); }
    }
    private static void Pump(Window window) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); } }
    private static void Wait(Window window, Task task) { while (!task.IsCompleted) { Pump(window); Thread.Sleep(1); } task.GetAwaiter().GetResult(); }
    private static T Wait<T>(Window window, Task<T> task) { Wait(window, (Task)task); return task.GetAwaiter().GetResult(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
