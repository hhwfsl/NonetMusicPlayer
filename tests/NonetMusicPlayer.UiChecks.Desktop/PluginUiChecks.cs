using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

internal static class PluginUiChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var run = Path.Combine(output, "plugin-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var storage = new AppStorage(Path.Combine(run, "data"));
        using var manager = new PluginManager(storage);
        var manifestText = """{"id":"author.another-game","name":"贪吃蛇小游戏测试插件","navigationLabel":"贪吃蛇小游戏测试插件","version":"1.0.0","contractVersion":1,"type":"ui","permissions":[],"pageEntry":"pages/game.json"}""";
        var pageText = """{"schemaVersion":1,"title":"声明式游戏页","description":"纯原生 JSON 组件","widgets":[{"type":"snake","title":"贪吃蛇","tickMilliseconds":600},{"type":"text","title":"帮助","text":"方向键 / WASD，空格，R"}]}""";
        var package = Package(run, "game", manifestText, pageText);
        var inspected = PluginManager.Inspect(package);
        Check(inspected.Type == "ui" && inspected.NavigationLabel == "贪吃蛇小游戏测试插件" && !inspected.Enabled, "Inspect native UI label and disabled default");
        var manifest = manager.Install(package);
        Reject(() => manager.LoadPage(manifest), "Disabled UI cannot be opened");
        manager.SetEnabled(manifest, true);
        var page = manager.LoadPage(manifest);
        Check(page.Widgets.Count == 2 && page.Widgets[0].Snake?.TickMilliseconds == 600, "Strict native page parses independently of plugin id");
        using (var reloaded = new PluginManager(storage))
            Check(reloaded.Installed.Single().PageEntry == "pages/game.json" && reloaded.Installed.Single().NavigationLabel == manifest.NavigationLabel, "Source generated index preserves new fields");
        var view = new PluginPageView(manager, manifest);
        var window = new Window { Width = 920, Height = 760, Content = view };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var game = view.GetVisualDescendants().OfType<SnakeGameControl>().Single();
        Check(game is IPluginKeyboardScope && view is IPluginKeyboardScope, "Plugin focus scope marker");
        game.KeyboardTarget.Focus(); PressKey(window, Key.Space, PhysicalKey.Space);
        Check(game.Game.Status == SnakeGameStatus.Running && game.IsTimerRunning, "Space starts game");
        PressKey(window, Key.W, PhysicalKey.W); var head = game.Game.Cells[0]; game.Game.Advance();
        Check(game.Game.Cells[0].Y == head.Y - 1, "W turns snake upward");
        PressKey(window, Key.Space, PhysicalKey.Space);
        Check(game.Game.Status == SnakeGameStatus.Paused && !game.IsTimerRunning, "Space pauses timer");
        var pausedHead = game.Game.Cells[0]; Pump(70); Check(game.Game.Cells[0] == pausedHead, "Paused board remains stable");
        PressKey(window, Key.R, PhysicalKey.R);
        Check(game.Game.Score == 0 && game.Game.Cells.Count == 3 && game.IsTimerRunning, "R restarts game");
        var runningHead = game.Game.Cells[0]; Pump(650); Check(game.Game.Cells[0] != runningHead, "Live dispatcher timer advances running game");
        using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No plugin page render"))
            frame.Save(Path.Combine(output, "snake-plugin-page.png"), PngBitmapEncoderOptions.Default);
        manager.SetEnabled(manifest, false);
        Check(view.IsDisposed && game.IsDisposed && !game.IsTimerRunning, "Disable disposes native page timer");
        var stoppedHead = game.Game.Cells[0]; Pump(70); Check(game.Game.Cells[0] == stoppedHead, "Disposed game no longer advances"); window.Close();
        manager.SetEnabled(manifest, true);
        var removedView = new PluginPageView(manager, manifest);
        var removedGame = removedView.GetLogicalDescendants().OfType<SnakeGameControl>().Single(); removedGame.Restart();
        manager.Uninstall(manifest);
        Check(removedView.IsDisposed && removedGame.IsDisposed && !removedGame.IsTimerRunning, "Uninstall disposes detached view and timer");
        Check(manager.Installed.Count == 0 && !Directory.Exists(Path.Combine(storage.PluginsFolder, manifest.Id)), "Uninstall removes only exact plugin");
        Check(storage.PluginsFolder.StartsWith(Path.Combine(run, "data") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Plugin installation stays inside isolated test data");
        Reject(() => PluginManager.Inspect(Package(run, "script", manifestText, pageText, ("evil.js", "alert(1)"))), "UI code files rejected");
        Reject(() => PluginManager.Inspect(Package(run, "path", manifestText.Replace("pages/game.json", "../game.json"), pageText)), "Traversal rejected");
        Reject(() => PluginManager.Inspect(Package(run, "permission", manifestText.Replace("\"permissions\":[]", "\"permissions\":[\"network\"]"), pageText)), "UI permissions rejected");
        Reject(() => PluginManager.Inspect(Package(run, "entry", manifestText.Replace("\"pageEntry\"", "\"entryPoints\":{\"win-x64\":\"pages/game.json\"},\"pageEntry\""), pageText)), "UI process entries rejected");
        Reject(() => PluginManager.Inspect(Package(run, "xaml", manifestText, pageText.Replace("\"snake\"", "\"xaml\""))), "Arbitrary UI type rejected");
        Reject(() => PluginManager.Inspect(Package(run, "unknown", manifestText, pageText.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"script\":\"evil()\""))), "Unknown script field rejected");
        Reject(() => PluginManager.Inspect(Package(run, "duplicate", manifestText, pageText.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"))), "Duplicate JSON key rejected");
        Reject(() => PluginManager.Inspect(Package(run, "speed", manifestText, pageText.Replace("600", "1"))), "Unbounded timer rejected");
        Reject(() => PluginManager.Inspect(Package(run, "large", manifestText, pageText + new string(' ', PluginPageContract.MaximumBytes))), "Oversized page rejected");
        Reject(() => PluginManager.Inspect(Package(run, "many-games", manifestText, pageText.Replace("{\"type\":\"text\",\"title\":\"帮助\",\"text\":\"方向键 / WASD，空格，R\"}", "{\"type\":\"snake\"}"))), "Multiple game timers rejected");
        Reject(() => PluginManager.Inspect(Package(run, "case-duplicate", manifestText, pageText, ("PAGES/GAME.JSON", pageText))), "Case-insensitive duplicate ZIP paths rejected");
        Reject(() => PluginManager.Inspect(Package(run, "missing-page", manifestText.Replace("pages/game.json", "absent.json"), pageText)), "Missing page entry rejected");
        Reject(() => PluginManager.Inspect(Package(run, "bad-color", manifestText, pageText.Replace("\"tickMilliseconds\":600", "\"tickMilliseconds\":600,\"food\":\"red\""))), "Invalid color rejected");
        Reject(() => PluginManager.Inspect(Package(run, "unused-directory", manifestText, pageText, ("unused/", ""))), "Unused package directory rejected");
        var symlink = Package(run, "symlink", manifestText, pageText);
        using (var zip = ZipFile.Open(symlink, ZipArchiveMode.Update)) zip.GetEntry("pages/game.json")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        Reject(() => PluginManager.Inspect(symlink), "ZIP symbolic link rejected");
        Rules();
        Console.WriteLine("PASS UI PLUGIN: package policy, native page/index, non-hardcoded id, keyboard, pause/restart, disable/uninstall disposal, strict schema and game rules");
    }
    private static void Rules()
    {
        var game = new SnakeGameEngine(new(12, 10, 150), seed: 5);
        Check(!game.Cells.Contains(game.Food), "Food never starts in snake");
        game.Start(); var initial = game.Cells[0]; game.Turn(-1, 0); game.Advance(); Check(game.Cells[0].X == initial.X + 1, "Cannot reverse into neck");
        for (var i = 0; i < 20; i++) game.Advance(); Check(game.Status == SnakeGameStatus.GameOver, "Wall collision ends game");
        game.Reset(); Check(game.Status == SnakeGameStatus.Ready && game.Score == 0 && game.Cells.Count == 3, "Reset restores bounded state");
        var wrap = new SnakeGameEngine(new(12, 10, 150, true), seed: 5); wrap.Start(); for (var i = 0; i < 13; i++) wrap.Advance();
        Check(wrap.Status == SnakeGameStatus.Running && wrap.Cells.All(p => p.X is >= 0 and < 12 && p.Y is >= 0 and < 10), "Optional wrapping stays in board");
        var hungry = new SnakeGameEngine(new(12, 10, 150), seed: 4); hungry.Start();
        for (var meal = 1; meal <= 4; meal++)
        {
            var start = hungry.Cells[0]; var target = hungry.Food; var blocked = hungry.Cells.Take(hungry.Cells.Count - 1).ToHashSet();
            var previous = new Dictionary<SnakeCell, SnakeCell>(); var queue = new Queue<SnakeCell>(); var seen = new HashSet<SnakeCell> { start }; queue.Enqueue(start);
            while (queue.TryDequeue(out var cell) && !seen.Contains(target))
                foreach (var step in new SnakeCell[] { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) })
                {
                    var next = new SnakeCell(cell.X + step.X, cell.Y + step.Y);
                    if (next.X is < 0 or >= 12 || next.Y is < 0 or >= 10 || blocked.Contains(next) || !seen.Add(next)) continue;
                    previous[next] = cell; queue.Enqueue(next);
                }
            Check(seen.Contains(target), "Fixture food reachable without self collision");
            var route = new List<SnakeCell>(); for (var cell = target; cell != start; cell = previous[cell]) route.Add(cell); route.Reverse();
            foreach (var cell in route) { var head = hungry.Cells[0]; hungry.Turn(cell.X - head.X, cell.Y - head.Y); hungry.Advance(); }
            Check(hungry.Status == SnakeGameStatus.Running && hungry.Score == meal * 10 && hungry.Cells.Count == 3 + meal && !hungry.Cells.Contains(hungry.Food), "Food scores, grows, and respawns on empty cell");
        }
        var current = hungry.Cells[0]; var neck = hungry.Cells[1]; var heading = new SnakeCell(current.X - neck.X, current.Y - neck.Y);
        var turn = new SnakeCell(-heading.Y, heading.X);
        var loop = new SnakeCell[] { turn, new(-heading.X, -heading.Y), new(-turn.X, -turn.Y), heading };
        var position = current;
        if (loop.Select(step => position = new(position.X + step.X, position.Y + step.Y)).Any(p => p.X is < 0 or >= 12 || p.Y is < 0 or >= 10))
            loop = [new(-turn.X, -turn.Y), new(-heading.X, -heading.Y), turn, heading];
        foreach (var step in loop) { hungry.Turn(step.X, step.Y); hungry.Advance(); }
        Check(hungry.Status == SnakeGameStatus.GameOver, "A grown snake collides with its own body");
    }
    private static void PressKey(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, Avalonia.Input.RawInputModifiers.None, physical, null); window.KeyRelease(key, Avalonia.Input.RawInputModifiers.None, physical, null); Dispatcher.UIThread.RunJobs();
    }
    private static void Pump(int milliseconds)
    {
        using var cancellation = new CancellationTokenSource(milliseconds);
        Dispatcher.UIThread.MainLoop(cancellation.Token); Dispatcher.UIThread.RunJobs();
    }
    private static string Package(string root, string name, string manifest, string page, params (string Path, string Content)[] extras)
    {
        var file = Path.Combine(root, name + ".impp"); using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        Write("manifest.json", manifest); Write("pages/game.json", page); foreach (var extra in extras) Write(extra.Path, extra.Content); return file;
        void Write(string path, string text) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text); }
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException) { return; }
        throw new InvalidOperationException(message + " was not rejected");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
