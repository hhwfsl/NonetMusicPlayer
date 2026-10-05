using System.Net;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.Views;

/// <summary>用可取消的阻塞流验证真实点击流程，不连接网络、不退出真实播放器。</summary>
internal static class UpdateDialogChecks
{
    public static void Run(MainWindow owner, string dataRoot)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var http = new HttpClient(new DownloadHandler());
        typeof(MainWindow).GetField("_updates", flags)!.SetValue(owner, new ReleaseUpdateService(http));
        typeof(MainWindow).GetField("_availableUpdate", flags)!.SetValue(owner, new UpdateRelease("9.0.0", "Readonly changelog",
            new GitHubAsset { Name = "NonetMusicPlayer.Desktop-9.0.0-win-x64.zip", Size = 100_000, Digest = "sha256:" + new string('0', 64),
                BrowserDownloadUrl = ReleaseUpdateService.Repository + "/releases/download/v9.0.0/package.zip" }, ReleaseUpdateService.PlatformRid));
        var dialogTask = (Task)typeof(MainWindow).GetMethod("ShowUpdateAsync", flags)!.Invoke(owner, null)!;
        Pump(owner);
        var dialog = owner.OwnedWindows.Single();
        var notes = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "UpdateReleaseNotes");
        Require(notes.IsReadOnly && ToolTip.GetTip(notes) is null && !FullTextToolTips.GetEnabled(notes), "Software update notes are readonly and tooltip-free");
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DownloadUpdate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(owner);
        Require(dialogTask.IsCompleted && !owner.OwnedWindows.Any(), "Download closes update dialog immediately");
        var updates = Path.Combine(dataRoot, "Updates");
        Until(owner, () => Directory.Exists(updates) && Directory.EnumerateFiles(updates, "download.zip", SearchOption.AllDirectories).Any());
        var cancel = owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelDownload");
        Require(cancel.Parent is Grid && Grid.GetColumn(cancel) == 1, "Cancel is on the right of download progress");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Until(owner, () => !(bool)typeof(MainWindow).GetField("_downloadingUpdate", flags)!.GetValue(owner)!);
        Require(!Directory.EnumerateFileSystemEntries(updates).Any(), "Cancellation removes partial file and its dedicated stage");
        Console.WriteLine("PASS update dialog readonly/no tooltip, closes on download, right-hand cancel, partial-download cleanup");
    }
    private static void Until(Window owner, Func<bool> condition)
    {
        var until = Environment.TickCount64 + 10_000;
        while (!condition() && Environment.TickCount64 < until) { Pump(owner); Thread.Sleep(2); }
        Pump(owner); Require(condition(), "Asynchronous update UI settled");
    }
    private static void Pump(Window owner) { Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class DownloadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new WaitingContent() });
    }
    private sealed class WaitingContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 100_000; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => throw new NotSupportedException();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new WaitingStream());
    }
    private sealed class WaitingStream : Stream
    {
        private bool _read;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_read) { _read = true; buffer.Span[0] = 42; return 1; }
            await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => 100_000; public override long Position { get => _read ? 1 : 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
