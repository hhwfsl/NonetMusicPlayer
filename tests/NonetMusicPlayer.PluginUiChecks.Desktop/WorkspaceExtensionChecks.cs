using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Plugins;
using NonetMusicPlayer.Desktop.Services;

internal static class WorkspaceExtensionChecks
{
    public static void Run(string output)
    {
        output = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var storage = new AppStorage(Path.Combine(output, "data"));
        var layout = new UiLayoutService(storage);
        Require(PluginApprovalPolicy.RequiresConfirmation("ask", "settings.set", true), "Ask keeps confirmation.");
        Require(!PluginApprovalPolicy.RequiresConfirmation("assist", "settings.set", true) && PluginApprovalPolicy.RequiresConfirmation("assist", "playlist.delete", true), "Assist separates reversible settings from deletion.");
        Require(!PluginApprovalPolicy.RequiresConfirmation("full", "playlist.delete", true), "Full applies only after host grants it.");
        var page = new JsonObject { ["schemaVersion"] = 2, ["root"] = new JsonObject { ["type"] = "stack", ["children"] = new JsonArray((JsonNode)new JsonObject { ["type"] = "text", ["text"] = "Data-only UI" }) } };
        var package = PluginDevelopmentService.CreatePackage(Path.Combine(output, "projects"), new() { ["id"] = "custom.workspace-test", ["name"] = "Workspace Test", ["page"] = page, ["layout"] = UiLayoutService.DefaultJson }, layout.Read);
        var manifest = PluginPackageInspector.Inspect(package);
        Require(manifest.Runtime == "declarative" && manifest.EntryPoints.Count == 0 && manifest.ApprovalMode == "ask", "Data package has no program or pre-granted approval.");
        var before = File.ReadAllBytes(package);
        Reject(() => PluginDevelopmentService.CreatePackage(Path.Combine(output, "projects"), new() { ["id"] = manifest.Id }, layout.Read));
        Require(before.SequenceEqual(File.ReadAllBytes(package)), "Existing project remains untouched.");
        using var archive = System.IO.Compression.ZipFile.OpenRead(package);
        using var input = archive.GetEntry("page.json")!.Open(); var definition = ExtensionContract.ReadPage(input);
        var runtime = new DeclarativeExtension(definition);
        runtime.InitializeAsync(new(2, "{}", output, ExtensionContract.Capabilities), default).GetAwaiter().GetResult();
        var frame = runtime.InvokeAsync(new("apply-layout", new()), default).GetAwaiter().GetResult();
        Require(frame.Requests.Single().Service == "ui", "Declarative layout goes through host service.");
        runtime.CompleteAsync(frame.Requests.Single().Id, new() { ["success"] = true }, default).GetAwaiter().GetResult();
        Require(frame.Requests.Count == 0, "Completed requests cannot replay.");
        Reject(() => PluginDevelopmentService.WriteSource(output, "custom.bad-test", new() { ["../escape.cs"] = "bad" }));
        var source = PluginDevelopmentService.WriteSource(Path.Combine(output, "projects"), "custom.source-test", new() { ["plugin/Example.cs"] = "// 中文注释：仅生成源码。" });
        Require(File.Exists(Path.Combine(source, "plugin/Example.cs")), "Scoped new source project can be generated.");
        Reject(() => PluginDevelopmentService.WriteSource(Path.Combine(output, "projects"), "custom.source-test", new() { ["plugin/Example.cs"] = "replace" }));
        // 审批授权保存在宿主索引，安装包不能预授予；重启后仍保持用户选择。
        var approvalPackage = Path.Combine(output, "approval.impp");
        using (var zip = System.IO.Compression.ZipFile.Open(approvalPackage, System.IO.Compression.ZipArchiveMode.Create))
        {
            manifest.Id = "custom.approval-test"; manifest.SupportsApprovalModes = true; manifest.ApprovalMode = "full";
            Write(zip, "manifest.json", JsonSerializer.Serialize(manifest, AppStorage.Json));
            Write(zip, "page.json", JsonSerializer.Serialize(definition, ExtensionJson.Default.ExtensionPage));
            Write(zip, "plugin_config_schema.json", """{"approvalMode":{"type":"string","default":"ask","enum":["ask","assist","full"]}}""");
        }
        using (var manager = new NonetMusicPlayer.Desktop.Plugins.PluginManager(storage))
        {
            var installed = manager.Install(approvalPackage);
            Require(installed.ApprovalMode == "ask", "Package cannot pre-grant full approval.");
            try { manager.Configure(installed, """{"approvalMode":"full"}"""); throw new Exception("Approval upgrade bypassed consent."); }
            catch (InvalidOperationException) { }
            manager.Configure(installed, """{"approvalMode":"full"}""", false, true);
            Require(manager.ConfigurationValues(installed)["approvalMode"]!.GetValue<string>() == "full", "Confirmed mode is reflected in protected configuration.");
        }
        using (var manager = new NonetMusicPlayer.Desktop.Plugins.PluginManager(storage))
        {
            var installed = manager.Installed.Single(p => p.Id == "custom.approval-test");
            Require(installed.ApprovalMode == "full", "Host approval survives restart.");
            manager.Configure(installed, """{"approvalMode":"ask"}""");
            Require(installed.ApprovalMode == "ask", "Reducing approval needs no new consent.");
        }
        Console.WriteLine("PASS approval install reset, consent guard, persistence and downgrade.");
        Console.WriteLine("PASS workspace layout validation, approval modes, data-only package/runtime, no project overwrite, source path confinement.");
    }
    private static void Write(System.IO.Compression.ZipArchive zip, string path, string text) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(text); }
    private static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new InvalidOperationException("Unsafe project accepted."); }
}
