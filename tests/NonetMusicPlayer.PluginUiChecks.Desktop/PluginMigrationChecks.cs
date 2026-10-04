using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using NonetMusicPlayer.Desktop;
using NonetMusicPlayer.Desktop.Plugins;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

internal static class PluginMigrationChecks
{
    public static void Run(string output)
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        output = Path.GetFullPath(output);
        var run = Path.Combine(output, "plugin-migration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run);
        var storage = new AppStorage(Path.Combine(run, "source"), Path.Combine(run, "isolated-installation"));
        using var vm = new MainViewModel(new MusicLibraryScanner(storage), new FakeAudio());
        var manager = vm.Plugins;
        var beforePackage = Package(run, "review.before", "1.0.0");
        var removePackage = Package(run, "review.removed", "1.0.0");
        var laterPackage = Package(run, "review.pending", "1.0.0");
        var blockedPackage = Package(run, "review.blocked", "1.0.0");
        var providerPackage = Package(run, "review.provider", "1.0.0", provider: true);
        var before = manager.Install(beforePackage); var removed = manager.Install(removePackage); var provider = manager.Install(providerPackage);
        manager.SetEnabled(before, true); manager.SetEnabled(provider, true);
        manager.Configure(removed, "{\"label\":\"original archive configuration\"}");
        var secret = "FAKE-SESSION-" + Guid.NewGuid().ToString("N");
        var configuration = JsonSerializer.Serialize(new
        {
            server = "https://music.example.invalid", displayName = "safe original config",
            token = secret + "-token", password = secret + "-password", Authorization = "Bearer " + secret + "-auth",
            nested = new { keep = "safe nested", cookie = secret + "-cookie", api_key = secret + "-api" },
            array = new[] { new { label = "safe array", sessionSecret = secret + "-secret", credentials = secret + "-credential" } }
        });
        manager.Configure(provider, configuration);
        Check(SessionConfiguration(manager)[provider.Id] == configuration && !provider.Configuration.Contains(secret), "Session config retains live secrets while manifest is scrubbed");

        // Fixtures deliberately contain externally supplied private files: they must remain at
        // the old root, but the migration must not copy them into the pending runtime root.
        var beforeRoot = Path.Combine(storage.PluginsFolder, before.Id);
        File.WriteAllText(Path.Combine(beforeRoot, "secrets.json"), "externally-managed-private-file");
        File.WriteAllText(Path.Combine(beforeRoot, ".env"), "externally-managed-env");
        File.WriteAllText(Path.Combine(beforeRoot, "client.key"), "externally-managed-key");
        var credentials = Path.Combine(beforeRoot, "credentials"); Directory.CreateDirectory(credentials);
        File.WriteAllText(Path.Combine(credentials, "private.json"), "externally-managed-credentials");
        var staging = Path.Combine(storage.PluginsFolder, ".stage-test-private"); Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "private.json"), "externally-managed-stage");
        var copyFixtures = Path.Combine(storage.Root, "MigrationFixtures"); Directory.CreateDirectory(copyFixtures);
        for (var i = 0; i < 512; i++) File.WriteAllBytes(Path.Combine(copyFixtures, i.ToString("D4") + ".bin"), new byte[1024]);
        vm.Save();

        var next = Path.Combine(run, "pending");
        var index = Path.Combine(storage.PluginsFolder, "installed.json");
        var indexBefore = File.ReadAllText(index); var providerBefore = provider.Configuration;
        var migration = vm.ConfigureDataDirectoryAsync(next);
        var timer = Stopwatch.StartNew();
        while (!storage.IsMigrating && !migration.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        while (!Directory.Exists(next) && !migration.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(1);
        Check(storage.IsMigrating && vm.IsMigratingData && !migration.IsCompleted, "Real async migration exposes a protected transaction before UI commit");
        var copyingObserved = Directory.Exists(next) && storage.PendingRoot is null;
        MigrationReject(() => manager.Install(blockedPackage), "install");
        MigrationReject(() => manager.SetEnabled(before, false), "enable/disable");
        MigrationReject(() => manager.Configure(provider, "{\"server\":\"do-not-apply\",\"token\":\"rejected-token\"}"), "configure");
        MigrationReject(() => manager.Uninstall(removed), "uninstall");
        MigrationReject(manager.Save, "save");
        MigrationReject(() => storage.ArchivePendingPluginRemoval(removed.Id), "pending removal archive");
        Check(manager.Installed.Count == 3 && before.Enabled && provider.Configuration == providerBefore
            && SessionConfiguration(manager)[provider.Id] == configuration && File.ReadAllText(index) == indexBefore
            && Directory.Exists(Path.Combine(storage.PluginsFolder, removed.Id))
            && !Directory.Exists(Path.Combine(storage.PluginsFolder, "review.blocked")), "Rejected writes change neither live manifest/session nor old index/packages");
        Check(manager.LoadPage(before).Widgets.Count == 1, "Existing UI plugin stays readable while mutations are guarded");
        Wait(migration); var result = migration.GetAwaiter().GetResult();
        Check(!storage.IsMigrating && !vm.IsMigratingData && result.RequiresRestart && storage.Root != next && storage.PendingRoot == next, "Completed copy keeps current root active and lifts write guard");
        Console.WriteLine("PASS PLUGIN MIGRATION: async guard before mutation; worker-copy observation=" + copyingObserved);

        CheckIndexesEqual(storage, next, "Initial migration preserves installed index");
        Check(File.Exists(Path.Combine(next, "Plugins", before.Id, "page.json")) && File.Exists(Path.Combine(beforeRoot, "secrets.json"))
            && !File.Exists(Path.Combine(next, "Plugins", before.Id, "secrets.json"))
            && !File.Exists(Path.Combine(next, "Plugins", before.Id, ".env"))
            && !File.Exists(Path.Combine(next, "Plugins", before.Id, "client.key"))
            && !Directory.Exists(Path.Combine(next, "Plugins", before.Id, "credentials"))
            && !Directory.Exists(Path.Combine(next, "Plugins", ".stage-test-private")), "Migration copies usable package files, excludes private/staged entries, preserves old originals");

        // Do not call vm.Save between these operations: PluginManager.Save alone must synchronize.
        var later = manager.Install(laterPackage);
        Check(File.Exists(Path.Combine(next, "Plugins", later.Id, "page.json")), "Pending-stage install immediately copies package without VM save");
        CheckIndexesEqual(storage, next, "Pending-stage install index synchronized");
        manager.SetEnabled(later, true);
        Check(ReadIndex(next).Single(p => p.Id == later.Id).Enabled, "Pending-stage enable is immediately persisted at new root");
        manager.Configure(provider, configuration.Replace("safe original config", "safe latest config", StringComparison.Ordinal));
        var pendingProvider = ReadIndex(next).Single(p => p.Id == provider.Id);
        using (var config = JsonDocument.Parse(pendingProvider.Configuration))
        {
            Check(config.RootElement.GetProperty("displayName").GetString() == "safe latest config"
                && config.RootElement.GetProperty("nested").GetProperty("keep").GetString() == "safe nested"
                && config.RootElement.GetProperty("array")[0].GetProperty("label").GetString() == "safe array", "Latest safe nested/array configuration survives immediate mirroring");
        }
        CheckIndexesEqual(storage, next, "Pending-stage configure index synchronized");
        manager.SetEnabled(before, false); Check(!ReadIndex(next).Single(p => p.Id == before.Id).Enabled, "Pending-stage disable synchronized");
        manager.SetEnabled(before, true);

        manager.Uninstall(removed);
        var removedArchives = Directory.GetDirectories(Path.Combine(next, "Backups", "RemovedPlugins"));
        var archive = removedArchives.Single(folder => Path.GetFileName(folder).StartsWith(removed.Id + "-", StringComparison.Ordinal));
        Check(File.Exists(Path.Combine(archive, "page.json")) && File.Exists(Path.Combine(archive, "manifest.json"))
            && !Directory.Exists(Path.Combine(storage.PluginsFolder, removed.Id)) && !Directory.Exists(Path.Combine(next, "Plugins", removed.Id))
            && !ReadIndex(next).Any(p => p.Id == removed.Id), "Uninstall removes old/pending active package and records recoverable pending package archive");
        storage.SyncPendingResources(); vm.Save(); manager.Save();
        Check(!Directory.Exists(Path.Combine(next, "Plugins", removed.Id)) && !ReadIndex(next).Any(p => p.Id == removed.Id), "Subsequent resource/state/index synchronization cannot resurrect uninstalled package");
        var reinstalled = manager.Install(removePackage);
        Check(!reinstalled.Enabled && reinstalled.Configuration == "{}" && ReadIndex(next).Single(p => p.Id == reinstalled.Id).Configuration == "{}"
            && File.Exists(Path.Combine(next, "Plugins", reinstalled.Id, "page.json")) && File.Exists(Path.Combine(archive, "page.json")), "Same ID can reinstall cleanly without replacing recoverable archive or restoring old config");
        manager.SetEnabled(reinstalled, true); CheckIndexesEqual(storage, next, "Reinstallation/enable synchronized");

        var foreign = new PluginManifest { Id = provider.Id, Name = provider.Name, Type = "provider", Permissions = ["network", "process"] };
        Reject(() => manager.Configure(foreign, "{\"token\":\"must-not-enter-session\"}"), "Foreign manifest instance cannot configure installed plugin");
        AssertSecretsAbsent(storage.Root, secret); AssertSecretsAbsent(next, secret);
        using (var restarted = new PluginManager(new AppStorage(next, Path.Combine(run, "isolated-restart"))))
        {
            Check(restarted.Installed.Count == 4 && restarted.Installed.Single(p => p.Id == before.Id).Enabled && restarted.Installed.Single(p => p.Id == later.Id).Enabled
                && restarted.Installed.Single(p => p.Id == reinstalled.Id).Enabled && SessionConfiguration(restarted).Count == 0, "Restart reads current plugin state but never recovers prior session secrets");
            Check(restarted.LoadPage(restarted.Installed.Single(p => p.Id == reinstalled.Id)).Widgets.Count == 1, "Reinstalled page remains usable at new root after restart");
        }
        Console.WriteLine("PASS PLUGIN MIGRATION: install/configure/enable mirror, private-file exclusion, recoverable uninstall, no resurrection, clean reinstall, restart, nested session-secret non-persistence");
    }
    private static List<PluginManifest> ReadIndex(string root) => JsonSerializer.Deserialize<List<PluginManifest>>(File.ReadAllText(Path.Combine(root, "Plugins", "installed.json")), AppStorage.Json)!;
    private static void CheckIndexesEqual(AppStorage storage, string next, string message)
        => Check(File.ReadAllText(Path.Combine(storage.PluginsFolder, "installed.json")) == File.ReadAllText(Path.Combine(next, "Plugins", "installed.json")), message);
    private static Dictionary<string, string> SessionConfiguration(PluginManager manager)
        => (Dictionary<string, string>)typeof(PluginManager).GetField("_sessionConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
    private static void AssertSecretsAbsent(string root, string secret)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f) is ".json" or ".bak"))
            Check(!File.ReadAllText(file).Contains(secret), "Session secret cannot occur in index, backup, state, or archive: " + Path.GetRelativePath(root, file));
    }
    private static string Package(string run, string id, string version, bool provider = false)
    {
        var path = Path.Combine(run, id + ".impp");
        var manifest = new PluginManifest { Id = id, Name = id, Type = provider ? "provider" : "ui", Version = version };
        if (provider) { manifest.Permissions = ["network", "process"]; manifest.EntryPoints = new() { ["win-x64"] = "fixture.exe" }; }
        else manifest.PageEntry = "page.json";
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, AppStorage.Json));
        using (var writer = new StreamWriter(zip.CreateEntry(provider ? "fixture.exe" : "page.json").Open()))
            writer.Write(provider ? "not executable; this fixture is never launched" : "{\"schemaVersion\":1,\"title\":\"迁移测试\",\"widgets\":[{\"type\":\"text\",\"text\":\"native page remains readable\"}]}");
        return path;
    }
    private static void Wait(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted) { if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Plugin migration test timed out"); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        task.GetAwaiter().GetResult();
    }
    private static void MigrationReject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException error) when (error.Message.Contains("复制") || error.Message.Contains("迁移")) { return; }
        throw new InvalidOperationException("Mutation must reject during data copying: " + message);
    }
    private static void Reject(Action action, string message)
    { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException(message); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FakeAudio : IAudioPlayer
    {
        public bool IsAvailable => true; public bool IsPlaying => false; public TimeSpan Position { get; set; } public TimeSpan Duration => TimeSpan.Zero; public float Volume { get; set; }
        public event EventHandler? PlaybackStopped { add { } remove { } }
        public Task LoadAsync(string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Play() { } public void Pause() { } public void Stop() { } public void Dispose() { }
    }
}
