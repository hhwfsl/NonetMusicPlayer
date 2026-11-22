using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace NonetMusicPlayer.Core.Updates;

/// <summary>由旧版自身的独立副本执行更新；等待退出、逐文件替换、失败回滚，用户数据不参与更新。</summary>
public static class UpdateInstaller
{
    /// <summary>复制旧宿主到更新暂存目录，在本进程退出后由该副本替换安装文件。</summary>
    public static Process LaunchHelper(PreparedUpdate update, string dataRoot, string backupRoot)
    {
        var executable = Environment.ProcessPath ?? throw new IOException("Application path unavailable");
        var runtime = Path.GetDirectoryName(executable)!;
        var expected = ReleaseUpdateService.ExecutableFor(update.Product, ReleaseUpdateService.PlatformRid);
        if (!string.Equals(update.Executable, expected, StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(executable), Path.GetFileName(expected), StringComparison.Ordinal))
            throw new InvalidOperationException("Updates require the published executable for this edition");
        var installation = update.Product == ReleaseProduct.Desktop && OperatingSystem.IsMacOS() && Path.GetFileName(runtime) == "MacOS"
            ? Path.GetFullPath(Path.Combine(runtime, "../..")) : runtime;
        UpdatePaths.RejectLinks(installation);
        var probe = Path.Combine(installation, ".nonet-update-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        var helperFolder = Path.Combine(update.StageRoot, "helper"); Directory.CreateDirectory(helperFolder);
        foreach (var path in Directory.EnumerateFiles(runtime))
        {
            var name = Path.GetFileName(path);
            // 只复制程序和运行时依赖，不复制引导配置、数据库或用户资源。
            if (path != executable && !(name.EndsWith(".dll") || name.EndsWith(".dylib") || name.Contains(".so") || name.EndsWith(".deps.json") || name.EndsWith(".runtimeconfig.json"))) continue;
            UpdatePaths.RejectLinks(path); File.Copy(path, Path.Combine(helperFolder, name), false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(helperFolder, name), File.GetUnixFileMode(path));
        }
        using var current = Process.GetCurrentProcess();
        var plan = new UpdateApplyPlan { Installation = installation, Payload = update.Payload, Executable = update.Executable, ParentId = Environment.ProcessId, ParentStartedUtc = current.StartTime.ToUniversalTime(), DataRoot = Path.GetFullPath(dataRoot), BackupRoot = Path.GetFullPath(backupRoot), Manifest = update.Manifest, Product = update.Product };
        var planPath = Path.Combine(update.StageRoot, "apply-plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, UpdateJsonContext.Default.UpdateApplyPlan));
        var start = new ProcessStartInfo(Path.Combine(helperFolder, Path.GetFileName(executable))) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helperFolder };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(planPath);
        return Process.Start(start) ?? throw new IOException("Unable to start update helper");
    }
    /// <summary>执行经过路径约束的更新计划，完成后重新启动已安装的程序。</summary>
    public static int RunHelper(string planPath, ReleaseProduct product = ReleaseProduct.Desktop)
    {
        UpdateApplyPlan? plan = null;
        var canRestart = false;
        string? resultPath = null;
        try
        {
            planPath = Path.GetFullPath(planPath); UpdatePaths.RejectLinks(planPath);
            var stage = Path.GetDirectoryName(planPath)!;
            var helper = Path.GetDirectoryName(Environment.ProcessPath)!;
            if (!UpdatePaths.Same(helper, Path.Combine(stage, "helper")) || Path.GetFileName(planPath) != "apply-plan.json")
                throw new InvalidDataException("Update helper must use its own stage");
            var lockPath = Path.Combine(stage, "helper.lock");
            UpdatePaths.RejectLinks(lockPath);
            using var running = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (new FileInfo(planPath).Length > 512 * 1024) throw new InvalidDataException("Update plan too large");
            plan = JsonSerializer.Deserialize(File.ReadAllText(planPath), UpdateJsonContext.Default.UpdateApplyPlan) ?? throw new InvalidDataException("Invalid update plan");
            if (plan.Product != product || !UpdatePaths.Same(plan.Payload, Path.Combine(stage, "payload"))
                || !Guid.TryParseExact(Path.GetFileName(stage), "N", out _) || !UpdatePaths.Same(Path.GetDirectoryName(stage)!, Path.Combine(plan.DataRoot, "Updates"))
                || plan.Executable != ReleaseUpdateService.ExecutableFor(product, ReleaseUpdateService.PlatformRid))
                throw new InvalidDataException("Update helper and payload must share their own stage");
            var result = Path.Combine(stage, "result.txt");
            UpdatePaths.RejectLinks(result);
            resultPath = result;
            canRestart = product == ReleaseProduct.Desktop;
            using (var parent = TryGetParent(plan))
            {
                if (parent is not null && !parent.WaitForExit(60_000)) throw new IOException("Application did not exit before update");
            }
            // 另一个实例若在旧实例退出后启动，Windows 文件锁会阻止替换并触发回滚。
            Apply(plan, Path.Combine(stage, "rollback"));
            // 失败还原仅保护本次替换事务，成功后的下载、助手和旧副本由新进程统一回收。
            var downloaded = Path.Combine(stage, "download.zip");
            UpdatePaths.RejectLinks(downloaded);
            if (File.Exists(downloaded)) File.Delete(downloaded);
            File.WriteAllText(resultPath, "success");
            // CLI 子命令完成后交回当前终端，下一次运行时进入新版本。
            if (canRestart) StartApplication(plan);
            return 0;
        }
        catch (Exception error)
        {
            if (resultPath is not null)
                try { File.WriteAllText(resultPath, error.ToString()); } catch (IOException) { }
            if (canRestart && plan is not null) { try { StartApplication(plan); } catch (Exception) { } }
            return 1;
        }
    }
    private static Process? TryGetParent(UpdateApplyPlan plan)
    {
        try { var parent = Process.GetProcessById(plan.ParentId); if (parent.StartTime.ToUniversalTime() == plan.ParentStartedUtc) return parent; parent.Dispose(); return null; }
        catch (ArgumentException) { return null; }
    }
    private static void StartApplication(UpdateApplyPlan plan)
    {
        var executable = Path.Combine(plan.Installation, plan.Executable.Replace('/', Path.DirectorySeparatorChar));
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! });
    }
    /// <summary>先验证全部载荷，再逐文件原子替换；失败时按逆序还原已改动文件。</summary>
    public static void Apply(UpdateApplyPlan plan, string rollbackRoot)
    {
        var root = Path.GetFullPath(plan.Installation); var payload = Path.GetFullPath(plan.Payload); rollbackRoot = Path.GetFullPath(rollbackRoot);
        if (UpdatePaths.Contains(plan.DataRoot, root)) throw new InvalidDataException("Installation cannot be the data directory");
        UpdatePaths.RejectLinks(root); UpdatePaths.RejectLinks(payload); UpdatePaths.RejectLinks(rollbackRoot);
        if (!Directory.Exists(root) || UpdatePaths.Contains(payload, root) || UpdatePaths.Contains(rollbackRoot, root) || UpdatePaths.Contains(payload, rollbackRoot)) throw new InvalidDataException("Invalid update directories");
        ReleaseUpdateService.ValidateRelative(plan.Executable);
        if (plan.Manifest.Schema != 1 || plan.Manifest.Rid != ReleaseUpdateService.PlatformRid || !plan.Manifest.Files.ContainsKey(plan.Executable)
            || plan.Executable != ReleaseUpdateService.ExecutableFor(plan.Product, plan.Manifest.Rid)) throw new InvalidDataException("Wrong update target");
        var files = new List<(string Source, string Target, string Backup)>();
        foreach (var (relative, digest) in plan.Manifest.Files)
        {
            ReleaseUpdateService.ValidateRelative(relative);
            var source = Path.Combine(payload, relative.Replace('/', Path.DirectorySeparatorChar)); var target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (UpdatePaths.Contains(plan.DataRoot, target) || UpdatePaths.Contains(plan.BackupRoot, target)) throw new InvalidDataException("Update overlaps user data");
            UpdatePaths.RejectLinks(source); UpdatePaths.RejectLinks(target);
            using var stream = File.OpenRead(source); if (digest.Length != 64 || !digest.All(Uri.IsHexDigit) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(digest))) throw new InvalidDataException("Staged update was modified");
            files.Add((source, target, Path.Combine(rollbackRoot, relative.Replace('/', Path.DirectorySeparatorChar))));
        }
        Directory.CreateDirectory(rollbackRoot);
        using var updateLock = new FileStream(Path.Combine(rollbackRoot, "apply.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var changed = new List<(string Target, string Backup, bool HadOriginal)>();
        try
        {
            foreach (var file in files.OrderBy(f => f.Target.EndsWith(plan.Executable.Replace('/', Path.DirectorySeparatorChar)) ? 1 : 0))
            {
                var hadOriginal = File.Exists(file.Target); Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                if (hadOriginal) { Directory.CreateDirectory(Path.GetDirectoryName(file.Backup)!); File.Copy(file.Target, file.Backup, false); }
                changed.Add((file.Target, file.Backup, hadOriginal));
                // 同目录临时文件 + 原子替换，避免中断留下半个程序集。
                var temporary = file.Target + ".update-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(file.Source, temporary, false);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(file.Source));
                    File.Move(temporary, file.Target, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        catch
        {
            foreach (var change in changed.AsEnumerable().Reverse())
            {
                if (change.HadOriginal)
                {
                    File.Copy(change.Backup, change.Target, true);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(change.Target, File.GetUnixFileMode(change.Backup));
                }
                else if (File.Exists(change.Target)) File.Delete(change.Target);
            }
            throw;
        }
    }
}
public sealed class UpdateApplyPlan
{
    public ReleaseProduct Product { get; set; }
    public string Installation { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Executable { get; set; } = "";
    public string DataRoot { get; set; } = "";
    public string BackupRoot { get; set; } = "";
    public int ParentId { get; set; }
    public DateTime ParentStartedUtc { get; set; }
    public UpdateManifest Manifest { get; set; } = new();
}
