using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;
using Pointer = Avalonia.Input.Pointer;

/// <summary>在手指/鼠标尚未松开时检查真实容器让位，并经原生投放事件验证提交与取消。</summary>
internal static class TrackReorderChecks
{
    public static void Run(string output, string? style)
    {
        var storage = new AppStorage(Path.Combine(output, "reorder-data-" + Guid.NewGuid().ToString("N")));
        using var audio = new ApplicationIntegrationChecks.SilentAudioPlayer();
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), audio);
        vm.Settings.CloseToTray = vm.Settings.ConfirmClose = false;
        var artwork = Path.Combine(output, "queue-cover.png");
        using (var bitmap = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96)))
        {
            using (var pixels = bitmap.Lock()) for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                System.Runtime.InteropServices.Marshal.WriteInt32(pixels.Address + y * pixels.RowBytes + x * 4, unchecked((int)0xFFF0A0C0));
            bitmap.Save(artwork, PngBitmapEncoderOptions.Default);
        }
        var tracks = Enumerable.Range(0, 50).Select(i => new TrackItem("reorder-" + i, $"拖动预览 · {i + 1}", "Nonet 艺术家", "专辑", "fixture-" + i, ".wav", 100)
            { DurationSeconds = 263, CoverPath = i % 2 == 0 ? artwork : null }).ToArray();
        vm.State.Tracks.AddRange(tracks); var playlist = vm.CreatePlaylist("排序预览"); vm.AddToPlaylist(playlist, tracks);
        vm.Navigate("playlist:" + playlist.Id); vm.SetPlayMode(PlayMode.RepeatAll); Wait(vm.PlayTrackAsync(tracks[2])); vm.Seek(42);
        var original = vm.PlaybackQueue.Select(t => t.Id).ToArray(); var source = playlist.TrackIds.ToArray();
        var window = new MainWindow { DataContext = vm };
        if (style is not null) { using var input = File.OpenRead(style); window.ApplyInterfaceLayers("reorder-fixture", [StyleSheetContract.Read(input)]); }
        window.Show(); Pump(window, 200);
        window.SetPlaybackQueueOpen(true); Pump(window, 400);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "PlaybackQueueList");
        var surface = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "PlaybackQueuePopover");
        Require(!surface.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == L10n.T("Playback.QueueHint")), "Queue footer hint removed");
        Require(!surface.GetVisualDescendants().OfType<Control>().Any(c => c.Name == "QueueDragHandle"), "Queue rows do not have drag indicator controls");
        var images = list.GetVisualDescendants().OfType<Image>().Where(i => i.Name == "QueueTrackArtwork").ToArray();
        Require(images.Any(i => i.Source is not null) && images.Any(i => i.Source is null), "Real covers and missing-cover fallback coexist");
        var start = Center(Row(list, tracks[0]), window); var destination = Row(list, tracks[2]).TranslatePoint(new Point(80, 6), window)!.Value;
        var secondTop = Row(list, tracks[1]).TranslatePoint(default, window)!.Value.Y;
        var firstTop = Row(list, tracks[0]).TranslatePoint(default, window)!.Value.Y;
        window.MouseDown(start, MouseButton.Left); window.MouseMove(destination);
        var queueFrames = ObservePlacement(window, list, tracks[1], "_queueReorder");
        var ghost = Ghost(window);
        Require(ghost.DataContext == tracks[0] && ghost.Opacity > .5 && ghost.Opacity < 1 && !ghost.IsHitTestVisible
            && ghost.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == tracks[0].Title)
            && ghost.GetVisualDescendants().OfType<Image>().Any(i => i.Source is not null), "Held queue drag has a noninteractive translucent song copy with real text and cover");
        Require(Row(list, tracks[0]).Opacity < .2, "Dragged queue row remains as a dim placeholder");
        var ghostTop = Canvas.GetTop(ghost); window.MouseMove(destination + new Vector(6, 3));
        Require(ReferenceEquals(ghost, Ghost(window)) && Math.Abs(Canvas.GetTop(ghost) - ghostTop - 3) < .5, "Same ghost follows pointer motion without jumping to reordered row");
        var reversingFrom = Row(list, tracks[1]).TranslatePoint(default, window)!.Value.Y;
        window.MouseMove(start);
        var reversingStart = Row(list, tracks[1]).TranslatePoint(default, window)!.Value.Y;
        Require(Math.Abs(reversingStart - reversingFrom) < 1.5, "Fast reverse retains current presentation instead of snapping");
        Capture(window, output, "queue-drag-frame-01");
        Pump(window, 40); window.MouseMove(destination); Capture(window, output, "queue-drag-frame-02"); Pump(window, 240);
        queueFrames.Stop();
        Require(queueFrames.Positions.Where(y => y > firstTop + 1 && y < secondTop - 1).Select(y => Math.Round(y, 1)).Distinct().Count() >= 3,
            "Queue neighbor must render several intermediate positions instead of directly swapping");
        var expected = original.ToList(); expected.RemoveAt(0); expected.Insert(1, original[0]);
        Require(Items(list).SequenceEqual(expected), "Held queue drag previews actual new row order");
        Require(Row(list, tracks[1]).TranslatePoint(default, window)!.Value.Y < secondTop - 10, "Adjacent queue row physically moves before release");
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(original) && playlist.TrackIds.SequenceEqual(source), "Preview does not mutate business queue or playlist");
        Capture(window, output, "queue-held-preview"); window.MouseUp(destination, MouseButton.Left); Pump(window, 100);
        Require(!HasGhost(window) && ghost.Child is null, "Queue drop removes ghost and releases copied row");
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(expected) && playlist.TrackIds.SequenceEqual(source), "Drop commits queue only");
        Require(vm.CurrentTrack == tracks[2] && Math.Abs(vm.PlaybackPosition - 42) < .01 && vm.IsPlaying, "Queue reorder preserves current track, position and playing state");
        tracks[0].IsFavorite = !tracks[0].IsFavorite;
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(expected), "Favorite edit retains manual queue order");
        Wait(vm.PlayRelativeAsync(-1)); Require(vm.CurrentTrack == tracks[0], "Previous follows reordered queue");
        Wait(vm.PlayRelativeAsync(1)); Require(vm.CurrentTrack == tracks[2], "Next follows reordered queue");
        start = Center(Row(list, tracks[1]), window); destination = Row(list, tracks[3]).TranslatePoint(new Point(80, 6), window)!.Value;
        window.MouseDown(start, MouseButton.Left); window.MouseMove(destination); Pump(window, 170);
        Escape(window); window.MouseUp(destination, MouseButton.Left); Pump(window, 30);
        Require(window.IsPlaybackQueueOpen && Items(list).SequenceEqual(expected) && vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(expected), "Escape cancels held drag without closing queue or playing target: open=" + window.IsPlaybackQueueOpen + "; visible=" + string.Join(',', Items(list).Take(6)) + "; queue=" + string.Join(',', vm.PlaybackQueue.Take(6).Select(t => t.Id)));
        Require(!HasGhost(window) && list.GetRealizedContainers().OfType<ListBoxItem>().All(r => r.Opacity > .9), "Escape clears ghost and all dim placeholders");
        start = Center(Row(list, tracks[1]), window); var edge = list.TranslatePoint(new Point(80, list.Bounds.Height - 3), window)!.Value;
        window.MouseDown(start, MouseButton.Left); window.MouseMove(edge); Pump(window, 650);
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First(); Require(scroll.Offset.Y > 10, "Held drag scrolls virtual queue at edge");
        window.SetPlaybackQueueOpen(false); window.MouseUp(edge, MouseButton.Left); Pump(window, 300);
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(expected), "Closing queue rolls back uncommitted edge drag");
        Require(!vm.ReorderPlaybackQueue(original, original.Reverse().ToArray()), "Stale queue commit rejected");
        Require(!vm.ReorderPlaybackQueue(expected, expected.Select(_ => expected[0]).ToArray()), "Duplicate queue members rejected");

        // 模拟系统拖放的目标事件，源侧只跳过阻塞的 OS 拖放循环，仍使用真实窗口路由。
        vm.Navigate("playlist:" + playlist.Id); Pump(window, 80);
        var songs = window.FindControl<TrackListBox>("TracksList")!; songs.ScrollIntoView(tracks[0]); Pump(window, 50);
        Begin(window, [tracks[0].Id]);
        var data = new DataTransfer(); data.Add(DataTransferItem.CreateText("nonet-tracks:" + tracks[0].Id));
        destination = Row(songs, tracks[2]).TranslatePoint(new Point(120, 6), window)!.Value;
        var songTop = Row(songs, tracks[1]).TranslatePoint(default, window)!.Value.Y;
        var songFirstTop = Row(songs, tracks[0]).TranslatePoint(default, window)!.Value.Y;
        var songFrames = ObservePlacement(window, songs, tracks[1], "_trackReorder");
        window.DragDrop(destination, RawDragEventType.DragEnter, data, DragDropEffects.Move);
        window.DragDrop(destination, RawDragEventType.DragOver, data, DragDropEffects.Move);
        Require(HasGhost(window) && Row(songs, tracks[0]).Opacity < .2, "Native song drag shows snapshot and keeps a placeholder");
        // 在真正开始的 Enter 前订阅，动画期间不做同步 PNG 编码，避免测试 I/O 吞掉中间帧。
        Pump(window, 230);
        songFrames.Stop();
        Require(songFrames.Positions.Where(y => y > songFirstTop + 1 && y < songTop - 1).Select(y => Math.Round(y, 1)).Distinct().Count() >= 3,
            "Playlist neighbor must render several intermediate positions instead of directly swapping");
        Require(vm.VisibleTracks[0] == tracks[1] && vm.VisibleTracks[1] == tracks[0] && playlist.TrackIds.SequenceEqual(source), "Native song drag previews before persisting playlist: visible=" + string.Join(',', vm.VisibleTracks.Take(5).Select(t => t.Id)));
        // 行在指针下方让位会切换系统拖放命中项；Leave/Enter 之间没有 DragOver 也不能撤销预览。
        var heldGhost = Ghost(window); var heldOrder = vm.VisibleTracks.Select(t => t.Id).ToArray();
        for (var handoff = 0; handoff < 12; handoff++)
        {
            window.DragDrop(destination, RawDragEventType.DragLeave, data, DragDropEffects.Move);
            window.DragDrop(destination, RawDragEventType.DragEnter, data, DragDropEffects.Move); Pump(window, 18);
            Require(HasGhost(window) && ReferenceEquals(heldGhost, Ghost(window)) && vm.VisibleTracks.Select(t => t.Id).SequenceEqual(heldOrder),
                "Retargeting a native drag between song rows must retain the same ghost and preview without an intervening DragOver");
        }
        var player = window.FindControl<Border>("PlayerSurface")!; var playerPoint = Center(player, window);
        window.DragDrop(destination, RawDragEventType.DragLeave, data, DragDropEffects.Move);
        window.DragDrop(playerPoint, RawDragEventType.DragEnter, data, DragDropEffects.Move); Pump(window, 20);
        Require(!HasGhost(window) && vm.VisibleTracks.Select(t => t.Id).SequenceEqual(source), "Entering the player restores the song preview immediately");
        var hint = window.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "DropPreviewSurface");
        Require(hint.IsVisible, "Entering the player still shows its original drop-to-play hint");
        window.DragDrop(playerPoint, RawDragEventType.DragLeave, data, DragDropEffects.Move);
        window.DragDrop(destination, RawDragEventType.DragEnter, data, DragDropEffects.Move); Pump(window, 20);
        Require(HasGhost(window) && ReferenceEquals(heldGhost, Ghost(window)) && !hint.IsVisible && vm.VisibleTracks.Select(t => t.Id).SequenceEqual(heldOrder), "Returning from player to songs resumes sorting and clears the unrelated hint on Enter alone");
        // 真正离开时仍恢复，重新进入仅收到 Enter 时也应立即恢复预览，无需等待下一次移动。
        window.DragDrop(destination, RawDragEventType.DragLeave, data, DragDropEffects.Move); Pump(window, 40);
        Require(!HasGhost(window) && vm.VisibleTracks.Select(t => t.Id).SequenceEqual(source), "A genuine native leave clears the ghost and restores the uncommitted playlist");
        window.DragDrop(destination, RawDragEventType.DragEnter, data, DragDropEffects.Move); Pump(window, 40);
        Require(HasGhost(window) && ReferenceEquals(heldGhost, Ghost(window)) && vm.VisibleTracks.Select(t => t.Id).SequenceEqual(heldOrder), "Reentry immediately resumes the same preview on DragEnter alone");
        Capture(window, output, "playlist-held-preview");
        window.DragDrop(destination, RawDragEventType.Drop, data, DragDropEffects.Move); Pump(window, 80);
        Require(!HasGhost(window), "Native song drop clears ghost overlay");
        Require(playlist.TrackIds[0] == tracks[1].Id && playlist.TrackIds[1] == tracks[0].Id, "Native drop persists previewed order");
        var committed = playlist.TrackIds.ToArray(); Begin(window, [tracks[1].Id, tracks[0].Id]);
        var multiple = new DataTransfer(); multiple.Add(DataTransferItem.CreateText("nonet-tracks:" + tracks[1].Id + "\n" + tracks[0].Id));
        destination = Row(songs, tracks[4]).TranslatePoint(new Point(120, 6), window)!.Value;
        window.DragDrop(destination, RawDragEventType.DragEnter, multiple, DragDropEffects.Move);
        window.DragDrop(destination, RawDragEventType.DragOver, multiple, DragDropEffects.Move); Pump(window, 170);
        Require(Ghost(window).GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "×2"), "Multiple song ghost displays grouped count");
        Require(vm.VisibleTracks[0] == tracks[2] && vm.VisibleTracks[2] == tracks[1] && vm.VisibleTracks[3] == tracks[0], "Multiple selected songs preview as ordered block");
        Escape(window); Pump(window, 30);
        Require(vm.VisibleTracks.Select(t => t.Id).SequenceEqual(committed) && playlist.TrackIds.SequenceEqual(committed), "Cancelled song drag restores original visible and saved order");
        Require(!HasGhost(window), "Cancelled song drag removes overlay");
        using (var scattered = new TrackReorderPreview(songs, vm.VisibleTracks, [tracks[1].Id, tracks[2].Id, tracks[3].Id]))
        {
            scattered.Preview(Row(songs, tracks[0]).TranslatePoint(new Point(120, 6), songs)!.Value); Pump(window, 170);
            Require(vm.VisibleTracks[1] == tracks[2] && vm.VisibleTracks[2] == tracks[3], "Noncontiguous selection previews as one ordered block");
        }
        Require(vm.VisibleTracks.Select(t => t.Id).SequenceEqual(committed), "Noncontiguous group cancellation restores gaps exactly");
        Begin(window, [tracks[1].Id]);
        window.DragDrop(destination, RawDragEventType.DragEnter, multiple, DragDropEffects.Move);
        // 编辑/换页后旧手势不能落入另一条传统排序分支，修改新的列表。
        vm.Navigate("settings"); vm.Navigate("playlist:" + playlist.Id); Pump(window, 40);
        var single = new DataTransfer(); single.Add(DataTransferItem.CreateText("nonet-tracks:" + tracks[1].Id));
        window.DragDrop(destination, RawDragEventType.DragOver, single, DragDropEffects.Move);
        Escape(window); window.DragDrop(destination, RawDragEventType.Drop, single, DragDropEffects.Move); Pump(window, 30);
        Require(playlist.TrackIds.SequenceEqual(committed), "Cancelled native gesture cannot fall through and reorder refreshed list");
        var persisted = storage.Load(); Require(persisted.Playlists.Single(p => p.Id == playlist.Id).TrackIds.SequenceEqual(committed), "Only released playlist order persisted to storage");
        window.Width = 640; window.Height = 480; window.SetPlaybackQueueOpen(true); Pump(window, 400);
        Require(surface.Bounds.Right <= window.Bounds.Width && surface.Bounds.Bottom <= window.Bounds.Height, "Queue fits minimum window");
        Capture(window, output, "queue-minimum");
        window.Width = 1100; window.Height = 760; Pump(window, 100);
        foreach (var type in new[] { PointerType.Touch, PointerType.Pen })
        {
            scroll.Offset = default; list.UpdateLayout();
            var moving = (TrackItem)list.Items[0]!; var target = (TrackItem)list.Items[2]!;
            var row = Row(list, moving);
            start = Center(row, window); destination = Row(list, target).TranslatePoint(new Point(80, 6), window)!.Value;
            using var pointer = new Pointer(Pointer.GetNextFreeId(), type, true);
            var pressed = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed);
            row.RaiseEvent(new PointerPressedEventArgs(row, pointer, window, start, 10, pressed, KeyModifiers.None));
            using (var stop = new CancellationTokenSource(500)) Dispatcher.UIThread.MainLoop(stop.Token);
            Pump(window, 20);
            Require(ReferenceEquals(pointer.Captured, list), type + " long press lifts whole queue row");
            list.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, list, pointer, window, destination, 20, pressed, KeyModifiers.None)); Pump(window, 170);
            Require(!Items(list).SequenceEqual(expected), type + " previews queue reorder");
            pointer.Capture(null); Pump(window, 30);
            Require(Items(list).SequenceEqual(expected) && vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(expected), type + " capture loss cancels without changing actual queue");
            Require(!HasGhost(window), type + " capture loss releases ghost");
        }
        var oldMotion = Environment.GetEnvironmentVariable("NONET_REDUCED_MOTION");
        try
        {
            Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", "1");
            start = Center(Row(list, (TrackItem)list.Items[0]!), window); destination = Row(list, (TrackItem)list.Items[2]!).TranslatePoint(new Point(80, 6), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(destination); Pump(window, 30);
            Require(HasGhost(window), "Reduced motion still shows essential drag feedback"); Escape(window); window.MouseUp(destination, MouseButton.Left);
            Require(!HasGhost(window), "Reduced motion cancel clears ghost");
        }
        finally { Environment.SetEnvironmentVariable("NONET_REDUCED_MOTION", oldMotion); }
        Wait(vm.PlayQueueTrackAsync(tracks[2])); tracks[0].IsFavorite = !tracks[0].IsFavorite;
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(expected), "Manual queue selection and favorite edits preserve reordered repeat-all queue");
        var timing = vm.Plugins.Install(PluginTestFixtures.Timing(output)); vm.Plugins.SetEnabled(timing, true);
        using (var lease = vm.ReserveLyricsPlayback(timing.Id, tracks[2])) Wait(lease.StartAsync());
        tracks[0].IsFavorite = !tracks[0].IsFavorite;
        Require(vm.PlaybackQueue.Select(t => t.Id).SequenceEqual(expected), "Lyrics timing lease restores manual queue order and its edit protection");
        vm.SetPlayMode(PlayMode.Shuffle); Wait(vm.PlayTrackAsync(tracks[2]));
        var shuffled = vm.PlaybackQueue.Select(t => t.Id).ToArray(); var reversed = shuffled.Reverse().ToArray();
        Require(vm.ReorderPlaybackQueue(shuffled, reversed) && vm.Settings.PlayMode == PlayMode.Shuffle, "Shuffle queue accepts manual reorder without changing mode");
        Wait(vm.PlayRelativeAsync(1)); Require(vm.CurrentTrack?.Id == reversed[0], "Shuffle next follows reordered queue and wraps without reshuffling");
        window.Close();
        Console.WriteLine("PASS queue covers/footer, held mouse preview, audio continuity, next/previous, cancel/close, edge scrolling, stale commits, native row Leave/Enter continuity, genuine leave/reentry, song drop/multi-drag/rollback and persisted order.");
    }
    private static ListBoxItem Row(ListBox list, TrackItem track) => (ListBoxItem)list.ContainerFromItem(track)!;
    private static Point Center(Control c, Window w) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
    private static IEnumerable<string> Items(ListBox list) => list.Items.Cast<TrackItem>().Select(t => t.Id);
    private static void Begin(MainWindow w, string[] ids)
    {
        var list = w.FindControl<TrackListBox>("TracksList")!; var row = list.GetRealizedContainers().OfType<ListBoxItem>().First(r => r.DataContext is TrackItem t && ids.Contains(t.Id));
        typeof(MainWindow).GetField("_dragStart", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(w, row.TranslatePoint(new Point(100, row.Bounds.Height / 2), list)!.Value);
        typeof(MainWindow).GetMethod("BeginTrackReorderPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, [ids]);
    }
    private static Border Ghost(Window w) => w.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "DraggedTrackGhost");
    private static bool HasGhost(Window w) => w.GetVisualDescendants().OfType<Canvas>().Any(c => c.Name == "TrackDragOverlay");
    private static (List<double> Positions, Action Stop) ObservePlacement(Window window, ListBox list, TrackItem track, string field)
    {
        var preview = typeof(MainWindow).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var visual = preview.GetType().GetField("_visual", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        var clock = (RetargetableMotion)visual.GetType().GetField("_placement", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(visual)!;
        var positions = new List<double> { Row(list, track).TranslatePoint(default, window)!.Value.Y };
        void Sample(double _) { if (list.ContainerFromItem(track) is ListBoxItem row) positions.Add(row.TranslatePoint(default, window)!.Value.Y); }
        clock.Changed += Sample;
        return (positions, () => clock.Changed -= Sample);
    }
    private static void Escape(Window w) { w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); w.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); }
    private static void Wait(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } task.GetAwaiter().GetResult(); }
    private static void Pump(Window w, int ms) { var until = Environment.TickCount64 + ms; do { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); w.CaptureRenderedFrame()?.Dispose(); Thread.Sleep(3); } while (Environment.TickCount64 < until); }
    private static void Capture(Window w, string output, string name) { using var frame = w.CaptureRenderedFrame(); frame?.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
