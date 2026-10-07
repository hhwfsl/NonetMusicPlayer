# 通用扩展接口参考（SDK 4.5）

## 设计边界

Nonet 的桌面宿主采用三类扩展入口：数据服务、业务流程钩子、界面贡献。复杂自绘和低延迟音效另有独立的可选托管接口。插件通过能力协商选择入口，而不是依赖某个应用版本或具体插件名称。

“一切可扩展”表示非核心领域有可组合的扩展入口，不表示无限权限，也不表示能预测尚未定义的业务。后续新增领域使用新的能力版本，并保留已有接口及默认行为。

以下部分由宿主管理，不提供替换内部对象的接口：

- 插件身份、信任确认、权限和审批校验。
- 数据库事务、文件归属、引用清理、迁移、备份一致性。
- 解码器、音频设备句柄、播放时钟、设备恢复和播放锁。
- 应用更新校验、进程唯一性、退出与资源释放。
- 设置、插件管理、终端及恢复入口的可达性。

插件可以申请调用这些领域的公开事务，不可以绕过其保护。音效处理只借用小段 PCM；不能改变缓冲长度、声道数或时钟。

## 能力目录

调用 `host` 服务，参数 `{}`，返回实际 capabilities、slots、hooks、services、过滤后的命令及当前表现状态。只声明必需的 requiredCapabilities。SDK 的 AssemblyVersion 保持 4.0.0.0。

| 能力 | 入口 |
| --- | --- |
| host.catalog.v1 | host：发现目录和状态 |
| host.query.v1 | query：只读分页查询 |
| storage.v1 | storage：本插件键值数据 |
| logging.v1 | log：插件事务日志 |
| events.publish.v1 | events：本插件命名空间的事件 |
| workflow.v1 | hooks、INonetWorkflowExtension / extension.hook |
| ui.surfaces.v1 | shell.* 及新增 UI 插槽 |
| ui.native.v1 | INonetNativeViewExtension / INonetNativeView |
| files.transactions.v1 | files：用户选择文件后执行事务 |
| audio.pcm.v1 | INonetAudioExtension / INonetPcmProcessor |
| metadata.v1 | metadata：修改软件内的元数据 |
| artwork.v1 | artwork：有界封面缩略图 |
| maintenance.v1 | maintenance：宿主软件及插件更新检查 |

服务仍使用 ExtensionHostRequest(id,service,arguments)，由 CompleteAsync 接收结果。请求 ID 不能重用。例：

```json
{
  "id": "query-001",
  "service": "query",
  "arguments": { "operation": "music.list", "arguments": ["keyword"], "offset": 0, "limit": 50 }
}
```

## 数据和控制服务

### query

支持 status、music.list、music.info、playlist.list、queue.list、history.list、statistics、player.devices、settings.list/get、plugins.list、selection.list。arguments 为命令参数数组。offset ≥ 0，limit 为 1–100。结果包括 success/data/total/offset/limit。先分页再脱敏，不暴露本机路径、凭据或其他插件配置。

查询权限沿用对应命令的权限；`host.commands` 是权威目录。控制操作通过 commands 调用同一业务路由。可控制播放、队列、收藏、歌单、历史、设置、导航、窗口、桌面歌词、选择、插件启停/卸载/音源刷新与撤销。文件路径不接受模型或进程插件直接提供；使用 files 服务。

### metadata

需要 library-write。参数为 `{trackId,values:{title,artist,album}}`，只更新现有歌曲的软件元数据，支持撤销，不写音频源标签。字段只能是非空、无控制字符、最长 1000 字符的文本。按照宿主审批策略确认。

### lyrics

read/associate/unlink 保持既有契约；新增 parse，参数 trackId 与可选 text，返回逐行、译文和逐字时序的数据。读取和解析需要 music-read，修改关联需要 lyrics-write。手动取消歌词关联后，后台插件不能擅自恢复。不会删除源 LRC 或源音频。

### storage

参数 operation=get/put/delete/list、key、value。key 只允许字母、数字、点、连字符、下划线，最长 100 字符。每插件最多 512 个键、2 MB JSON。不能以 key 访问文件路径。

文件位于宿主管理的 Storage/<id>/host-values.json，同 ID 更新和保留文件卸载不丢失。卸载并删除文件时一并删除。存储不是凭据保险箱；API Key 等敏感配置使用原保护配置服务。

### log

参数 level=info/warning/error、message。最长 4000 字符，按插件 ID 归类并脱敏，写入同一 Logs 管道；不将命令文本作为日志。插件不得把密钥或聊天附件主动写入日志。

### events

需要 plugin-services。参数 name、data。宿主发送 `plugin:<id>.<name>`，插件不能伪造系统事件或其他插件的身份。订阅 events 可以使用结尾 .*，例如 player.*。同名待处理事件合并为最新值，队列最多 64 种事件；这不是可靠消息队列或审计日志。

系统事件包括 player.track-changed/state-changed/position、navigation.changed、settings.changed、library.changed、window.changed、operation.completed、library.lyrics-missing。敏感数据不加入载荷。精确事务审计使用 Logs，状态事件用于刷新或触发异步任务。

## 文件事务

files 需要 user-files 和当次用户操作，路径只来自宿主文件选择器，不接受 arguments 中的路径。返回成功状态，不返回源路径。

| operation | 附加权限 | 参数与用途 |
| --- | --- | --- |
| import | library-write | playlistId；用户选歌曲后添加到歌单 |
| temporary-play | player-control | 用户选歌曲后临时播放 |
| lyrics-import | lyrics-write | trackId；用户选 LRC/TXT 后关联 |
| lyrics-export | library-write | text；用户选择保存位置 |
| playlist-import/export | library-write | 导出需要 playlistId |
| playlist-cover | library-write | playlistId；选择及裁剪封面 |
| album-cover/artist-cover | library-write | name；修改分类封面 |
| background/font | settings-write | 选择背景或字体，使用现有验证与导入逻辑 |
| data-directory | settings-write | 用户选择并确认新的数据目录 |

files.pick 保留原文件快照语义及每次接收确认。files 不会将源音频发送给插件进程；大音频导入与临时播放不经过 Base64 整文件快照。图片沿用宿主大小限制和裁剪流程。

## 界面扩展

原页面、菜单、卡片和 overlay 插槽继续支持。新增：

- shell.workspace：替换整个导航/内容/播放器工作区。
- shell.navigation / shell.player：分别替换侧栏和播放器区域。
- shell.titlebar：替换标题品牌区域，宿主窗口控制和恢复入口不受覆盖。
- settings.sections：添加设置卡片，支持设置搜索。
- navigation.items、tray.actions：添加导航及托盘动作。
- album.more、artist.more、history.more、selection.more、library.more：对应上下文动作。

多个替换贡献按插件 ID 确定优先项；工作区替换优先于其中的区域替换。支持 Native=true 或通用 view 控件树。树中 bind=host.title/position/duration/playing/volume/accent 等可读取实时宿主表现数据，需 music-read。host 还含 effectiveTheme、窗口尺寸和 renderScaling。

进入设置、插件管理或终端时恢复原工作区。Ctrl+Shift+Escape 停用界面扩展并打开插件管理。关闭或失败的视图释放后恢复默认，不覆盖用户布局文件。

### 原生视图

需要 runtime=managed、in-process/native-ui/ui-extend、ui.native.v1 和首次托管信任确认。实现 INonetExtension 的类额外实现 INonetNativeViewExtension：

```csharp
public INonetNativeView CreateView(ExtensionViewContext context)
{
    return new CustomView(context);
}
```

CustomView 实现 INonetNativeView：

- View 返回新的 Avalonia Control，不能返回已挂在其他父控件上的对象。
- Update(ExtensionFrame) 更新自己的视图；调用发生在 UI 线程，不能阻塞。
- DisposeAsync 释放订阅、图像和后台任务；离开区域释放视图，不结束后台插件会话。
- context.RequestAsync 调用公共服务；context.InvokeAsync 调用本插件动作。

插件自行引用与宿主兼容的 Avalonia 12.1.x 包，可实现复杂控件、动画、布局、触摸及自身窗口。SDK 本身仍不引用 GUI 包。Avalonia 类型从宿主共享加载；不能把另一个框架版本强行塞入默认运行时。升级框架主版本时宿主须提供旧原生能力适配或继续保留其运行基线。

托管代码具有普通用户权限，不是安全沙箱；仅安装可信插件。数据/进程模式不接受任意 XAML、脚本或样式表达式。

原生视图的后台查询使用 `ExtensionViewContext.RequestAsync`；明确的用户动作使用 `RequestFromUserAsync`，仍需权限和宿主选择器/确认。获得焦点的原生视图保留自身按键，播放器全局快捷键不抢占。自定义事件发布上限为每插件每秒 20 条。

封面查询服务 `artwork` 需要 music-read，参数 `{type:"track"|"playlist",id,size:256}`。返回 PNG 的 Base64，不返回本机路径；输出宽度 32–512、上限 2 MB，输入最多 32 MB。无封面时 data 为 null。

更新检查服务 `maintenance` 使用 operation=software-check（settings-write）或 plugins-check（plugins-control）。仅调用宿主检查与提示，不允许插件指定更新二进制、绕过签名/清单校验或自动信任新代码。

`files` 还支持 folder-import、data-backup、data-restore、backup-directory、plugin-import、cover-reset。均由用户动作发起，路径来自本次宿主选择器；数据恢复确认后执行，安装可执行插件始终要求独立信任确认。cover-reset 的 type 为 playlist/album/artist，分别使用 playlistId/name，可为分组指定 softwareDefault=true；它不删除源音频或源封面。

## 流程扩展

清单声明 workflow 权限、workflow.v1 和 hooks。可用钩子：

| name | 数据 | 可采用决定 |
| --- | --- | --- |
| command.before | operation | 取消通过命令路由执行的事务；不改写参数或审批 |
| playback.before | trackId/source | 取消或选择当前队列中的其他 trackId |
| navigation.before | page | 取消或改为合法页面；恢复页不拦截 |
| import.before | playlistId/inputCount | 取消导入；不取得或重写原始路径 |

桌面按钮直接调用的历史事务会继续发布 operation.completed，但不应把它误当成可取消的 command.before；需替换相关界面入口时，使用通用界面贡献并通过 commands 路由执行事务。

托管类可选实现 INonetWorkflowExtension.EvaluateAsync。进程响应 extension.hook，参数 hook 为 ExtensionHook JSON，结果为 `{cancel:false,data:{}}`。决定最长 32 KB。不在决定过程中调用宿主事务；重入请求被拒绝。正常决策预算 500 ms，整体等待有界；失败时保留原宿主流程，并在当前会话停用故障钩子。

只有声明的钩子被调用。旧插件无 hooks 时保持原流程和性能。排序按插件 ID；前一个取消后不再执行后续决策。

## 音源与歌词角色组合

通用 extension 可声明 providedServices 包含 media-source 或 lyrics-source，不再要求页面和来源属于两个插件。

宿主以 service:media-source / service:lyrics-source 的 ExtensionInvocation 调用，values.method 表示实际动作。插件将数据放入 frame.state.serviceResult，不能附带递归宿主事务。

- media-source：catalog.list（cursor）返回 ProviderCatalog；playback.resolve（trackId）返回 PlaybackSource。最多单页 1000、总 50000，禁止重复 cursor。需 network；播放仍采用经过校验的 loopback-http/Range，由宿主解码。
- lyrics-source：lyrics.search/fetch/auto，使用已有 LyricsQuery/LyricsCandidate/LyricsFetchResult DTO 与格式校验，需 network、lyrics-write。音频嵌入还需 audio-tags 和独立的源文件修改确认。

v1 的 provider/lyrics 入口继续保留，既有插件不要求重打包。

## 低延迟音效

需要可信 managed、in-process/audio-processing 和 audio.pcm.v1。实现 INonetAudioExtension.CreateProcessor，返回 INonetPcmProcessor。Process 接收交错 Float32 Span 与 ExtensionPcmFormat，不拥有内存，不能保留 Span 或指针。

最多 8 个处理器，按 ID 排序。必须同步、快速、无文件/网络 IO、无宿主回调；参数在控制线程更新。可在可信托管桥接中调用使用固定 ABI 的 Native AOT/C/C++ 动态库，不能把独立 AOT 可执行文件直接当作 managed DLL 加载。

宿主每次只保留当段缓冲以便回退；异常、NaN/Infinity 或超时会恢复此阶段输入并旁路该处理器。输出限制为有效音频范围。撤销租约后不再进入新回调，当前回调结束后才析构。

时限检测不能强制终止一个永远不返回的托管回调；不能保证恶意插件不阻塞声音。这是托管信任边界，需在发布前进行真实声卡、延迟和稳定性测试。

## 兼容与验证

INonetExtension 的六个基线方法不变；新增能力通过独立接口、字段与服务实现。新插件只声明需要的能力，旧插件默认值不变。冻结旧 process/managed 包纳入回归。

```text
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/universal --universal
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/frozen --extensions
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/compatibility --compatibility
```

独立 CLI 不加载桌面原生 UI 或桌面钩子；其共享播放引擎和命令仍无图形依赖。跨平台原生 UI/音效必须分别实机验证。

## SDK 4.5：复合输入、图片与独立浮层

新增可选能力：`ui.assets.v1`、`ui.composer.v1`、`config.approval.v1`、`interaction.v1`、`ui.overlay.v1`、`ui.large-text.v1`。旧插件无需重编译；程序集版本仍为 4.0.0.0。

清单 `navigationImage` 指向包内 PNG/JPEG/WebP/BMP（10 MB 上限）；页面 `image.asset` 和导航图片由打包器自动收集，禁止外部路径及缺失资源。控件新增可选 `selectedBind`、`cornerRadius`、`maxHeight`、`borderless`；横向 repeat 自动换行，适用于附件卡；accent 变体使用主题色圆形主要动作。默认值保持旧布局。

`config` 服务的 `approval.read` 返回本插件审批模式；`approval.set` 只允许直接用户点击的上下文，保存完整配置、不重启会话。提升模式必须由宿主确认，不能通过异步 interaction 设置审批。普通 `config {}` 仍打开配置页。

异步任务可调用 `interaction {service, arguments}`，service 仅限 files、files.pick、config（只打开）、dialogs.prompt、dialogs.confirm。宿主首先确认插件的交互请求，再执行自身选择器和权限检查；模型布尔字段不能制造用户手势。文件接收/安装信任确认不受自动审批模式省略。

原生 overlay 贡献可设置 `overlay:{width,height,transparent,topmost,showInTaskbar}`。宿主创建独立窗口，不跟随主窗口最小化；native 控件由已授权的 INonetNativeViewExtension 提供。关闭、停用、卸载和退出释放视图。透明行为依赖目标系统合成器。

可选 `ExtensionFrame.LargeTextState` 将状态上限从 2 MB 提高到 30 MB，传输硬上限为 32 MB；只有确需完整长文的页面使用。避免把附件 Base64、PCM 或全库封面塞入状态，流式更新复用正文控件，不重建行。

Agent 可调用全部有权限的公开服务，并根据目录生成 native / hook / PCM 插件源码；这不等于把进程工具转换成原生音效回调，也不授予执行生成代码、任意文件读取、秘密配置或安全内核绕过能力。
