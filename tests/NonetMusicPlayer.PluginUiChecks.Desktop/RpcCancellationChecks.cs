using System.Runtime.InteropServices;
using NonetMusicPlayer.Core.Plugins;

/// <summary>使用真实旧进程协议验证取消后排空，避免旧回复被误当作下一次 RPC 的结果。</summary>
internal static class RpcCancellationChecks
{
    public static async Task Run(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../NonetMusicPlayer.ExtensionFixture/bin/Release/net10.0"));
        var executable = "NonetMusicPlayer.ExtensionFixture" + (OperatingSystem.IsWindows() ? ".exe" : "");
        var manifest = new PluginManifest { Id = "fixture.cancel", Name = "RPC fixture", Type = "provider", EntryPoints = new() { [RuntimeInformation.RuntimeIdentifier] = executable } };
        using (var client = new ProviderClient(fixture, manifest))
        {
            using var stop = new CancellationTokenSource(); var marker = Path.Combine(root, "drain-started");
            var old = client.CallAsync("fixture.delay", new() { ["started"] = marker, ["milliseconds"] = "500" }, stop.Token);
            await Until(() => File.Exists(marker)); stop.Cancel();
            try { await old.WaitAsync(TimeSpan.FromMilliseconds(200)); throw new InvalidDataException("Caller did not cancel immediately."); } catch (OperationCanceledException) { }
            var reply = await client.CallAsync("fixture.delay", new() { ["started"] = Path.Combine(root, "next-started"), ["milliseconds"] = "0" });
            Check(reply.GetProperty("reply").GetString() == "delayed" && !client.IsDisposed, "The old reply must be drained before a new RPC begins.");
        }
        var faults = 0;
        using (var client = new ProviderClient(fixture, manifest, _ => Interlocked.Increment(ref faults)))
        {
            using var stop = new CancellationTokenSource(); var marker = Path.Combine(root, "timeout-started");
            var old = client.CallAsync("fixture.delay", new() { ["started"] = marker, ["milliseconds"] = "60000" }, stop.Token, TimeSpan.FromSeconds(2));
            await Until(() => File.Exists(marker)); stop.Cancel();
            try { await old; throw new InvalidDataException("Caller did not cancel."); } catch (OperationCanceledException) { }
            await Until(() => client.IsDisposed);
            Check(faults == 1, "A timed-out drain must release the desynchronized process and report one fault.");
        }
        Console.WriteLine("PASS real process RPC: immediate caller cancellation, bounded reply draining, next reply integrity and drain-timeout disposal.");
    }
    private static async Task Until(Func<bool> condition)
    { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!condition()) await Task.Delay(10, deadline.Token); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
