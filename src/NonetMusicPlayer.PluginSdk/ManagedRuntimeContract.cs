using System.Text.Json;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>可选托管 RPC 入口，保留 Contract v1 方法和数据，不修改既有扩展 ABI。</summary>
public interface INonetManagedRpcPlugin : IAsyncDisposable
{
    ValueTask<JsonElement> CallAsync(string method, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
}

/// <summary>可选运行时连接：日志和后台任务归属到当前插件。旧插件无需实现。</summary>
public interface INonetRuntimeAware
{
    void AttachRuntime(PluginRuntimeContext context);
}

/// <summary>不是安全沙箱。后台工作应使用 RunAsync，禁止无监督线程和 async void。</summary>
public sealed class PluginRuntimeContext(CancellationToken lifetime,
    Action<string, string> log, Func<Func<CancellationToken, Task>, Task> run)
{
    public CancellationToken Lifetime { get; } = lifetime;
    public void Log(string message, string level = "info") => log(level, message);
    /// <summary>观察任务异常并停用插件；停止是协作式的，不能强制终止任意托管代码。</summary>
    public Task RunAsync(Func<CancellationToken, Task> work) => run(work);
}

/// <summary>主动报告可恢复业务失败，不应被视为程序错误而停用。</summary>
public sealed class PluginOperationException(string message) : Exception(message);
