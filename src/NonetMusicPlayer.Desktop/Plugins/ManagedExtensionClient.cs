using System.Reflection;
using System.Runtime.Loader;
using NonetMusicPlayer.Core.Plugins;
namespace NonetMusicPlayer.Desktop.Plugins;

/// <summary>用户授权的托管插件共享运行时，不是 OS 沙箱，不保证强制卸载。</summary>
internal sealed class ManagedExtensionClient : IAsyncDisposable
{
    private readonly Context _context;
    public INonetExtension Instance { get; }
    public ManagedExtensionClient(string path, string className)
    {
        _context = new(path);
        // 使用流加载而不是文件映射，Windows 更新时不锁住插件原 DLL。
        using var stream = File.OpenRead(path);
        Instance = Activator.CreateInstance(_context.LoadFromStream(stream).GetType(className, true)!) as INonetExtension
            ?? throw new InvalidDataException("Extension class must implement INonetExtension.");
    }
    public async ValueTask DisposeAsync() { try { await Instance.DisposeAsync(); } finally { _context.Unload(); } }
    private sealed class Context(string path) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(INonetExtension).Assembly.GetName().Name) return typeof(INonetExtension).Assembly;
            // 原生 UI 必须使用宿主已加载的 Avalonia 类型身份，不能加载第二份框架副本。
            if (name.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true)
                return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
            var resolved = _resolver.ResolveAssemblyToPath(name);
            if (resolved is null) return null;
            using var stream = File.OpenRead(resolved); return LoadFromStream(stream);
        }
        protected override nint LoadUnmanagedDll(string name)
        {
            var resolved = _resolver.ResolveUnmanagedDllToPath(name); return resolved is null ? 0 : LoadUnmanagedDllFromPath(resolved);
        }
    }
}
