# 桌面软件更新

适用于 Nonet 0.4.0-beta.1。更新源为 [hhwfsl/NonetMusicPlayer](https://github.com/hhwfsl/NonetMusicPlayer)。只有 GitHub Release 中符合下述契约的桌面资产才会触发应用更新；上传源码不等同于发布更新。

## 使用

启动时后台检查公开 GitHub Releases。没有仓库、没有桌面 Release 或已经是最新版时，不显示 New。离线自动检查不会弹出阻断窗口；设置最下面“关于 → 版本”可手动检查，“仓库”链接可直接打开 GitHub。

发现新版本后，标题栏版本号旁显示 New。点击查看 Release 更新日志，选择取消或下载。下载进度使用应用内提示条，完成提示自动关闭。准备完成后，选择“重启并更新”；暂不重启可以关闭更新日志页，之后再次点击 New 完成更新。

必须使用可写安装目录；只读目录需要迁移至可写目录或重新运行安装程序。安装版默认使用当前用户的可写程序目录。不要在数据迁移期间重启更新。

## Release 资产约定

发布脚本在 `artifacts/releases/update-packages/<版本>` 生成干净更新 ZIP，构建时尚未复制便携用户数据。资产名称：

```text
NonetMusicPlayer.Desktop-0.4.0-beta.1-win-x64.zip
NonetMusicPlayer.Desktop-0.4.0-beta.1-osx-x64.zip
NonetMusicPlayer.Desktop-0.4.0-beta.1-linux-x64.zip
```

Release 标签可为 `desktop-v<版本>` 或 `v<版本>`。程序按语义版本比较预发布编号，过滤 draft、非桌面资产及不匹配的系统/进程架构。Android 和 CLI 的 Release 不会触发桌面更新。

`scripts/publish-desktop.ps1 -CleanOnly -SkipInstaller` 将从全新 artifacts 暂存目录生成 ZIP，输出到 `publish/desktop/archives`，不读取或覆盖正在使用的发布目录。macOS ZIP 解压到 `Nonet.app` 内；可执行文件路径为 `Contents/MacOS/Nonet`，Windows 为 `Nonet.exe`，Linux 为 `Nonet`。

ZIP 根目录包含 `NonetMusicPlayer.update.json`，声明 schema、version、rid 和每个程序文件的 SHA-256；macOS 的文件从 `.app` 内的 Contents 起组织。下载还要求 GitHub 资产提供 `sha256:` digest。[GitHub Release 资产 API](https://docs.github.com/en/rest/releases/assets?apiVersion=2022-11-28)定义了 digest 和下载链接字段。

只接受预设仓库的 HTTPS 资产，校验大小、整体摘要和逐文件摘要；拒绝未列出的文件、路径穿越、符号链接、重复路径和用户数据目录。摘要防止损坏和未经预期的文件改变，但不是独立的发布者签名；仓库账户安全仍很重要。

## 重启安装

下载和解压只在当前数据目录的 Updates 下进行。重启前保存状态，启动旧版程序的独立助手副本并退出主程序。助手等待原进程结束，先验证暂存内容，再逐文件备份并原子替换程序文件；安装失败时恢复已替换的文件，再启动原程序。

Data、引导配置、自定义数据/备份目录、原始音乐和插件不进入更新清单，也不被替换。回滚和结果记录保存在该次 Updates 暂存目录。更新不删除未知旧文件；避免把用户文件误判为旧程序垃圾。更新包和旧程序备份会占用额外磁盘空间。

本版已通过离线模拟的 Release 查询、下载进度、整体/逐文件校验、程序替换、中途失败回滚和拒绝非法包。Windows 实际发布版助手也通过隔离替身的跨进程测试：等待旧 PID 退出、替换程序、重新启动且不改变 Data。真实 GitHub Release 尚不存在，线上发布到安装的完整链路需要在仓库创建并发布资产后再验收。macOS/Linux 的跨进程安装与签名策略尚需对应系统实机验收。
