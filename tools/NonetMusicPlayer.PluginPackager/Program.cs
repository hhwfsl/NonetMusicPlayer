using NonetMusicPlayer.Core.Plugins;

/// <summary>主项目维护唯一打包入口；独立插件工程依赖宿主 SDK，不携带另一套验证代码。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine("Nonet plugin packager\npack --source <folder> --output <file.impp> [--rid <win-x64|osx-x64|linux-x64>] [--force] [--include <relative-file>]\nvalidate <file.impp>"); return 0;
            }
            if (args[0] == "validate" && args.Length == 2)
            {
                var manifest = PluginPackageInspector.Inspect(args[1]); Console.WriteLine($"Valid: {manifest.Id} {manifest.Version} (Contract {manifest.ContractVersion})"); return 0;
            }
            if (args[0] != "pack") throw new ArgumentException("Unknown action. Use help.");
            string? source = null, output = null, rid = null; var force = false; var include = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                switch (args[i])
                {
                    case "--source": source = Path.GetFullPath(Value()); break;
                    case "--output": output = Path.GetFullPath(Value()); break;
                    case "--rid": rid = Value(); break;
                    case "--force": force = true; break;
                    case "--include": include.Add(Value()); break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (source is null || output is null) throw new ArgumentException("pack requires --source and --output.");
            // 新开发插件必须声明仓库坐标。运行时仍允许没有此字段的旧插件正常安装。
            using var input = File.OpenRead(Path.Combine(source, "manifest.json"));
            var plugin = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(input, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty manifest.");
            PluginRepository.Validate(plugin.RepositoryOwner, plugin.RepositoryName);
            if (plugin.EntryPoints.Count > 0 && rid is null) throw new InvalidDataException("Process plugins require --rid; publish one .impp per platform.");
            if (plugin.OriginRepository.Length != 0 || plugin.Enabled || plugin.AudioTagWriteConsent || plugin.Configuration != "{}")
                throw new InvalidDataException("Source manifest cannot contain host installation state.");
            Console.WriteLine(PluginPackageBuilder.Pack(source, output, force, include, rid)); return 0;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        { Console.Error.WriteLine("Build failed: " + error.Message); return 1; }
    }
}
