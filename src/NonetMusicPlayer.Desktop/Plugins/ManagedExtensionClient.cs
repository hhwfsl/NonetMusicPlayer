using NonetMusicPlayer.Core.Plugins;
namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>托管扩展复用公共加载器，冻结的 INonetExtension 接口不变。</summary>
internal sealed class ManagedExtensionClient : IAsyncDisposable
{
    private readonly ManagedPluginLoader _loader;
    public INonetExtension Instance { get; }
    public ManagedExtensionClient(string path, string className, PluginRuntimeContext runtime)
    {
        _loader = new(path, className, runtime);
        Instance = _loader.Instance as INonetExtension ?? throw new InvalidDataException("Extension class must implement INonetExtension.");
    }
    public ValueTask DisposeAsync() => _loader.DisposeAsync();
}
