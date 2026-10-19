# Nonet 0.4.0-beta.31 验证

日期：2026-10-19。Windows 原生与 Android 15 模拟器验证；Linux/macOS 为交叉构建和包检查，未进行对应系统实机验证。公开源码及 Release 仅包含桌面、CLI、共享核心与必要工具，不包含移动端、私人插件、模板或用户数据。

- 最终解决方案构建通过，0 警告、0 错误；核心命令、TUI、实际 Avalonia 设备页面、冻结旧插件 Contract 及模板直接引用检查通过。
- HTTPS 回归验证一次超时不结束配对、单方向连续延迟超过三十秒时反向已认证通信仍保留原会话、延迟恢复后免重新配对，以及明确撤销凭据立即断开。确实停止的服务在持续失联窗口后结束连接；分页正文仍有独立二十秒上限。
- Android JUnitCore 18/18、完整仪器回归 29/29、跨端集成测试 1/1 通过。跨端测试先保持 150 秒无播放的后台连接，中途熄屏，超过 120 秒仍持有已续租连接保护；随后 Windows 与 Media3 分别独立解码对方音频，双方主动断开均通知对端。ADB 转发仅模拟双向拓扑，不替代真实无线网络验收。
- 最终 Android R8 包沿用原签名，覆盖安装、冷启动、搜索入口聚焦并弹出输入法、实际界面截图及崩溃缓冲检查通过；lint 为 0 errors、58 warnings。荣耀 REA-AN00 / MagicOS 9 真机未连接。
- 三平台纯净桌面 ZIP 完整摘要、GPL 与 POSIX 执行权限检查通过；Windows installer 的 12 个程序文件与纯净 ZIP 一致，隔离安装/卸载通过，安装器目录仅保留 beta.31。
- 原 Windows 发布目录的 593 个数据及当前引导文件摘要不变，旧品牌引导配置亦与备份一致。实际精简 EXE 使用隔离配置夹具完成窗口初始化与保存/重新打开，实际 CLI 以隔离数据执行共享命令通过；未关闭或修改 H 盘运行的用户实例。
- 修复使用既有共享网络层和通用服务，不新增或淘汰插件 Contract。

# Nonet 0.4.0-beta.30 本地验证

日期：2026-10-19。Windows 原生与 Android 15 模拟器验证；Linux/macOS 为交叉构建和包检查，未做对应系统实机测试。本节记录发布前本地验证；公开源码与 Release 使用独立审查的桌面/CLI 快照，不包含移动端、私人插件、用户数据及旧开发历史。

- 核心 HTTPS 回归覆盖桌面/移动四种设备组合、一次审批的反向访问、固定证书、一次性反向协商、501 首分页、Unicode、HEAD/Range、撤销凭据与异常断线。
- 实际 Avalonia 主窗口验证终端 network 命令与界面共用状态、隐藏窗口的待审批请求、终端批准、过期/完成请求不能重放，以及断线保留已保存引用。冻结旧插件 Contract 兼容回归通过，既有程序集身份不变。
- 同一次跨端配对后，Windows NativeAudioPlayer 与 Android Media3 均独立解码对方的合成音频，真实播放时钟前进；双方主动断开均通知对端。模拟器的单端口 ADB 转发仅用于测试网络拓扑，不代表真实无线网络验收。
- Android 领域测试 15/15、完整仪器测试 27/27 和跨端集成测试 1/1 通过。另测后台通知审批和两次播放器采样之间的平滑逐字着色；最终 R8 包签名、覆盖安装、冷启动及崩溃缓冲检查通过。荣耀真机未连接。
- 三平台纯净桌面 ZIP 的逐文件 SHA-256、GPL、可执行权限及无用户数据检查通过。Windows installer 隔离安装的 12 个程序文件与纯净包一致，隔离卸载通过；installer 目录仅保留当前成功版本。
- 原 Windows 发布程序完成独立配置夹具的原生窗口初始化；再次验证前后 592 个现有 Data 文件哈希不变。未关闭或改写其他路径中运行的用户实例。
- 核心、桌面歌词、更新事务和 SDK/模板直接引用检查通过；双向状态、审批时长及设备类型通过既有通用扩展服务的可选字段提供。

# Nonet 0.4.0-beta.29 本地验证

日期：2026-10-19。Windows 隔离回归与实际安装器验证；Linux/macOS 为交叉构建和包检查，未做对应系统实机测试。本次未上传 GitHub 或 Release。

- 核心命令、TUI、冻结 Contract v1 插件兼容回归通过。旧 KaraokeLine 五参数方法、LanCatalog 六参数构造/解构和服务端构造签名保留，新增信息为可选字段。
- HTTPS 核心夹具验证待确认连接不完成、拒绝不发放凭据、重新申请需重新批准，以及 501 首分页、Unicode、Range/寻址与断线；真实 Avalonia 主窗口接受/拒绝弹窗和侧栏远端歌单的来源标记/移除通过。
- 桌面歌词逐字位置跟随、逐行阅读位置单向推进、句尾停留、暂停不动及主动回退复位通过；歌词视口和字号保持固定。
- 离线更新模拟验证中途失败还原、摘要校验与数据排除；成功暂存清理、活动助手锁保护和未完成下载保留通过。
- 三平台纯净桌面 ZIP 的完整 SHA-256、GPL、可执行权限及无数据/退役解码器检查通过。Windows installer 隔离 QA 安装、12 个程序文件哈希及卸载通过，目录仅保留当前成功版本。
- 原 Windows 发布目录的 590 个数据文件哈希保持不变；没有替换另一路径正在运行的 H 盘实例。单实例限制下常规启动会转发到该实例，因此未将它计为新版冷启动成功。
- 实际精简 Nonet.exe 使用独立最小配置夹具完成窗口初始化、反复展开/收起、保存与重新打开的原生界面自检，不发送模型请求、不触及运行中的用户实例。

# Nonet 0.4.0-beta.28 验证

日期：2026-10-19。Windows 隔离与实际精简发行程序验证；Linux/macOS 为交叉构建和包检查。

- 完整解决方案构建通过，0 警告、0 错误；共享 Core / 终端 / TUI 及冻结 Contract v2 独立进程/托管插件回归通过。
- 设备互听紧接听歌统计，位于“我的音乐”分组内；设备页、配对、独立播放、断线与导航保持回归通过。沿用已有导航路由及通用接口，不变更插件 SDK。
- 三平台纯净桌面 ZIP 清单、逐文件 SHA-256、GPL 许可、POSIX 权限和退役解码器检查通过；三平台 CLI ZIP 逐文件摘要和无 GUI 依赖检查通过。
- Windows installer 与本次纯净 ZIP 的 12 个程序文件一致，隔离安装/卸载及旧文件迁移通过；精简 ZIP 的实际 EXE 和 CLI 首次启动通过。
- 原 Windows 发布目录更新后，592 个数据/引导文件 SHA-256 未变；旧程序本地备份，installer 目录仅保留 beta.28，旧安装器本地归档。
- 公开源码和附件不含移动端、私人插件、模板、使用数据、凭据或旧本地历史。

# Nonet 0.4.0-beta.27 验证

日期：2026-10-18。Windows 隔离数据回归；Linux/macOS 为交叉构建和发行包检查，不代表对应系统实机验证。

- 桌面、CLI、SDK 与必要测试的完整解决方案构建通过，0 警告、0 错误。
- Core / 终端 / TUI、插件版本与平台规则、冻结 Contract v1/v2（独立进程和托管）兼容、通用服务及歌词转场回归通过。
- 局域网协议验证固定证书 HTTPS、私网邀请、501 首分页、Unicode、HEAD/Range/回跳、目录刷新、隐私限制和心跳断线；桌面设备页验证共享启停、远端播放引用、断线保留收藏与导航不跳转。
- 三平台桌面 ZIP 从全新暂存目录构建，不读取现有便携版数据；逐文件 SHA-256、GPL 许可、POSIX 执行权限及已移除视频解码器的清理检查通过。
- Windows 安装器与本次纯净 ZIP 的 12 个程序文件哈希一致；隔离安装、旧背景解码器迁移与卸载保留非程序数据通过。安装器目录保留最新成功版本，旧安装器仅本地归档。
- 实际精简 ZIP 中的 Nonet.exe 使用全新隔离数据完成首次启动、显示窗口并保持存活，未记录启动错误；CLI 实际发行程序执行共享命令通过且不含 GUI 库。
- 公开源码使用经审查的桌面/CLI 快照，不包含移动端、私人插件、模板、用户数据、凭据或旧本地 Git 历史。
- Android 移动端仍单独本地维护，本次 GitHub Release 不分发 APK。跨端真实音频互听的既有隔离验证不替代不同手机厂商与网络环境的实机验收。

# Nonet 0.4.0-beta.24 验证

日期：2026-10-18。桌面改动使用隔离数据验证；不关闭或替换其他路径中正在运行的副本。

- 歌词展开/收回、顶部与底部播放栏、连续快速切换、最小化取消及减少动画模式通过；临时快照限制为 200 万像素并在结束时释放。
- 字体、背景的相对路径显示及移除项目目录设置通过；生成服务保持原协议，使用宿主管理目录。
- 核心、控件级布局及冻结插件回归通过；保留已发布 SDK 程序集身份和 Contract。
- 发布使用不含旧本地历史、移动端、私人插件、模板及用户数据的桌面/CLI 源码快照。Windows 执行验证；macOS/Linux 构建及包检查，不声称实机通过。

# Nonet 0.4.0-beta.23 验证

日期：2026-10-18。Windows 实际发行程序验证；Linux/macOS 完成跨平台构建及包检查，未进行本机图形测试。

- 完整解决方案构建通过，0 警告、0 错误；共享核心、既有插件 UI、冻结 Contract v1/v2、通用服务、故障停用及云端搜索通过。
- 导航栏和播放栏位置下拉框生效；旧用户布局 JSON 留存但启动忽略，不再提供相关文件操作入口。
- 控件目录、属性覆盖、网格行列、父级/顺序重排、循环拒绝、冲突回滚、原绑定恢复、命名及运行时锚点插入/撤销通过；停用插件释放自定义控件和布局。
- 图标源图与提供的 PNG SHA-256 一致；Windows EXE 的全部 7 个图标分辨率与新 ICO 一致。旧活动 SVG 已移除。
- 原 Windows 发布目录 590 个数据和引导文件整体 SHA-256 在更新前后保持一致；旧程序保留本地备份。
- 三平台纯净 ZIP 的清单、SHA-256、内容白名单和执行权限检查通过；不含用户数据、私人插件或 SDK 开发工具。
- 原发布目录的精简程序验证冻结托管/进程插件，以及配置的重复展开、保存重开、放弃更改；测试隔离数据，不访问模型。
- 插件模板继续直接引用 SDK，文档同步和最小 impp 打包测试通过；SDK AssemblyVersion 保持 4.0.0.0。
- 页面短淡入与播放按钮反馈不改变排版尺寸；减少动画环境开关测试通过。
- Windows beta.23 安装器补全，输出目录仅保留最新文件，3 份旧安装器转入开发归档；版本与便携程序一致。发布跳过防护、版本错误拒绝和原安装器保护通过。本次检测到播放器正在使用，未自动执行安装、卸载或关闭应用。

# Nonet 0.4.0-beta.22 本地验证

日期：2026-10-12。以下为本次已执行的源码回归；正式发布另使用纯净桌面/CLI 源码快照构建，验证实际 Windows 发行程序。macOS/Linux 只进行跨平台构建及包结构检查，未进行本机图形测试。

- 托管初始化、动作、事件、受监督后台任务及 Contract v1 RPC 故障均停用对应插件；故障状态持久化，业务拒绝保持可用。
- 日志记录包含插件名和稳定 ID；导入及首次启用提示致命错误仍可能使宿主退出。
- 原生网易云 YRC、独立翻译轨道、时间戳微差及旧双语 LRC 解析通过；既有逐字呈现、内嵌与手动优先级通过。
- Core、冻结 Contract v2 进程/托管、通用服务和音乐搜索通过；6,000 条结果只实现约 7 行可见控件。
- 模板直接引用主 SDK；新增可选接口不改变 SDK AssemblyVersion 4.0.0.0，独立进程和 AOT 继续支持。
- 发布快照不包含移动端、模板、私人插件、用户数据、凭据、开发缓存或本地主工程旧 Git 历史。

# Nonet 0.4.0-beta.21 本地发布验证

日期：2026-10-12。Windows 实际宿主验证。下文记录本地验证；公开源码和 Release 使用另行审查的桌面/CLI 快照及全新纯净构建，不包含本地开发历史、移动端、私人插件、模板或用户数据。

- 主工程构建通过，0 警告、0 错误；Core、既有 UI、Contract v1、冻结 v2 进程/托管和通用扩展回归通过。
- 搜索首次请求完成前已进入结果页并显示加载动画；底部滚动自动追加 30 首，6,000 条元数据只实现 7 个可见行控件，旧行身份保持不变。
- 按需封面、离页迟到响应丢弃及搜索输入清空通过；真实公开音源搜索两页、封面和 Range 请求通过。
- 真实原生音频后端播放 96 kHz/24 位 PCM；24 秒期间注入 5 次 1.2 秒网络等待，时钟连续、无错误设备恢复，PCM 容量 3,072,000 字节；暂停、寻址和自然结束通过。
- 原发布目录精简 `Nonet.exe` 验证云端搜索封面、歌单稳定 ID、歌词、PCM 缓冲、实际解码/时钟/寻址、音源进程重新启用及插件配置反复展开保存。
- 原目录发布版本为 0.4.0-beta.21；528 个原数据及引导文件逐文件 SHA-256 未改变，旧程序已作本地备份。纯净 Windows ZIP 在复制用户数据前独立构建，不含运行数据。
- 本地模板直接引用 SDK 4.9，文档一致性与基础包测试通过。新接口是可选能力，原 Contract、页面 Schema 和程序集身份不变。
- 私人音源的 Windows AOT、Linux/macOS 自包含包分别校验；Linux 实际进程初始化、错误恢复和退出通过，macOS 未作实机测试。私人插件不进入播放器仓库。

复现：

```powershell
dotnet build NonetMusicPlayer.slnx -c Release
dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release -- artifacts/core
dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release -- artifacts/streaming --streaming-audio-only
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/search --music-search
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/compatibility --compatibility
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/extensions --extensions
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/universal --universal
```

# Nonet 0.4.0-beta.13 发布验证

日期：2026-10-08。Windows 环境实际执行；macOS/Linux 为交叉构建及包校验，不代表实机通过。

- 主工程构建通过，0 警告 / 0 错误；独立最小测试夹具不依赖私人插件或相邻模板。
- 新增图片资源打包、复合输入、绑定选择、独立透明原生浮层、最小化独立显示及停用清理回归通过。
- 内联审批不销毁会话，公开 / 保护配置同时保存；后台不能提升权限，宿主确认仍有效。
- 冻结旧 Contract v1 与原始 v2 进程 / 托管包、同 ID 升级、配置重载、流程、PCM、查询、界面恢复及权限回归通过。
- Core / 命令 / CLI TUI、歌词、统计与 SQLite、进度预览、持久页面、离线更新模拟回归通过。
- 原 Windows 发布目录的精简 Nonet.exe 验证 44.1 kHz 单声道时钟、自然结束、封面、源生成序列化、插件配置及冻结 v2 ABI，通过模拟接口验证实际进程扩展、模型目录及凭据重启恢复。
- 三个纯净桌面 ZIP 从全新暂存构建，版本、文件 SHA-256、许可及 POSIX 执行权限通过；Windows / Linux 各 12 个程序文件，macOS 15 个。
- Windows 安装器隔离安装的程序文件与纯净 ZIP 哈希一致，卸载保留非程序测试数据。
- 原 Windows 目录原地升级，396 个 Data / 引导文件的聚合 SHA-256 未改变；旧程序仅本地备份。
- 本地模板直接依赖 SDK 4.5，基础包与文档一致性检查通过；私人插件和模板不包含在公开源码或 Release。

复现：
```powershell
dotnet build NonetMusicPlayer.slnx -c Release
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/composer --composer
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/universal --universal
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/extensions --extensions
dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release
pwsh -File scripts/verify-clean-desktop.ps1 -Version 0.4.0-beta.13
```

# Nonet 0.4.0-beta.12 发布验证

日期：2026-10-07。仅 Windows 实际执行；macOS/Linux 为交叉构建，不代表实机通过。

- 334 个桌面、CLI、共享核心、SDK、打包器、测试及文档文件的独立公开源码快照构建通过，0 警告、0 错误；不含移动端、私人插件、模板、数据、凭据或本地开发历史。
- 快照的通用服务目录、分页查询、持久化、原生视图和数据区域替换、配置重载、流程取消/选曲、恢复入口、角色组合、禁用/重新启用及旧 Agent 权限回归通过。
- 冻结旧 Contract v1 包及原始 Contract v2 进程/托管程序集回归通过；原 Windows 发布目录的精简程序再次运行冻结 v2 包，未以重新编译旧包代替验证。
- PCM 小段处理、非法采样恢复与故障旁路通过夹具。实际音频引擎验证 44.1 kHz 单声道播放时钟、自然结束及内嵌封面；效果插件的真实声卡延迟仍需单独实测。
- Core、命令、TUI、持久化/歌词及离线软件/插件更新模拟回归通过，不调用真实模型或私人插件网络接口。
- 三个平台纯净 ZIP 从新暂存构建，版本、逐文件 SHA-256、GPL 许可、禁止数据路径与 POSIX 执行权限通过。
- Windows 安装器隔离安装后的 12 个程序文件与纯净 ZIP 哈希完全一致；隔离卸载删除程序但保留非程序数据。
- 原 Windows 目录完成原地升级，395 个原有 Data 和引导文件 SHA-256 不变；旧程序仅保存在本地 artifacts。
- 本地模板直接依赖宿主 SDK 4.4，文档一致性、基础包、配置、路径与覆盖保护检查通过。模板和私人插件不进入本次公开提交或 Release。

复现：

    dotnet build NonetMusicPlayer.slnx -c Release
    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/universal --universal
    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/compatibility --compatibility
    dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release
    dotnet run --project tests/NonetMusicPlayer.UiChecks.Desktop -c Release -- artifacts/updates --desktop-revision-only
    pwsh -File scripts/verify-clean-desktop.ps1 -Version 0.4.0-beta.12
    pwsh -File tests/verify-installer.ps1 -Installer publish/desktop/windows_installer/NonetMusicPlayer-0.4.0-beta.12-windows-x64-setup.exe -Zip publish/desktop/archives/NonetMusicPlayer.Desktop-0.4.0-beta.12-win-x64.zip

扩展保护边界、原生信任限制与默认界面流程的可拦截范围见 UNIVERSAL_EXTENSIONS.md。未订阅扩展时使用原内置路径；不把“一切可扩展”解释为无限权限或对尚未定义业务的承诺。

# Nonet 0.4.0-beta.11 发布验证

日期：2026-10-07。仅 Windows 实际执行；macOS/Linux 为交叉构建，不代表实机通过。

- 317 个桌面、CLI、共享核心、SDK、打包器、测试及文档文件的独立源码快照构建通过，0 警告、0 错误；不包含移动端、具体插件、用户数据和本地历史。
- 独立快照的 Core / 命令 / TUI、桌面更新、工作区扩展与冻结旧 Contract v1 兼容回归通过。
- 更新检测按数字语义版本选取候选，不使用 GitHub Release 列表第一项；离线模拟覆盖资产筛选、下载、SHA-256、清单、替换与拒绝非法包。
- 三个平台 ZIP 从新暂存目录构建，版本、逐文件 SHA-256、GPL 许可、禁止用户数据路径与 POSIX 执行权限全部通过。
- 实际 Windows 精简程序验证 44.1 kHz 单声道播放时钟、自然结束、内嵌封面、持久化、字体、桌面歌词/托盘配置及插件配置，不使用用户音频或凭据。
- Windows 安装器从同一纯净暂存生成；隔离安装的 12 个程序文件与 ZIP 对应文件 SHA-256 全部一致。安装后的实际程序通过播放及持久化验证，隔离卸载保留非程序测试数据。
- 本次构建和测试没有替换现有 Windows 使用目录。

复现：

    dotnet build NonetMusicPlayer.slnx -c Release
    dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release -- artifacts/core-checks
    dotnet run --project tests/NonetMusicPlayer.UiChecks.Desktop -c Release -- artifacts/updates --desktop-revision-only
    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/workspace --workspace
    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/compatibility --compatibility
    pwsh -File scripts/verify-clean-desktop.ps1 -Version 0.4.0-beta.11

# Nonet 0.4.0-beta.8 通用扩展验证

日期：2026-10-07。Windows 实际发行程序与 Avalonia Headless 验证；macOS/Linux 桌面仅构建发行包，不能替代实机验证。

- 完整主解决方案构建通过，0 警告、0 错误；旧 Contract v1 和首次冻结的 Contract v2 进程/托管程序集回归通过。
- 通用控件树、状态绑定、插件动作、事件、导航保留会话、权限过滤、托管启用确认和取消歌词关联持久化通过。
- 通用输入控件使用实际键盘事件验证 Enter 执行动作、Shift+Enter 换行；可选实际进程包在 1280×800 和 900×650 检查页面与有界输入区。
- 原 Windows 发布目录的精简程序验证冻结 v2 进程/托管插件、真实 AOT 插件入口、配置加密恢复、歌词 RPC、关联、嵌入确认、备份和生命周期。不使用用户源文件或真实模型凭据。
- 原 Windows 388 个 Data/引导文件在原地升级和隔离验收后 SHA-256 一致，旧程序在 artifacts 中可恢复。
- 三个桌面 ZIP 从全新暂存构建，不含便携数据、插件或配置；逐文件 SHA-256、许可、版本及 POSIX 执行权限通过。Windows 安装器只枚举必要程序文件。
- 独立插件模板直接引用宿主 SDK/打包器，通用 Hello World、配置、最小包、覆盖保护及三平台打包通过；模板和私人插件不进入公开源码或 Release。

复现：

    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/extensions --extensions
    dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/compatibility --compatibility
    pwsh -File scripts/verify-clean-desktop.ps1 -Directory artifacts/releases/update-packages/0.4.0-beta.8 -Version 0.4.0-beta.8

冻结回归保留 artifacts/frozen-extension-v2 首次构建的字节，不以重新编译替代旧包。该基线不提供实时 DSP/解码器 ABI；托管和进程模式均不应被描述为操作系统安全沙箱。

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

纯净 ZIP 为 `NonetMusicPlayer.Desktop-<version>-<rid>.zip`。Windows / Linux 的程序在 ZIP 根目录，macOS 的程序为 `Contents/MacOS/Nonet`，需解压到 `Nonet.app/` 内。三个包均含 `NonetMusicPlayer.update.json`。

公开源码仅包含桌面、CLI、共享核心 / SDK、打包器与必要测试文档，不包含模板、私人插件、移动端、使用数据或旧开发历史。运行中的发布文件夹不参与取包；原地更新须在程序退出后进行并保留数据与备份。

## 未完成的平台验证

- macOS / Linux 交互、音频设备、文件关联、更新助手和原生窗口尚待实机验证。
- 没有进行代码签名或 macOS 公证。
- GitHub 在线 Release 下载到原地更新，需发布符合契约的 Release 资产后继续验证；仅上传源码不会产生在线更新。

复现命令见 README；ZIP 校验使用 `pwsh -File scripts/verify-clean-desktop.ps1`。

## 0.4.0-beta.16 验证范围

- 共享核心回归通过：冻结的插件 Contract、命令参数及确认、本地化资源、音频与歌词模型、持久化和 TUI。
- 隔离媒体与目录夹具通过：带音轨视频始终静音、GIF/动态 WebP 画面循环、有界帧缓冲、隐藏恢复、损坏文件及临时副本清理、同步旧背景迁移、多文件夹分别建歌单、递归导入与同名歌词。
- 使用原 Windows 发布目录中的精简程序与随包解码器验证三种动态媒体，不以未裁剪的开发宿主代替；最小化与恢复后背景正常，音乐输出链路未被使用。
- 自启动默认关闭，验证 Windows 命令引用、Linux Desktop Entry 字段转义与 macOS plist 参数编码；夹具不启用实际系统启动项。登录启动及系统管理策略仍需在各目标系统验证。
- 三平台桌面 ZIP 与 CLI ZIP 不含使用数据、私人插件或调试文件；文件摘要及 POSIX 可执行权限通过。
- Windows ZIP 首次启动及独立 CLI 命令通过；Windows 安装器 15 个程序文件与 ZIP 摘要相同，隔离卸载保留非程序数据。
- Windows 原发布目录的既有数据与更新前备份逐文件摘要相同。Linux/macOS 仅交叉构建及归档验证，未进行实机运行验证。

以上为 beta.16 的历史验证记录。动态背景及其专用回归工具已在 beta.17 移除；如需复现 beta.16，应检出对应版本的源码。

## 0.4.0-beta.17 验证范围

静态背景与目录导入回归命令：

```powershell
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/background-folders-beta17 --background-folders
pwsh -File scripts/verify-clean-desktop.ps1 -Version 0.4.0-beta.17
```

移除视频、GIF、动态 WebP/APNG 背景及专用解码器；保留音乐播放依赖的 SoundFlow FFmpeg 原生库、开机自启动及以文件夹新建歌单。旧设置迁移和解码器清理只作用于应用管理的路径，不修改用户原媒体。

日期：2026-10-11。验证运行于 Windows；Linux/macOS 未做实机交互验证。

- 解决方案构建成功，0 警告 / 0 错误。共享核心、完整桌面交互、旧插件 Contract 兼容及静态背景/目录导入检查通过。封面测试按现有原图像素策略修正旧版固定 320px 断言。
- 实际音频回归覆盖 WAV、FLAC、MP3，44.1/48/96 kHz、单/双声道、定位、输出设备重绑、10 次自然切歌、4 次重复播放及损坏流恢复。精简的原 Windows 发布程序亦通过真实音频、封面、歌词、持久化和插件配置检查。
- 三个平台桌面 ZIP 均通过逐文件 SHA-256、清单、GPL 内容、纯净文件范围及 POSIX 权限检查；文件数恢复为 Windows 12、macOS 15、Linux 12，不包含 VideoDecoder 或独立 FFmpeg 程序。三个独立 CLI ZIP 同样不包含图形依赖或使用数据。
- Windows ZIP 首次启动及 CLI 单次命令通过。安装器的 12 个程序文件与 ZIP 摘要一致；模拟旧 VideoDecoder 后，安装只移除已知解码器文件，保留未知文件；卸载保留非程序数据。
- 原 Windows 发布目录更新前后，400 个数据与引导配置文件的 SHA-256 保持一致。旧程序备份保留在本地 artifacts，不进入公开源码或发行包。
- 当前源码不再依赖动态背景专用构建工具。静态背景、关闭旧动态配置、不修改原媒体、受控解码器清理、图片缓存上限、文件夹递归导入、同名歌词关联及自启动默认关闭均通过隔离测试。

## 0.4.0-beta.18 验证范围

日期：2026-10-11。Windows 实际运行验证；Linux/macOS 桌面仅交叉构建与发行包检查。

- 解决方案构建通过，0 警告 / 0 错误；共享核心、完整桌面交互及冻结旧 Contract 插件兼容检查通过。
- 从实际设置选择框切换左右按键位置，核对共享配置、按钮顺序、即时布局与保存读回；两种布局都不再以 Windows 作为测试前提。macOS 左侧、Windows/Linux 右侧的模型默认值保持不变。
- 触摸和 3:2 布局覆盖 1440×960、1080×720、900×600、640×480；按键槽宽固定 22 DIP，总宽不超过 66 DIP。此项为隔离布局夹具，不代表跨平台实机交互已验证。
- 原 Windows 精简发行程序通过真实音频、封面、持久化和插件配置检查；新 ZIP 首次启动及 CLI 单次命令通过。
- 三平台桌面 ZIP、CLI ZIP 纯净范围与摘要检查通过；Windows 安装器的 12 个程序文件与 ZIP 一致，隔离卸载保留非程序数据。
- 原 Windows 目录更新前后，400 个数据及引导文件的 SHA-256 完全一致。用户数据、移动端、插件模板和私人插件不进入公开源码与 Release。
