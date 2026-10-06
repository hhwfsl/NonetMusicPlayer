# Nonet 0.4.0-beta.6 Agent 与主题色验证

日期：2026-10-06。桌面交互在 Windows / Avalonia Headless 验证；macOS/Linux 桌面只构建发行包，不代表对应系统实际交互通过。

- 歌词页原文/译文高亮在 Light、Dark 下等于 Accent 原始 RGB，底色不变；关于仓库显示 GitHub。
- Agent 页面实际宿主命令更新 VM，并进入桌面终端结果流；禁用后不能执行动作；禁止安装新插件。
- 工具白名单、选项结束符、防伪确认、JSON 参数与现有 ID 校验；路径/凭据结果剔除、源生成 DTO、有界消息。
- 冻结旧 Contract v1 主题/卡片/音源/UI 包、同 ID 更新、失败回滚、配置保存与权限变更回归通过。
- 共享命令 / TUI、旧平台包裁剪、仓库来源与数字版本规则通过；模板直接引用 SDK 及文档同步通过。
- 私人 Agent 的代码及生成包不在主项目；模拟接口验收与原生包验收在其独立项目执行，不用真实模型或用户 API Key。
- 三个平台 ZIP 通过逐文件 SHA-256、清单及 POSIX 可执行权限校验；原 Windows 361 个 Data/引导文件校验一致，旧程序可从 artifacts 恢复。
- 真实发行 ZIP 启动、发布目录更新助手重启、安装器隔离安装/卸载通过；安装器不包含 Data，卸载保留测试数据。不操作用户源音频。

复现：

    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/agent-checks --agent
    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/compatibility --compatibility
    dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release -- artifacts/core-checks
    pwsh -File scripts/verify-clean-desktop.ps1 -Version 0.4.0-beta.6

# Nonet 0.4.0-beta.5 更新与平台插件验证

日期：2026-10-05。仅 Windows 环境执行；macOS/Linux 跨平台编译不代表实机交互通过。

- 主解决方案、共享核心 / 命令 / TUI、当前桌面状态连续性、冻结旧 Contract v1、插件基础工程检查通过。
- 离线 HTTP 夹具覆盖本机 Release 资产筛选、启动后台检查、New 标记即时更新、确认升级、配置及安装来源保留、下载数据清理，不向私人插件仓库上传内容。
- 更新弹窗的发行日志只读且无 tooltip；下载按钮立即关闭弹窗，进度条右侧取消可终止下载并清理暂存。更新助手重启夹具包含下载 ZIP 删除断言。
- 插件卸载但保留文件时留在原目录，重新导入相同版本恢复注册和配置；旧多平台包只提取本机文件，冻结旧 SDK Contract 与包格式继续兼容。
- 原图封面检查覆盖 1024 像素图像导入、裁剪、详情页解码及控件离开页面后的图像释放；已有被缩小保存的封面需要重新选择原图。
- 发布脚本从新暂存构建三个平台 ZIP，使用逐文件 SHA-256、清单、禁止用户数据 / 引导配置 / 调试文件和 POSIX 执行权限检查。Windows 实际程序启动、CLI 与更新助手分别使用隔离数据夹具，不操作用户源音频。
- 原 Windows 目录替换前后核对 Data 和引导配置逐文件 SHA-256，旧程序仅保存在本地 artifacts。GitHub 仅提交主项目公开树，不包含具体插件、模板、移动端、用户数据或旧私有历史。

复现命令：

```powershell
$env:NUGET_PACKAGES = "$PWD/.packages"
dotnet build NonetMusicPlayer.slnx -c Release
dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release --no-build
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release --no-build -- artifacts/plugin-beta5 --update-layout
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release --no-build -- artifacts/plugin-compatibility --compatibility
pwsh -File scripts/verify-clean-desktop.ps1 -Version 0.4.0-beta.5
pwsh -File tests/verify-update-restart.ps1 -PublishedDirectory publish/desktop/windows
pwsh -File tests/verify-published-startup.ps1 -Zip publish/desktop/archives/NonetMusicPlayer.Desktop-0.4.0-beta.5-win-x64.zip -CliDirectory publish/cli/windows
```

# Nonet 0.4.0-beta.4 插件开发关系与更新验证

日期：2026-10-05。测试运行于 Windows，Surface 实机及 macOS/Linux 实机尚未验证。

- 主解决方案编译通过，0 警告 / 0 错误。模板和 LDDC 独立工程直接引用主项目 SDK/打包器；模板最小包与 27 项歌词离线检查通过，不再生成源码 ZIP。
- 共享命令 / TUI、当前桌面 UI、完整通用插件和冻结旧 Contract v1 回归通过；旧的主题、卡片、音源与页面包缺少新仓库属性仍可用。
- 以 1440×960、1080×720、900×600 和 640×480 逻辑窗口尺寸检查左右标题栏与鼠标/触摸模式，圆点按钮固定 22 DIP 槽宽，总宽不超过 66 DIP。不代表已在 Surface 真机测试。
- 离线 HTTP 模拟验证 GitHub Release 元数据、下载进度、SHA-256、实际包版本优先于 tag、来源防伪、仓库不存在提示；未创建或上传私人插件仓库。
- 更新按钮处于配置与删除之间且垂直居中；卸载确认的删除文件开关位于同一行右侧。
- 验证本地新包保留远程安装来源和配置、重启恢复来源；降级 / 相同版本 / 不兼容身份拒绝，文件事务失败回滚，权限改变停用。
- LDDC 界面、编辑预览、关联、嵌入确认、生命周期和同 ID 更新专用宿主检查通过。以原 Windows 发布目录内的精简 Nonet.exe 验证旧 1.0.1 / 新 1.0.2 插件，实际歌词 RPC、默认逐行、文本清理、关联、写入确认、内嵌读回、备份与停用/卸载生命周期全部通过。只使用隔离数据和生成音频。
- 原 Windows 发布目录升级至 beta.4，使用数据与引导配置的校验摘要保持不变；旧应用在 artifacts 内可恢复。不在 Git 中记录用户的歌曲、配置或统计数据。
- 三个平台纯净 ZIP 通过清单/逐文件 SHA-256 / 完整许可 / POSIX 执行权限检查，必要文件数分别为 Windows 12、macOS 15、Linux 12；不含 Data、插件、调试文件或引导配置。
- 排除模板与私人插件的独立公开源码快照构建成功，0 警告 / 0 错误，其 Core / 命令 / TUI 及全部通用插件检查通过。远程旧历史已有 samples，本次只从当前仓库树移除，不擅自改写已发布历史。

纯净 Release ZIP 从全新暂存构建；GitHub 源码快照排除具体插件、插件模板、移动端、使用数据、缓存及本地未发布历史。只沿远程既有公开历史追加，不推送含私有 samples 的旧本地提交。

# Nonet 0.4.0-beta.3 插件兼容与歌词编辑验证

日期：2026-10-05。仅 Windows 实机环境；未做 macOS / Linux 实机交互验证。

- 完整桌面/CLI 解决方案构建成功，0 警告 / 0 错误；当前桌面 UI、全部通用插件及独立基础工程检查通过。
- 冻结旧版 Contract v1 最小包，验证主题、卡片、音源及 UI 页面不因宿主版本升级失效。SDK 维持 3.1.0；清单及页面版本维持 1。
- 同 ID 更新覆盖配置和启用状态保留、旧页面释放、成功删除临时旧包、相同版本/降级/不兼容配置拒绝、索引写入失败回滚、权限变化停用、重启读取。
- LDDC 专用集成检查通过：预览可编辑且无 tooltip，下载与普通关联使用编辑后的 Unicode 文本，内嵌写入也使用该文本，空编辑区不能误写原始结果；1.0.0 → 1.0.1 更新仍只有同一插件身份，保留原配置和授权。
- 27 项离线歌词/解密/匹配/规范/授权/文件写入检查及实际新版 impp 搜索与匹配检查通过。
- 原 `publish/desktop/windows` 原地更新至 beta.3。346 个用户数据与引导文件在更新及测试前后 SHA-256 完全一致；旧应用副本保留在 `artifacts/releases/previous-20261005-032903/windows`。
- 以该正式目录内的精简 Nonet.exe 分别验证 LDDC 1.0.0 和 1.0.1，安装、实际歌词 RPC、清理、关联、授权、内嵌读取、原音频备份与生命周期全部通过。只使用插件项目的隔离数据及生成音频，不修改已有安装或用户源音频。
- 插件 dist 仅保留 1.0.1 的 impp 和独立源码 ZIP；不分发旧版本，也不生成额外测试客户端。

# Nonet 0.4.0-beta.2 Windows 发布目录验证

日期：2026-10-05。仅验证 Windows 正式便携目录，不代表 macOS / Linux 实机通过。

- 将 `publish/desktop/windows` 从 0.4.0-beta.1 原地更新为 0.4.0-beta.2；构建成功，未创建独立测试版目录。
- 更新前后对 Data 及两种品牌的引导配置逐文件计算 SHA-256，共 335 个文件，内容完全一致。旧程序及数据的完整副本保留在 `artifacts/releases/previous-20261005-024205/windows`。
- 以正式目录内的 `Nonet.exe --verify-lyrics-plugin` 验证独立插件目录中的 LDDC 1.0.0 impp：安装、歌词页面 Contract、实际搜索 RPC、默认逐行、指定文本清理、歌词关联、嵌入授权、内嵌读取、原音频备份及停用/卸载生命周期全部通过。
- 验证仅使用插件项目 artifacts 下的隔离数据和生成的 MP3，不安装到现有用户数据，也不修改用户源音频。
- 新构建的 Windows 更新 ZIP 包含 12 个必要发行文件及更新清单，不含用户数据或引导配置；逐项 SHA-256 验证通过，与原地更新后的程序文件一致。

原安装失败来自仍运行 SDK 3.0 的 0.4.0-beta.1 宿主：旧验证器只允许音源插件声明进程生命周期，且不提供 SDK 3.1 的歌词插件接口。独立 impp 不依赖 samples 路径；迁移插件源码不是失败原因。

# Nonet 0.4.0-beta.1 验证范围

日期：2026-10-04。仅 Windows 环境执行，不代表 macOS / Linux 实机通过。源码由 AI 完成，尚无人工代码 review。

## 已执行

- 原工程与不包含移动端、旧 Git 历史或开发缓存的独立源码快照均可构建，0 警告 / 0 错误。
- Core / 命令 / TUI 检查：公开语法、Unicode、确认、资源 ID、宿主差异、命令历史、日志、随机算法、独立数据及全部键盘操作。
- 桌面全套 Headless 回归：布局与尺寸、多语言、主题、设置、选择、虚拟化、歌词、播放状态、持久化、恢复、插件导入和包安全。
- Nonet 专项：当前句译文、无译文不展示下一句、悬停不定位、指针时间预览、回顶按钮位置与行为、侧栏收起状态保存。
- 插件页面生命周期：关闭 / 重新开启后释放并重建，精准歌词选择歌曲、空格标记、播放锁、保存 / 取消 / 关闭页面 / 停用释放。
- 独立 GPL 插件模板：四个必要分发文件、生成 impp、配置、覆盖保护、非法路径与过大许可文件拒绝、与主工程相同 SDK 摘要。
- Windows 原生窗口：桌面歌词独立所有权、置顶恢复、锁定与点击穿透、四边调整、窗口移动边界、激活与最小化的滚动连续性、图标与托盘。
- Windows 实际音频（静音夹具）：增量流解码、WAV / FLAC / MP3、44.1 / 48 / 96 kHz、单/双声道、时钟、定位、设备重绑定、自然切歌及重复播放、损坏流恢复。
- 更新离线模拟：平台资产筛选、下载进度、摘要、清单、数据保护、安装与失败回滚。
- 三平台 ZIP：从全新暂存构建、逐文件 SHA-256、清单版本与平台、禁止用户数据及调试文件、完整 GPL 许可、Linux / macOS 可执行权限。
- Windows ZIP 实际启动：在独立新目录解压并启动，完成布局初始化与窗口显示；发行版 CLI 独立执行共享命令，不包含图形依赖。
- Windows 发行版更新助手：等待旧进程退出、替换程序、重启测试进程，原有数据保持不变。

## 发布组织

纯净 ZIP 为 `NonetMusicPlayer.Desktop-0.4.0-beta.1-<rid>.zip`。Windows / Linux 的程序在 ZIP 根目录，macOS 的程序为 `Contents/MacOS/Nonet`，需解压到 `Nonet.app/` 内。三个包均含 `NonetMusicPlayer.update.json`。

公开源码仅包含桌面、CLI、共享核心 / SDK 和插件模板，不包含使用数据或旧代码历史。原本的发布文件夹不参与取包，不被替换。

## 未完成的平台验证

- macOS / Linux 交互、音频设备、文件关联、更新助手和原生窗口尚待实机验证。
- 没有进行代码签名或 macOS 公证。
- GitHub 在线 Release 下载到原地更新，需发布符合契约的 Release 资产后继续验证；仅上传源码不会产生在线更新。

复现命令见 README；ZIP 校验使用 `pwsh -File scripts/verify-clean-desktop.ps1`。
