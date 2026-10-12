using System.Reflection;
using System.Runtime.Loader;

namespace NonetMusicPlayer.Core.Plugins;

/// <summary>共享宿主运行时及 SDK，依赖按插件隔离；不是崩溃隔离或 OS 沙箱。</summary>
public sealed class ManagedPluginLoader : IAsyncDisposable
{
    private readonly Context _context;
    public object Instance { get; }
    public ManagedPluginLoader(string path, string className, PluginRuntimeContext? runtime = null)
    {
        _context = new(path);
        try
        {
            using var stream = File.OpenRead(path);
            Instance = Activator.CreateInstance(_context.LoadFromStream(stream).GetType(className, true)!) ?? throw new InvalidDataException("Empty managed plugin.");
            if (Instance is INonetRuntimeAware aware && runtime is not null) aware.AttachRuntime(runtime);
        }
        catch { _context.Unload(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { if (Instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync(); else if (Instance is IDisposable disposable) disposable.Dispose(); }
        finally { _context.Unload(); }
    }
    private sealed class Context(string path) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(INonetExtension).Assembly.GetName().Name) return typeof(INonetExtension).Assembly;
            if (name.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true) return Default.LoadFromAssemblyName(name);
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
