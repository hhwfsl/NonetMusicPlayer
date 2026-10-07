using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Plugins;

namespace NonetMusicPlayer.Desktop.Services;

internal static partial class ReleaseVerifier
{
    /// <summary>用实际发行程序与真实插件入口检验配置/RPC，所有数据和 HTTP 均为隔离夹具。</summary>
    public static int RunAgent(string package, string output)
    {
        var root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                for (var count = 0; count < 2; count++)
                {
                    using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
                    using var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, true);
                    var request = await reader.ReadLineAsync(deadline.Token) ?? "";
                    var length = 0; var authorized = false;
                    while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } header)
                    {
                        if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(header[15..].Trim());
                        if (header == "Authorization: Bearer TEST-ONLY-RELEASE-KEY") authorized = true;
                    }
                    if (!authorized) throw new InvalidDataException("Fixture authentication missing.");
                    if (length > 200_000) throw new InvalidDataException("Fixture request too large.");
                    if (length > 0) { var body = new char[length]; await reader.ReadBlockAsync(body.AsMemory(), deadline.Token); }
                    var payload = request.StartsWith("GET ", StringComparison.Ordinal)
                        ? """{"data":[{"id":"fixture-model"}]}"""
                        : """{"choices":[{"message":{"content":"发行包 Agent 验证通过。","reasoning_content":"模拟思考"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":5}}""";
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), deadline.Token);
                    await stream.WriteAsync(bytes, deadline.Token);
                }
            }, deadline.Token);
            var storage = new AppStorage(Path.Combine(root, "data")); AppLog.Initialize(storage.Root);
            string id;
            using (var manager = new PluginManager(storage))
            {
                var plugin = manager.Install(Path.GetFullPath(package)); id = plugin.Id; manager.SetEnabled(plugin, true);
                var config = manager.ConfigurationValues(plugin);
                config["baseUrl"] = $"http://127.0.0.1:{port}/v1"; config["model"] = "fixture-model";
                config["apiKey"] = "TEST-ONLY-RELEASE-KEY"; config["maxTokens"] = 0; config["timeoutSeconds"] = 0;
                manager.Configure(plugin, config.ToJsonString());
                var models = manager.AgentModelsAsync(plugin, config.ToJsonString(), deadline.Token).GetAwaiter().GetResult();
                if (models.Models is not ["fixture-model"]) throw new InvalidDataException("Trimmed model discovery failed.");
                var reply = manager.AgentStepAsync(plugin, new([new("user", "隔离测试")], AgentCommandPolicy.Catalog()), deadline.Token).GetAwaiter().GetResult();
                if (reply.Text != "发行包 Agent 验证通过。" || reply.Reasoning != "模拟思考" || reply.OutputTokens != 5)
                    throw new InvalidDataException("Trimmed agent optional DTO/RPC failed.");
                if (File.ReadAllText(PluginConfigurationStore.PathFor(storage.PluginsFolder, plugin)).Contains("TEST-ONLY-RELEASE-KEY")
                    || plugin.Configuration.Contains("TEST-ONLY-RELEASE-KEY")) throw new InvalidDataException("Plaintext credential persistence.");
            }
            using (var restarted = new PluginManager(storage))
            {
                var plugin = restarted.Installed.Single(p => p.Id == id);
                if (restarted.ConfigurationValues(plugin)["apiKey"]!.GetValue<string>() != "TEST-ONLY-RELEASE-KEY")
                    throw new InvalidDataException("Trimmed protected configuration restart failed.");
                restarted.SetEnabled(plugin, false);
            }
            server.GetAwaiter().GetResult();
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "PASS\nActualPublishedHost=True\nActualPluginProcess=True\nModelDiscovery=True\nSourceGeneratedOptionalReply=True\nProtectedConfigurationRestart=True\nNoPlaintextKey=True\n");
            return 0;
        }
        catch (Exception error)
        {
            AppStorage.AtomicWrite(Path.Combine(root, "result.txt"), "FAIL\n" + AppLog.Redact(error.GetType().Name + ": " + error.Message)); return 1;
        }
        finally { deadline.Cancel(); listener.Stop(); AppLog.Flush(); AppLog.Shutdown(); }
    }
}
