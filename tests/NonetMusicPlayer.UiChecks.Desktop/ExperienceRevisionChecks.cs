using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;

/// <summary>逐字空间填色、翻译同步、拖动预览和独立基础工程的桌面导入集成检查。</summary>
internal static class ExperienceRevisionChecks
{
    public static void Run(string output)
    {
        var line = new LyricLine(0, "光の道", "中文翻译") { Words = [new(0, "光", 1), new(1, "の", 2), new(2, "道", 3)] };
        Require(Math.Abs(LyricsService.LineCharacterProgress(line, 1.5, true) - 2) < .001, "Ordinary translation follows original word timing proportionally");
        Require(LyricsService.LineCharacterProgress(line, -1, true) == 0 && LyricsService.LineCharacterProgress(line, 4, true) == 4, "Translation clamps before and after line");
        Require(LyricsService.LineCharacterProgress(line with { TranslationWords = [new(0, "中文翻译", 4)] }, 1, true) == 1, "Translation with its own timing uses independent word boundaries");
        Require(LyricsService.LineCharacterProgress(new(0, "普通歌词", "逐行翻译"), 0, true) == 4, "Ordinary LRC retains whole-line highlight");
        var first = new KaraokeLine { Wrap = false, Width = 200, Height = 110, TextSize = 80, TextColor = Colors.Coral, PendingColor = Colors.White };
        var second = new KaraokeLine { Wrap = false, Width = 200, Height = 110, TextSize = 80, TextColor = Colors.Coral, PendingColor = Colors.White };
        var third = new KaraokeLine { Width = 250, Height = 170, TextSize = 28, TextColor = Colors.Coral, PendingColor = Colors.White };
        first.UpdateTimed("光", [new(0, "光", 1)], .25); second.UpdateTimed("光", [new(0, "光", 1)], .75);
        third.UpdateTimed("换行歌词保持一句歌词从左向右填色，不拆开组合文字", [new(0, "换行歌词保持一句歌词从左向右填色，不拆开组合文字", 5)], 2);
        var slider = new PlayerProgressSlider { Width = 690, Height = 18, Maximum = 240, Value = 0 };
        var content = new StackPanel { Children = { new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { first, second, new VectorIcon { Kind = IconKind.GitHub, Width = 48, Height = 48, Brush = Brushes.White } } }, third, slider } };
        var window = new Window { Width = 720, Height = 440, Background = new SolidColorBrush(Color.Parse("#283142")), Content = content }; window.Show(); Pump(window);
        var quarter = first.ActiveClip.Bounds; var threeQuarters = second.ActiveClip.Bounds;
        Require(quarter.Width > 0 && Math.Abs(threeQuarters.Width / quarter.Width - 3) < .02 && Math.Abs(threeQuarters.X - quarter.X) < .01, "Within a glyph the fill boundary moves left to right, not global opacity");
        first.UpdateTimed("光", [new(0, "光", 1)], .75, false); Require(first.ActiveClip.Bounds.Width == 0, "Inactive lyric cannot highlight from supplied timing");
        first.UpdateTimed("光", [new(0, "光", 1)], .25); Pump(window);
        using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "karaoke-spatial-fill.png"), PngBitmapEncoderOptions.Default);
        var point = slider.TranslatePoint(new Point(120, 9), window)!.Value;
        window.MouseMove(point); Pump(window);
        Require(slider.Value == 0 && slider.IsPreviewVisible && slider.PreviewTime == "0:40", "Hover previews pointer target without seeking");
        slider.Value = 180; Pump(window);
        Require(slider.PreviewTime == "0:40", "Playback updates do not replace pointer time");
        window.MouseDown(point, MouseButton.Left); Pump(window);
        Require(slider.IsPreviewVisible && slider.PreviewTime == PlayerProgressSlider.FormatTime(slider.Value), "Seeking shows exact preview immediately");
        var nextPoint = slider.TranslatePoint(new Point(350, 9), window)!.Value;
        window.MouseMove(nextPoint, RawInputModifiers.LeftMouseButton); Pump(window);
        Require(slider.Value > 100 && slider.IsPreviewVisible, "Preview tracks pointer movement");
        var previewText = (TextBlock)typeof(PlayerProgressSlider).GetField("_previewText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(slider)!;
        var popup = TopLevel.GetTopLevel(previewText)!;
        PumpPopup(popup); Require(popup.Bounds.Width > 20 && previewText.Text == slider.PreviewTime, "Preview floating host is actually rendered with the dragged time");
        using (var frame = popup.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "seek-preview-popup.png"), PngBitmapEncoderOptions.Default);
        using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(output, "seek-preview.png"), PngBitmapEncoderOptions.Default);
        window.MouseUp(nextPoint, MouseButton.Left); Pump(window);
        window.MouseMove(new Point(5, 5)); Pump(window); Require(!slider.IsPreviewVisible, "Leaving the rail removes preview");
        Require(PlayerProgressSlider.FormatTime(3661.8) == "61:01" && PlayerProgressSlider.FormatTime(-1) == "0:00", "Seek previews consistently use minutes and seconds");
        window.Close();
        // 基础配置回归使用独立生成的 v1 夹具，不读取相邻模板或任何私人插件。
        var root = Path.Combine(output, "foundation-import-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"manifest.json"), """{"id":"fixture.foundation","name":"Foundation fixture","type":"ui","navigationLabel":"Foundation","pageEntry":"page.json","permissions":["navigation"]}""");
        File.WriteAllText(Path.Combine(root,"page.json"), """{"schemaVersion":1,"title":"Hello","description":"","widgets":[{"type":"text","title":"Greeting","text":"${config.greeting}"}]}""");
        File.WriteAllText(Path.Combine(root,"plugin_config_schema.json"), """{"greeting":{"type":"string","default":"Hello, NonetMusicPlayer!"}}""");
        var package = PluginPackageBuilder.Pack(root,Path.Combine(root,"hello.impp"));
        Require(typeof(PluginPackageInspector).Assembly.GetName().Name=="NonetMusicPlayer.PluginSdk","Desktop uses shared SDK.");
        using var manager=new PluginManager(new AppStorage(Path.Combine(root,"Data")));
        var plugin=manager.Install(package); manager.SetEnabled(plugin,true);
        Require(manager.LoadPage(plugin).Widgets.Single().Text=="Hello, NonetMusicPlayer!","Baseline UI configuration loads.");
        manager.Configure(plugin,"{\"greeting\":\"你好，插件！\"}");
        Require(manager.LoadPage(plugin).Widgets.Single().Text=="你好，插件！","Developer schema creates effective configuration.");
        manager.SetEnabled(plugin,false); manager.Uninstall(plugin,false); Require(manager.Installed.Count==0,"Fixture disconnects without deleting source package.");
        Console.WriteLine("PASS experience: spatial karaoke, synchronized translation, official SVG, seek preview and foundation integration");
    }
    private static void Pump(Window window) { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); window.CaptureRenderedFrame()?.Dispose(); } }
    private static void PumpPopup(TopLevel popup) { Dispatcher.UIThread.RunJobs(); popup.UpdateLayout(); popup.CaptureRenderedFrame()?.Dispose(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
