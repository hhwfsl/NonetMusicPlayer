using NonetMusicPlayer.Core.Plugins;

/// <summary>跨平台开发入口；默认从当前工程的 plugin 目录生成最小 impp 包。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] is "help" or "--help" or "-h") { Help(); return 0; }
            var action = args.FirstOrDefault() ?? "pack";
            if (action == "validate")
            {
                if (args.Length != 2) throw new ArgumentException("Usage: validate <plugin.impp>");
                var valid = PluginPackageInspector.Inspect(args[1]); Console.WriteLine($"Valid: {valid.Id} {valid.Version} (Contract {valid.ContractVersion})"); return 0;
            }
            if (action != "pack") throw new ArgumentException("Unknown action. Use help.");
            var source = Path.GetFullPath("plugin"); string? output = null; var force = false; var include = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                switch (args[i])
                {
                    case "--source": source = Path.GetFullPath(Value()); break;
                    case "--output": output = Path.GetFullPath(Value()); break;
                    case "--force": force = true; break;
                    case "--include": include.Add(Value()); break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (output is null)
            {
                // 默认包名从清单读取，但最终清单及页面仍必须通过宿主同款完整验证。
                using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")));
                var id = manifest.RootElement.GetProperty("id").GetString() ?? "plugin";
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9.-]{2,80}$")) throw new InvalidDataException("Invalid plugin ID.");
                output = Path.GetFullPath(Path.Combine("publish", id + ".impp"));
            }
            Console.WriteLine(PluginPackageBuilder.Pack(source, output, force, include)); return 0;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
        { Console.Error.WriteLine("Build failed: " + error.Message); return 1; }
    }
    private static void Help() => Console.WriteLine("NonetMusicPlayerPlugin packager\npack [--source <folder>] [--output <file.impp>] [--force] [--include <relative-file>]\nvalidate <file.impp>\nRun from the project root. No arguments builds plugin/ to publish/.");
}
