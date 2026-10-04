# 项目维护要求

- 使用单一 `NonetMusicPlayer` 工程目录开发、构建和发布；本地测试版本放在 `publish/desktop` 与 `publish/cli`。不再维护两个主工程目录。
- GitHub 暂仅提交桌面、CLI、共享核心、SDK、必要测试和插件模板；移动端仅本地保留。禁止上传使用数据、凭据、缓存、旧开发历史和本地归档。
- 正式发布前检查暂存区文件范围并扫描敏感内容；纯净 ZIP 从全新暂存构建，作为 Release 资产上传，不提交进源码 Git。更新测试版时保留数据及旧包备份。
- 保留用户已有修改，尤其是未提交的 XAML 布局；新增或改写的代码使用中文说明性注释。
- 插件生态的开发基础工程为 `../NonetMusicPlayerPlugin`，公开仓库内的副本为 `plugin-template/NonetMusicPlayerPlugin`。新插件必须以该工程的 `plugin/` 模板与打包器为基础；大型测试插件可以放在播放器的 samples 中，但基础工程本身不能附带 samples。
- Contract、原生部件、配置或生命周期发生变化时，同步 `src/NonetMusicPlayer.PluginSdk`、插件参考文档及基础工程的 vendored SDK；执行 `scripts/sync-plugin-foundation.ps1 -Update`，再执行其检查模式和基础工程测试。
- SDK 是无音频、无图形、无外部 NuGet 依赖的共享验证源；播放器与基础工程必须使用相同验证器。不得复制出另一套逐渐分叉的包验证规则。
- 基础工程必须可在没有播放器源码的机器上独立构建，README 和新手教程给出从修改插件到生成并导入 `.impp` 的完整步骤。
- 跨工作区更新基础工程需要遵守当前文件写入权限；Git 仅本地管理，未经用户要求不创建远程仓库、不上传、不推送。
- 本地构建使用 `NUGET_PACKAGES=<本工程>/.packages` 和 NuGet.Config，不手工设置缺少结尾分隔符的 NuGetPackageRoot。
