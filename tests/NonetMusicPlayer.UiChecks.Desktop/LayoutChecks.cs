using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;

internal static class LayoutChecks
{
    public static void Run(string output)
    {
        var storage = new AppStorage(Path.Combine(output, "layout-checks-" + Guid.NewGuid().ToString("N")));
        var service = new UiLayoutService(storage);
        Require(File.Exists(service.Path), "Default layout generated");
        Require(service.Current.SchemaVersion == 2 && service.Current.Player.Columns.SequenceEqual(new[] { "*", "210", "*" }), "Symmetric version 2 default grid");
        Require(Items(service.Current.Player).Count(i => i.Id is not null) == 12 && !Items(service.Current.Player).Any(i => i.Id == "lyrics"), "All 12 necessary controls; cover retains lyrics action");
        service.Read(UiLayoutService.DefaultJson.Replace("\"schemaVersion\": 2,", "\"schemaVersion\": 2, /* comment */"));
        service.Read(UiLayoutService.DefaultJson.Replace("\"rows\": [\"24\", \"*\"]", "\"rows\": [\"24\", \"*\",]"));
        Require(service.NormalizeJson(UiLayoutService.DefaultJson) == UiLayoutService.DefaultJson, "Version 2 comments and handwritten formatting preserved");
        Reject(service, Mutate(root => root["schemaVersion"] = 3), "$.schemaVersion");
        Reject(service, Mutate(root => root["xaml"] = "<Button/>"), "$.xaml");
        Reject(service, UiLayoutService.DefaultJson.Replace("\"schemaVersion\": 2,", "\"schemaVersion\": 2, \"schemaVersion\": 2,"), "重复");
        Reject(service, Mutate(root => root["player"]!["items"]![0]!["id"] = "unknown"), ".id");
        Reject(service, Mutate(root => root["player"]!["items"]![0]!["id"] = "lyrics"), "版本 2 已移除");
        Reject(service, Mutate(root => root["player"]!["items"]![0]!["columnSpan"] = 4), "网格");
        Reject(service, Mutate(root => root["player"]!["items"]![0]!["opacity"] = 0), ".opacity");
        Reject(service, Mutate(root => root["player"]!["items"]![0]!["margin"] = new JsonArray(-1, 0, 0, 0)), ".margin");
        Reject(service, Mutate(root => root["player"]!["items"]!.AsArray().RemoveAt(0)), "progress");
        Reject(service, Mutate(root => root["player"]!["items"]![1]!["grid"]!["items"]![1]!["id"] = "artwork"), "重复");
        Reject(service, Mutate(root => root["player"]!["items"]![2]!["grid"]!["items"]![0]!["width"] = 8), "32");
        Reject(service, Mutate(root => root["player"]!["items"]![1]!["grid"]!["items"]![1]!["column"] = 0), "同一个网格");
        Reject(service, Mutate(root => root["player"]!["rows"]![0] = "0"), ".rows[0]");
        Reject(service, Mutate(root => root["player"]!["items"]![1]!["id"] = "title"), "id 或 grid");
        Reject(service, Mutate(root => root["workspace"] = JsonNode.Parse("""{"Navigation":{"x":0,"y":0,"width":0.2,"height":0.8}}""")), "缺少");
        Reject(service, Mutate(root => root["workspace"] = JsonNode.Parse("""{"Navigation":{"x":0,"y":0,"width":0.2,"height":0.8},"Content":{"x":0.1,"y":0,"width":0.9,"height":0.8},"Player":{"x":0,"y":0.8,"width":1,"height":0.2}}""")), "重叠");
        Reject(service, new string(' ', UiLayoutService.MaximumBytes + 1), "100 KB");
        Reject(service, "{\n \"schemaVersion\":,\n}", "第 2 行");
        Reject(service, Mutate(root =>
        {
            var leaf = root["player"]!["items"]![1]!["grid"]!["items"]![0]!.DeepClone();
            leaf["row"] = 0; leaf["column"] = 0; leaf["rowSpan"] = 1;
            for (var i = 0; i < 4; i++) leaf = new JsonObject { ["row"] = 0, ["column"] = 0, ["grid"] = new JsonObject { ["rows"] = new JsonArray("*"), ["columns"] = new JsonArray("*"), ["items"] = new JsonArray(leaf) } };
            root["player"]!["items"]![1]!["grid"]!["items"]![0] = leaf;
        }), "4 层");

        var changed = Mutate(root => root["player"]!["items"]![1]!["grid"]!["items"]![1]!["fontSize"] = 15);
        service.Apply(changed);
        Require(service.HasBackup && File.ReadAllText(service.BackupPath) == UiLayoutService.DefaultJson, "Valid previous layout backed up");
        var previous = service.Current;
        try { service.Apply("{}"); throw new InvalidOperationException("Invalid Apply accepted"); } catch (UiLayoutException) { }
        Require(ReferenceEquals(previous, service.Current) && File.ReadAllText(service.Path) == changed, "Invalid apply changes neither memory nor file");
        service.Reload(); Require(Items(service.Current.Player).Single(i => i.Id == "title").FontSize == 15, "Reload handwritten layout");
        Require(service.AppliedJson == changed, "AppliedJson follows reload source");
        service.RestoreBackup(); Require(Items(service.Current.Player).Single(i => i.Id == "title").FontSize == 18, "Restore previous backup");
        AppStorage.AtomicWrite(service.Path, "{ broken");
        var recovered = new UiLayoutService(storage);
        Require(recovered.LastError is not null && Items(recovered.Current.Player).Single(i => i.Id == "title").FontSize == 15, "Corrupt startup falls back to valid backup");
        Require(recovered.AppliedJson == changed, "AppliedJson reflects valid backup rather than corrupt primary");
        Require(File.ReadAllText(service.Path) == "{ broken", "Corrupt original preserved");
        var goodBackup = File.ReadAllText(service.BackupPath);
        recovered.RestoreDefault(); Require(File.ReadAllText(service.BackupPath) == goodBackup, "Recovery does not replace good backup with malformed primary");
        var diskValidButUnusable = Mutate(root => root["player"]!["rows"]![0] = "2000");
        AppStorage.AtomicWrite(service.Path, diskValidButUnusable);
        service.Apply(changed, UiLayoutService.DefaultJson);
        Require(File.ReadAllText(service.BackupPath) == UiLayoutService.DefaultJson, "Renderer's last usable layout wins over geometrically unusable external disk file");
        Require(service.AppliedJson == changed, "AppliedJson follows explicit usable-backup apply");
        try { service.Apply(UiLayoutService.DefaultJson, "{}"); throw new InvalidOperationException("Invalid previous usable JSON accepted"); } catch (UiLayoutException) { }
        Require(File.ReadAllText(service.Path) == changed && service.AppliedJson == changed, "Invalid usable-backup input changes neither memory nor file");
        var blockedStorage = new AppStorage(Path.Combine(output, "layout-blocked-" + Guid.NewGuid().ToString("N")));
        File.WriteAllText(Path.Combine(blockedStorage.Root, "Layouts"), "fixture blocks creating layout directory");
        var blockedService = new UiLayoutService(blockedStorage);
        Require(blockedService.LastError is not null && Items(blockedService.Current.Player).Count() == 12, "First layout save failure still permits memory default and startup");
        CheckLegacyMigration(output, service);
        foreach (var sample in new[] { "default.layout.json", "compact.layout.json" })
        {
            var sampleDocument = service.Read(File.ReadAllText(Path.Combine("docs", "layouts", sample)));
            Require(sampleDocument.SchemaVersion == 2 && Items(sampleDocument.Player).Count() == 12, "Version 2 sample " + sample);
        }
        Console.WriteLine("Layout checks passed: 12 controls, strict parsing, schema 1 migration, normalization, atomic apply and recovery.");
    }

    private static void CheckLegacyMigration(string output, UiLayoutService parser)
    {
        var legacy = LegacyJson();
        var migrated = parser.Read(legacy);
        var leaves = Items(migrated.Player).ToArray();
        Require(migrated.SchemaVersion == 2 && leaves.Length == 12 && leaves.All(i => i.Id != "lyrics"), "Legacy lyrics button removed from returned version 2 tree");
        Require(migrated.Player.Columns.SequenceEqual(new[] { "*", "142", "*" }), "Legacy grid tracks and positions preserved");
        Require(leaves.Single(i => i.Id == "previous").Width == 36 && leaves.Single(i => i.Id == "play").Width == 42 && leaves.Single(i => i.Id == "volume").Height == 36, "Legacy implicit button dimensions preserved against new defaults");
        Require(leaves.Single(i => i.Id == "title").FontSize == 16 && leaves.Single(i => i.Id == "artist").FontSize == 12, "Legacy implicit font sizes preserved");
        var normalized = parser.NormalizeJson(legacy);
        Require(parser.NormalizeJson(normalized) == normalized && parser.Read(normalized).SchemaVersion == 2, "Migration text normalizes idempotently without reflection");
        var withoutDeprecated = LegacyJson(root => root["player"]!["items"]![3]!["grid"]!["items"]!.AsArray().RemoveAt(5));
        Require(Items(parser.Read(withoutDeprecated).Player).Count() == 12, "Already removed legacy lyrics is accepted");
        var isolated = LegacyJson(root =>
        {
            var right = root["player"]!["items"]![3]!["grid"]!["items"]!.AsArray();
            right[5] = new JsonObject { ["row"] = 0, ["column"] = 5, ["grid"] = new JsonObject { ["rows"] = new JsonArray("*"), ["columns"] = new JsonArray("*"), ["items"] = new JsonArray(new JsonObject { ["id"] = "lyrics", ["row"] = 0, ["column"] = 0 }) } };
        });
        var isolatedResult = parser.Read(isolated);
        Require(isolatedResult.Player.Items[3].Grid!.Items.Count == 5 && Items(isolatedResult.Player).Count() == 12, "Containers emptied only by obsolete lyrics are removed safely");
        Reject(parser, LegacyJson(root => root["player"]!["items"]![3]!["grid"]!["items"]![5]!["width"] = 8), "32");
        Reject(parser, LegacyJson(root => root["player"]!["items"]![3]!["grid"]!["items"]![5]!["column"] = 6), "网格");
        Reject(parser, LegacyJson(root => root["player"]!["items"]![3]!["grid"]!["items"]![4]!["id"] = "lyrics"), "重复");
        Reject(parser, LegacyJson(root => root["player"]!["items"]![3]!["grid"]!["items"]![5]!["opacity"] = 0), ".opacity");

        var storage = new AppStorage(Path.Combine(output, "legacy-layout-" + Guid.NewGuid().ToString("N")));
        var active = Path.Combine(storage.Root, "Layouts", "active.layout.json");
        AppStorage.AtomicWrite(active, legacy); AppStorage.AtomicWrite(active + ".bak", UiLayoutService.DefaultJson);
        var restored = new UiLayoutService(storage);
        Require(restored.Current.SchemaVersion == 2 && restored.AppliedJson == normalized && restored.MigrationMessage is not null, "Startup migrates a valid legacy active file");
        Require(File.ReadAllText(active) == normalized && File.ReadAllText(restored.LegacyBackupPath) == legacy, "Original schema 1 text preserved separately before migration");
        Require(File.ReadAllText(active + ".bak") == UiLayoutService.DefaultJson, "Existing last usable backup not overwritten by startup migration");
        restored.Reload(); Require(restored.MigrationMessage is null && restored.AppliedJson == normalized, "Reload version 2 is migration-free and stable");
        var alternateLegacy = LegacyJson(root => root["player"]!["items"]![1]!["grid"]!["items"]![1]!["fontSize"] = 19);
        restored.Apply(alternateLegacy, UiLayoutService.DefaultJson);
        Require(restored.Read(File.ReadAllText(active)).SchemaVersion == 2 && File.ReadAllText(restored.LegacyBackupPath) == legacy, "Import migrates without replacing original schema 1 archive");
        Require(File.ReadAllText(restored.BackupPath) == UiLayoutService.DefaultJson, "Explicit actually usable backup preserved during legacy import");
        restored.Apply(UiLayoutService.DefaultJson, legacy);
        Require(parser.Read(File.ReadAllText(restored.BackupPath)).SchemaVersion == 2 && !File.ReadAllText(restored.BackupPath).Contains("\"lyrics\""), "Old previous-usable input normalized into restorable version 2 backup");

        var blocked = new AppStorage(Path.Combine(output, "legacy-blocked-" + Guid.NewGuid().ToString("N")));
        var blockedPath = Path.Combine(blocked.Root, "Layouts", "active.layout.json"); AppStorage.AtomicWrite(blockedPath, legacy);
        Directory.CreateDirectory(Path.Combine(blocked.BackupFolder, "active.layout.json.schema1.bak"));
        var inMemory = new UiLayoutService(blocked);
        Require(inMemory.LastError is not null && inMemory.Current.SchemaVersion == 2 && Items(inMemory.Current.Player).Count() == 12, "Migration backup I/O failure does not prevent memory compatibility");
        Require(File.ReadAllText(blockedPath) == legacy, "Migration backup failure never overwrites original active file");
    }

    private static string LegacyJson(Action<JsonObject>? mutate = null)
    {
        var root = JsonNode.Parse(UiLayoutService.DefaultJson, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
        root["schemaVersion"] = 1;
        root["player"]!["rows"] = new JsonArray("20", "*"); root["player"]!["columns"] = new JsonArray("*", "142", "*");
        var items = root["player"]!["items"]!.AsArray(); items[0]!.AsObject().Remove("height");
        items[1]!["grid"]!["columns"] = new JsonArray("68", "*");
        items[2]!["grid"]!["columns"] = new JsonArray("44", "54", "44");
        foreach (var item in items[2]!["grid"]!["items"]!.AsArray()) { item!.AsObject().Remove("width"); item.AsObject().Remove("height"); }
        foreach (var item in items[1]!["grid"]!["items"]!.AsArray()) item!.AsObject().Remove("fontSize");
        var right = items[3]!["grid"]!.AsObject(); right["columns"] = new JsonArray("112", "38", "38", "38", "38", "38");
        foreach (var item in right["items"]!.AsArray()) { item!.AsObject().Remove("width"); item.AsObject().Remove("height"); }
        right["items"]!.AsArray().Add(new JsonObject { ["id"] = "lyrics", ["row"] = 0, ["column"] = 5, ["horizontalAlignment"] = "Center" });
        mutate?.Invoke(root); return root.ToJsonString();
    }

    private static IEnumerable<UiLayoutItem> Items(UiPlayerLayout grid) => grid.Items.SelectMany(item => item.Grid is { } nested ? Items(nested) : [item]);
    private static string Mutate(Action<JsonObject> action)
    {
        var root = JsonNode.Parse(UiLayoutService.DefaultJson, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
        action(root); return root.ToJsonString();
    }
    private static void Reject(UiLayoutService service, string value, string fragment)
    {
        try { service.Read(value); throw new InvalidOperationException("Invalid layout was accepted: " + fragment); }
        catch (UiLayoutException e) { Require(e.Message.Contains(fragment, StringComparison.Ordinal), "Error location/reason: " + e.Message); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
