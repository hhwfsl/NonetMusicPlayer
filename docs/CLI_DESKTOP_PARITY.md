# 桌面终端与 CLI 功能对照

桌面 0.4.0-beta.48 / CLI 0.4.0-beta.32 源码功能对照。只在实际发布对应 GitHub Release 时递增版本；本次 Release 仅发布桌面资产。两种终端使用同一命令目录、参数检查和危险操作确认，`nonet help` 只列当前宿主支持的命令。

| 功能 | 桌面终端 / CLI 公共入口 |
| --- | --- |
| 播放、暂停、停止、进度、音量、输出、模式 | `player play/pause/resume/stop/next/previous/seek/volume/mute/device/mode` |
| 实际队列、手动重排、手动选歌 | `queue list/play/select/move`；位置从 1 开始，调整队列不改原歌单，不重载当前音轨 |
| 随机播放 | 手动选歌生成完整排列并将所选歌曲放首位，前后切歌和循环保持顺序；切换列表/单曲循环恢复来源排序 |
| 音乐搜索 | `music sources/query/results/more`；新词/新来源替换结果，分页追加，加入歌单或喜欢才建立资料库引用 |
| 音乐、歌单、排序、多文件夹、M3U | `music list/info/remove/undo`，`playlist list/tracks/create/rename/describe/add/remove/delete/move/reorder/from-folders/import/export/cover` |
| 分类封面与内容 | `album list/tracks/cover`、`artist list/tracks/cover`，`auto` 恢复自动解析、`default` 使用默认封面 |
| 喜欢与最近播放 | `favorite add/remove`、`history list/remove/clear` |
| 歌词 | `lyrics show/path/import/export/reload/clear`；clear 取消软件关联，保留原文件；再导入恢复关联 |
| 听歌统计 | `statistics show/hours/history`；小时统计最多 72 小时，缺省最近 24 小时；明细按 session/interval/event/hour 分页 |
| 插件 | `plugins list/install/enable/disable/uninstall/config/schema/refresh/info/reload/state/invoke/trust` |
| 局域网互听 | `network sharing/invitation/connect/requests/accept/reject/peers/playlists/tracks/play/disconnect/renew/status`；显式批准后建立双向只读音乐共享 |
| 设置与备份 | `settings list/get/set/reset`、`data backup/restore`；各宿主只列自身设置，禁止 CLI 使用桌面资料目录 |

CLI 新增“播放队列”“平台搜索”“设备互听”TUI 栏目。队列和歌单可 Ctrl+↑↓ 调整顺序；搜索页 S 选择来源并输入关键词、L 加载下一页；统计页展示正常高度的最近 24 小时文字图表。全部非图形操作可由 Ctrl+K 菜单选择并填写参数。窄终端优先显示当前栏目，不要求固定分辨率。

桌面独有的窗口/桌面歌词/页面导航/框选/布局/托盘与图形更新入口，由 `window`、`desktop-lyrics`、`navigation`、`selection`、`layout`、`updates` 等命令提供。`window default-size` 恢复默认尺寸并居中；`queue show/hide/toggle` 控制播放器栏浮层。CLI 没有 OS 窗口、主题、拖拽动画或原生插件页面，这些命令不会出现在其帮助中。CLI 软件更新通过下载并替换对应 CLI 发布目录完成，保留 Data。

CLI 插件继续使用冻结 Contract v1/v2、SDK 包验证器和相同稳定 ID。v2 音源通过 `media-source` 服务提供搜索、目录、描述和播放地址；运行形态支持进程、可信托管及声明式数据扩展。可查询状态和调用动作，按声明权限处理已有命令/歌词请求；危险事务需在终端直接确认，不向插件授予 Shell、任意文件路径或隐式 `-y`。图形服务返回不支持，图形页面和聊天/桌宠类旧插件仍仅由桌面宿主承载。托管运行具有用户权限，致命错误可能使宿主退出；`plugins trust <id> -y` 明确记录共享运行时信任，启用仍需确认。更新代码包不继承未经重新确认的托管执行授权。

桌面与 CLI 共用账户加密的插件配置实现；Windows 使用当前用户 DPAPI，其余平台使用权限受限的本地 AES-GCM 密钥。安装索引只保存公开值，扩展存储位于包外 `Plugins/Storage/<id>`，同 ID 更新保留。CLI 备份命令导出自身状态，插件配置/包和外部音乐文件需要另行保留整个 Data；配对会话凭据只在内存中。

本地包目录：桌面 `publish/desktop/archives` 和 `publish/desktop/windows_installer`；CLI `publish/cli/archives`。纯净 ZIP 在复制用户数据前生成，包含逐文件摘要；Linux/macOS ZIP 保留程序执行权限。Windows x64 使用本机验证；其余目标的交叉构建和静态检查不能替代目标系统的音频/窗口实机验收。

验证入口：`CoreChecks --desktop-parity`（CLI 业务、三个通用音源、本机显式批准双向互听）、`UiChecks.Desktop --terminal-parity-only`（桌面实际业务入口）、`--track-reorder-only`（虚影、多帧动画与取消）、`PluginUiChecks.Desktop --music-search`（右上真实控件重复搜索）。测试夹具独立生成，不依赖私人插件。
