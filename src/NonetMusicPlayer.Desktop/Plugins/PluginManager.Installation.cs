using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Plugins;

public sealed partial class PluginManager
{
    private (PluginManifest Manifest, string Stage) PrepareInstall(string package)
    {
        RequireWritable();
        PluginPathPolicy.RejectLinkedAncestors(_storage.PluginsFolder);
        var manifest = Inspect(package);
        var stage = Path.Combine(_storage.PluginsFolder, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try { ZipFile.ExtractToDirectory(package, stage); return (manifest, stage); }
        catch { RemoveInstallStage(stage); throw; }
    }

    /// <summary>以 ID 识别更新；验证、停止旧实例、替换文件、保存索引依次完成，失败恢复旧包。</summary>
    private PluginManifest CommitInstall(PluginManifest manifest, string stage)
    {
        RequireWritable(); manifest.Validate();
        var target = Path.Combine(_storage.PluginsFolder, manifest.Id);
        PluginPathPolicy.RejectLinkedAncestors(target);
        var previous = Installed.SingleOrDefault(p => p.Id == manifest.Id);
        if (previous is null)
        {
            if (Directory.Exists(target)) throw new InvalidOperationException(L10n.T("Plugins.UnregisteredDirectory"));
            Directory.Move(stage, target);
            try { Installed.Add(manifest); Save(); }
            catch { Installed.Remove(manifest); Directory.Delete(target, true); throw; }
        }
        else
        {
            if (PluginUpdatePolicy.Evaluate(previous, manifest) != PluginInstallKind.Upgrade)
                throw new InvalidOperationException(L10n.T("Plugins.UpdateVersionNotNewer"));
            // 本地新包更新远程安装的插件时，不丢失原远程更新来源。
            if (manifest.OriginRepository.Length == 0) manifest.OriginRepository = previous.OriginRepository;
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException(L10n.T("Plugins.UnregisteredDirectory"));
            var effective = EffectiveConfiguration(previous);
            var schema = ReadStagedSchema(stage);
            var values = PluginConfigSchema.Resolve(schema, effective);
            PluginConfigSchema.Validate(schema, values);
            // 先用旧配置验证新页面，不能先覆盖文件，再发现用户设置已不兼容。
            if (manifest.Type is "ui" or "lyrics")
            {
                var node = JsonNode.Parse(File.ReadAllText(Path.Combine(stage, manifest.PageEntry)))!;
                using var resolved = new MemoryStream(Encoding.UTF8.GetBytes(PluginConfigSchema.Substitute(node, values).ToJsonString()));
                PluginPageContract.Read(resolved, manifest.Permissions);
            }
            using var configuration = JsonDocument.Parse(effective);
            var persisted = Scrub(configuration.RootElement).AsObject();
            PluginConfigSchema.RemoveSensitiveFields(schema, persisted);
            manifest.Configuration = persisted.ToJsonString();
            var samePermissions = previous.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(manifest.Permissions);
            var wasEnabled = previous.Enabled;
            manifest.Enabled = wasEnabled && samePermissions;
            manifest.AudioTagWriteConsent = samePermissions && previous.AudioTagWriteConsent && manifest.Permissions.Contains("audio-tags");
            var priorSession = _sessionConfiguration.GetValueOrDefault(manifest.Id);
            var index = Installed.IndexOf(previous);
            var rollback = Path.Combine(_storage.PluginsFolder, ".rollback-" + Guid.NewGuid().ToString("N"));
            var movedOld = false; var movedNew = false;
            try
            {
                // 在覆盖可执行文件前调用可选停用回调，并等待旧进程实际退出。
                previous.Enabled = false; PublishLifecycle(previous, "disabled");
                if (_clients.Remove(previous.Id, out var client)) { client.NotifyLifecycle(previous, "lifecycle.disable"); client.Dispose(); }
                NotifyUiUnavailable(previous.Id);
                if (_pets.Remove(previous.Id, out var pet)) pet.Close();
                PluginPathPolicy.AfterProcessExit(() => Directory.Move(target, rollback)); movedOld = true;
                Directory.Move(stage, target); movedNew = true;
                Installed[index] = manifest; _sessionConfiguration[manifest.Id] = effective;
                Save();
            }
            catch
            {
                Installed[index] = previous;
                if (priorSession is null) _sessionConfiguration.Remove(manifest.Id); else _sessionConfiguration[manifest.Id] = priorSession;
                if (movedNew) Directory.Delete(target, true);
                if (movedOld) Directory.Move(rollback, target);
                previous.Enabled = wasEnabled;
                try { Save(); } catch (Exception error) { AppLog.Warning("Plugins", "旧插件文件已恢复，但索引恢复写入失败", error); }
                RefreshUiRuntime(previous); PublishLifecycle(previous, wasEnabled ? "enabled" : "disabled");
                throw;
            }
            // 临时旧包只服务事务回滚；成功后删除，不长期保留旧插件文件。
            try { PluginPathPolicy.AfterProcessExit(() => Directory.Delete(rollback, true)); }
            catch (IOException error) { AppLog.Warning("Plugins", "更新已完成，但临时旧包清理失败", error); }
            RefreshUiRuntime(manifest);
            PublishLifecycle(manifest, manifest.Enabled ? "enabled" : "disabled");
        }
        OperationCompleted?.Invoke(this, CommandResults.Completed("plugins.install", new JsonObject { ["id"] = manifest.Id, ["version"] = manifest.Version, ["updated"] = previous is not null }));
        return manifest;
    }

    private static JsonObject ReadStagedSchema(string stage)
    {
        var path = Path.Combine(stage, PluginConfigSchema.FileName);
        if (!File.Exists(path)) path = Path.Combine(stage, "plugin_config_schema");
        if (!File.Exists(path)) return new JsonObject();
        using var input = File.OpenRead(path); return PluginConfigSchema.Read(input);
    }

    private static void RemoveInstallStage(string stage)
    {
        // 只删除本次随机创建、经过无链接校验的暂存目录，不使用清单或用户配置指定的路径。
        if (!Directory.Exists(stage)) return;
        PluginPathPolicy.RejectLinkedAncestors(stage); Directory.Delete(stage, true);
    }
}
