using System.IO.Compression;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;

/// <summary>只写 artifacts 夹具；验证初始工程能打包、配置、拒绝越界和不破坏已有输出。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args.FirstOrDefault() ?? ".");
            var work = Path.Combine(root, "artifacts", "checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            var package = PluginPackageBuilder.Pack(Path.Combine(root, "plugin"), Path.Combine(work, "hello.impp"));
            var manifest = PluginPackageInspector.Inspect(package); Require(manifest.Type == "ui" && !manifest.Enabled, "Manifest is valid and installs disabled.");
            using (var zip = ZipFile.OpenRead(package)) Require(zip.Entries.Count == 4 && zip.GetEntry("LICENSE") is not null && zip.Entries.All(e => !e.FullName.Contains("sdk") && !e.FullName.Contains("samples")), "Only runtime JSON and the required GPL license are distributed.");
            using var schemaFile = File.OpenRead(Path.Combine(root, "plugin", PluginConfigSchema.FileName)); var schema = PluginConfigSchema.Read(schemaFile);
            var values = PluginConfigSchema.Defaults(schema); values["greeting"] = "你好，プラグイン！"; PluginConfigSchema.Validate(schema, values);
            var page = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "plugin", manifest.PageEntry)))!;
            using var resolved = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(PluginConfigSchema.Substitute(page, values).ToJsonString()));
            Require(PluginPageContract.Read(resolved).Widgets.Single().Text == "你好，プラグイン！", "Developer schema resolves user-filled configuration.");
            var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package));
            Reject(() => PluginPackageBuilder.Pack(Path.Combine(root, "plugin"), package), "Existing output is protected.");
            Require(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package))), "Rejected build leaves old package intact.");
            Reject(() => PluginPackageBuilder.Pack(Path.Combine(root, "plugin"), Path.Combine(work, "bad.impkg")), "Only impp is accepted.");
            Reject(() => PluginPathPolicy.ValidateRelativePath("../secret"), "Directory traversal is rejected.");
            Reject(() => PluginPackageBuilder.Pack(Path.Combine(root, "plugin"), Path.Combine(work, "extra.impp"), additionalFiles: ["page.json", "../README.md"]), "Undeclared escaping files are rejected.");
            var oversized = Path.Combine(work, "oversized-license.impp");
            using (var sourceZip = ZipFile.OpenRead(package))
            using (var invalidZip = ZipFile.Open(oversized, ZipArchiveMode.Create))
            {
                foreach (var entry in sourceZip.Entries.Where(e => e.FullName != "LICENSE"))
                { using var input = entry.Open(); using var destination = invalidZip.CreateEntry(entry.FullName).Open(); input.CopyTo(destination); }
                using var writer = new StreamWriter(invalidZip.CreateEntry("LICENSE").Open()); writer.Write(new string('x', 256 * 1024 + 1));
            }
            Reject(() => PluginPackageInspector.Inspect(oversized), "License exception remains bounded and cannot bypass package policy.");
            Console.WriteLine("PASS minimal package, configuration, overwrite protection and path validation"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message) { try { action(); } catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException) { return; } throw new InvalidOperationException(message); }
}
