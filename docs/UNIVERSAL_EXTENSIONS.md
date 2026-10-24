# 通用扩展接口参考（SDK 4.16）

## 选择器与近期统计（SDK 4.16，可选）

`ui.select-options.v1` 为 `select` 增加 `popupPlacement` 和 `suppressTooltips`，见 [PLUGIN_DEVELOPMENT](PLUGIN_DEVELOPMENT.md)。同一组选项在状态刷新期间保持身份，提交动作完成后同步选中值。

`statistics.charts.v1` 提供 `ListeningBarChart` 样式类型和趋势卡片样式类，见 [STYLE_EXTENSIONS](STYLE_EXTENSIONS.md)。既有 `query` 服务的 `statistics` 操作结果新增可选 `recent`：`days` 为最近七个本地日，值为秒；`hours` 为包含当前小时的二十四个小时，值为播放次数。每点包含 `label`、`value`、`detail`。需要现有 `music-read` 权限，无需新增写入权限。

小时计数基于开始聆听超过一秒的真实播放会话；暂停恢复不重复计数。UTC 整点键避免本地小时重复，持久化最多七十二个桶。旧版本只有每日数据，因此旧小时图保持零，不根据每日记录分摊。`recent` 不受既有 day/month/year 筛选改变，其余统计字段语义保持不变。

## 类型级风格与动画（SDK 4.14，可选）

声明 `ui.styles.v1` 和 `ui-extend` 后使用 `styles` 服务，支持 catalog/apply/reset/morph。风格数组从底层到顶层排列，按控件类型、类名、模板部件与状态合并属性；资源和动画均可覆盖或补充。宿主管理可释放绑定、动态控件匹配、减少动画与快照清理，插件管理导入、排序及持久化。完整格式见 [STYLE_EXTENSIONS](STYLE_EXTENSIONS.md)。

可选清单 `autoStart:true` 需要 `runtime.startup.v1`，启用与宿主启动后初始化；`nativePage:true` 使用现有 INonetNativeViewExtension 工厂创建 `plugin.page`，需要可信托管原生 UI 权限。两项默认 false，旧扩展不受影响。

## 局域网设备曲库（SDK 4.13，可选）

声明 `network.library.v1` 后调用 `network` 服务。读操作需要 `music-read`；`peers`、`playlists`、`tracks` 支持 `offset` 与 `limit`（1–100）。查询仅返回稳定 ID 与公开音乐元数据，不返回本地路径、邀请、会话令牌或临时音频 URL。

| operation | 参数 | 返回/行为 |
| --- | --- | --- |
| `status` | 无 | sharing、connectedDevices；新增可选 bidirectional、approvalWaitSeconds、pendingRequests |
| `peers` | offset、limit | id、name、trackCount、playlistCount、可选 deviceKind（desktop/mobile/unknown）、reachable（布尔）、connectionState（available/reconnecting） |
| `playlists` | peerId、offset、limit | id、name、description、count、可用于 navigate 的 page |
| `tracks` | peerId、offset、limit | id、title、artist、album、duration、source=device、可选 deviceKind |
| `open` | 无 | 进入宿主设备配对页，需要 network、navigation 及直接用户手势 |
| `set-sharing` | enabled（布尔） | 启停本机只读共享，需要 network、直接用户手势与宿主审批 |
| `disconnect` | peerId | 断开已配对设备，需要 network、直接用户手势与宿主审批 |
| `set-approval-wait` | seconds（10–120 的整数） | 修改后续接入请求的确认时限，需要 network、直接用户手势与宿主审批 |

结果沿用 `{success,data,total,offset,limit}`。通过 `commands` 的 `player.play`、`music.info`、`favorite.add`、`playlist.add` 操作返回的歌曲 ID，权限与审批沿用原规则。插件不能替换 TLS 验证器、打开任意路径或操纵对方播放器。配对邀请只在宿主页面处理。共享每次启动默认关闭，认证仅保存在内存。

远端歌单在宿主歌单栏中以临时只读导航显示，断开后消失；不写入或再次共享为本地歌单。新客户端一次配对建立双向读取，播放仍独立。接入请求必须由共享端用户接受，默认 30 秒，可设置 10–120 秒。此安全确认不作为插件可批准或可自动化的操作公开；插件可以读取已配对设备的公开元数据，但不能代替用户接受外部连接。宿主终端的 network accept/reject 直接完成真实用户的待处理审批，不向插件公开这些命令。

订阅 `network.changed` 获取 `{sharing,connectedDevices}`，再分页查询最新数据。此事件不含秘密。既有 Contract v1/v2、已编译 SDK 程序集身份 4.0.0.0 和旧默认行为不变。

`connectedDevices` 表示配对数，不能当作此刻网络可用数。暂时停网、后台休眠或探测失败只改变可选 `reachable` / `connectionState` 并发出原有变更通知，保留曲库与授权；恢复后继续原会话。旧插件可忽略新字段，不需要重编译。插件不能获取令牌、绕过证书校验或以不可达为由替用户接受新配对。

## 分页与按需封面（SDK 4.9，可选）

搜索音源声明 `"musicSearch":{"label":"音源名","pageSize":30,"deferredArtwork":true}`，并在 `requiredCapabilities` 声明 `media.artwork-batch.v1`。没有新字段时仍使用内联 `CoverBase64`，不请求新方法；旧包和程序集不需要重编译。

- `music.search` 新增字符串参数 `includeArtwork`。启用延迟封面时为 `"false"`，否则为 `"true"`；旧音源可以忽略此参数。先返回元数据，避免等待整页图片。
- `music.artwork` 接收字符串参数 `trackIds`，其值为 JSON 字符串数组，如 `"[\"123\",\"456\"]"`。单次 1–30 个稳定歌曲 ID，只能查询发起音源自己的歌曲，不接受任意网址或本机路径。
- 返回 `MusicSearchPage`，每项包含该歌曲的正常 `ProviderTrack` 元数据与可选 `CoverBase64`。可以省略无封面的歌曲；不得返回请求外的 ID，不能返回重复 ID 或超过请求数量。封面保持原编码大小及像素限制。
- 宿主仅按虚拟化视口请求封面，显示及磁盘缓存有界；缓存淘汰后允许重新查询。音源也应限制缩略图和元数据缓存。
- 宿主滚动到底使用 `nextCursor` 自动追加。追加不重建旧行；空页、重复游标或没有新增 ID 时结束分页。下一游标为空表示结束，`total` 只用于信息展示，不能代替游标。

云端播放的网络读取与解码在后台执行，PCM 缓冲目标为四秒且不超过 8 MiB，网络块不超过 1 MiB。缓冲不足时暂时输出静音并保留歌曲时钟；不缓存整首音频。暂停停止消费，寻址丢弃旧 PCM，关闭先取消网络再释放解码器。持续低于播放码率的网络仍可能需要重新缓冲，缓冲不能替代足够带宽。

## 动态图片绑定（SDK 4.8，可选）

使用 `ui.image-data.v1` 的页面可声明 `{"type":"image","imageDataBind":"qrImage","width":240,"height":240}`。状态键 `qrImage` 为 PNG/JPEG 的纯 Base64，空字符串清除图像。不要传入网址、data URI、SVG 或文件路径；宿主不自行下载图像。原 `asset` 包内图片入口保持兼容，两者同时存在时动态绑定优先。

单张编码数据最多 2,000,000 字符，原图单边最多 8192、总像素最多 16,000,000；显示解码最长边最多 1024。图片仅在数据变化时解码；替换或离页立即释放像素。无效图片显示为空并记录警告，不执行图像内脚本。所有图片仍计入 ExtensionFrame 原有总状态限额；不要用持续增长的状态数组保存历史二维码。

插件在 `ExtensionInitialization.StorageDirectory` 保存自己的登录会话；仅将公开状态和二维码写入 UI，不把 Cookie、令牌或密码写入 Frame、日志、包或明文配置。普通进程不是沙箱，插件必须可信。登录交互、服务端授权与会话加密由音源实现，不在宿主加入账号供应商条件。

## 音乐搜索与目录导入（SDK 4.7）

仅需搜索的音源可继续使用 Contract v1 provider；需要页面/导入交互的音源使用 Contract v2 extension，声明 `providedServices: ["media-source"]`。两者均声明可选 `musicSearch: {"label":"音源名","pageSize":30}`、`network` 权限。v2 需要 `process` 或已确认的托管模式，按需声明 `media.search.v1` / `media.import.v1`。旧包未声明此字段时不增加搜索项、不请求新方法。

v1 直接使用同名 RPC；v2 使用 `extension.invoke`，动作 `service:media-source`，在 Values 传入 method 及以下字符串参数。返回 `ExtensionFrame.State.serviceResult`，不能在媒体查询帧附带递归宿主事务。

| method | 参数 | 返回 |
| --- | --- | --- |
| `music.search` | query、cursor、limit | `MusicSearchPage`：tracks、nextCursor、可选 total/message |
| `music.describe` | trackId | `ProviderTrack`，可选有界 CoverBase64 / Lyrics |
| `catalog.list` | catalogId、cursor | `ProviderCatalog`；省略 catalogId 保持旧目录语义 |
| `playback.resolve` | trackId | 既有 `PlaybackSource`，kind 为 loopback-http |

query 为 1–300 字符；每页 1–100 首；游标最多 512 字符；歌曲 ID 最多 300 字符，标题/艺术家/专辑最多 1000 字符。SDK 共同验证元数据、重复 ID、有限时长和大小。音频 URL 只保留在内存，不能作为歌单永久身份。宿主仅接受回环 HTTP 播放地址，音源应实现增量流式 Range 代理，按其服务器协议管理鉴权与短时地址。

v2 插件的用户动作可请求 `media` 服务：`{"operation":"import-catalog","catalogId":"稳定目录ID","name":"歌单名"}`。需声明 `library-write`，且必须来自用户手势并遵循宿主确认。宿主只请求发起插件自己的媒体服务，按游标校验并读取整个目录，最多 50000 首；全部成功后才一次性建立歌单。拒绝、重复游标、网络失败不建立半个歌单。不会修改远程歌单或下载音频。

搜索结果属于临时集合，可播放、收藏和加入普通歌单。`music.search` 可在每首 `ProviderTrack.CoverBase64` 返回封面；宿主在独立临时缓存显示缩略图，最多 256 张 / 32 MB，不因此建立音乐库引用。加入歌单或播放时转存封面，缓存清理不影响持久引用。加入后保存稳定 `ProviderId` / `ProviderTrackId`；卸载或禁用音源不会删除歌单引用，重新启用可继续使用。封面/歌词详情在播放时按需加载，尊重用户取消歌词关联及已有手动歌词。

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

## 无独立页面的扩展（SDK 4.6）

Contract v2 扩展可声明 `"hasPage": false`、`"pageEntry": ""`，并将 `ui.page-optional.v1` 放入 requiredCapabilities。省略 hasPage 时仍为 true，保留旧包行为；旧 Contract v1 不使用此字段关闭页面。无页面扩展仍可提供插槽、事件、流程和原生浮层，不生成侧栏页面按钮，直接导航也不会产生空白页。

没有页面的包不需要 page.json；打包、检查、启用、同 ID 更新和卸载后重新接入均使用同一规则。仍须满足其他权限与运行形态约束。若没有导航贡献，不必申请 navigation 权限。配置表单仍由插件中心管理，不因省略业务页面而消失。

示例清单片段：

```json
{
  "type": "extension",
  "contractVersion": 2,
  "hasPage": false,
  "pageEntry": "",
  "requiredCapabilities": ["ui.page-optional.v1"]
}
```

此片段不是完整安装清单，必须另行声明身份、运行形态、入口及所需权限。纯后台/浮层扩展不必创建无意义的空页面。

## 运行时与故障管理（SDK 4.10）

新插件默认使用 `runtime=managed`。Contract v2 实现既有 `INonetExtension`；Contract v1 的 provider/lyrics/agent 可选实现 `INonetManagedRpcPlugin`，保持原方法名及 JSON 结果。清单声明 `in-process`、`extensionClass` 和目标 RID 的 DLL 入口。构建为 .NET 10 类库，不打包 .NET 运行时或 SDK DLL，保留自身依赖与 deps.json。每平台单独生成 .impp。

`process` 自包含 .NET 与 Native AOT 保持同等支持，不要求旧插件重打包。进程仍使用 stdin/stdout JSON-RPC；stdout 不能写诊断日志。新进程实现可用 RPC 错误码 -32603 报告内部程序错误；-32000 等旧业务错误仍可恢复。AOT 跨 OS 需要目标原生工具链。

### 信任边界

托管插件不是沙箱，也不是崩溃隔离。宿主可捕获自身调用、生命周期、事件及受监督任务中的普通异常并停用对应插件；栈溢出、任意线程未捕获异常、FailFast、原生致命崩溃或全局状态污染仍可能使 Nonet 退出。首次启用时集中确认风险，不在安装后另发重复通知；更新包撤销托管授权，再次启用需重新确认。取消令牌只支持协作停止；无法强制杀死不合作线程或保证立即卸载 DLL。需要崩溃隔离时选择 process，不因模式不同削减服务能力。

### 运行时连接与后台任务

实现可选 `INonetRuntimeAware.AttachRuntime(PluginRuntimeContext)`，并声明 `runtime.supervised.v1`。宿主在初始化之前连接运行时上下文：

```csharp
private PluginRuntimeContext? _runtime;
public void AttachRuntime(PluginRuntimeContext context) => _runtime = context;
// 在明确的插件动作中启动；禁止 async void、未观察任务和自建无监督线程。
public Task RefreshAsync() => _runtime!.RunAsync(async token =>
{
    await RefreshCacheAsync(token);
    _runtime.Log("Cache refreshed.");
});
```

`Lifetime` 在停用、更新、卸载或退出时取消。后台循环必须响应取消；DisposeAsync 解除事件、停止网络代理和后台任务。普通未处理异常触发统一故障停用，不能通过继续轮询自动重新启动故障插件。取消及 `PluginOperationException` 表示预期业务拒绝，不作为程序故障；不要用业务异常隐藏程序错误。初始化、动作、事件、音效、原生视图和运行时监督任务采用相同插件归属。

### 日志

所有形态可请求 `log` 服务（`logging.v1`）：

```json
{"id":"log-1","service":"log","arguments":{"level":"info","message":"Cache refreshed."}}
```

可信托管插件也可调用 `PluginRuntimeContext.Log(message, level)`。info/warning/error 写入宿主 Logs，并发布到终端日志流。宿主添加插件名称及稳定 ID，脱敏凭据、地址和本机路径，单条最多 4000 字符且限制日志频率。不要写 API Key、Cookie、用户上传内容或模型消息；换行不能伪造日志前缀。

### 独立歌词轨道

`lyrics.tracks.v1` 为音源 ProviderTrack 增加可选 `lyricsTranslation`，`lyrics` 保留原文；旧字段与旧包行为不变。可以返回标准 LRC、Enhanced LRC 或原生网易云 YRC。不要把独立翻译直接拼接到原文中。宿主按原文时间有界匹配译文，译文使用独立较小字体，逐字翻译保留自身时序；无自身逐字时序时跟随原文进度比例。匹配不到的译文不变成第二条同等级原文。

宿主保存显式原文/译文段标识。可导入原生网易云 API JSON 的 lrc/yrc/tlyric/ytlrc 字段以及双段 LRC；普通时间戳重复与旧双语 LRC仍按既有规则处理。旧云端自动歌词可刷新为显式轨道，手动关联歌词不被覆盖。

## 控件级布局（SDK 4.11）

新增可选 `ui.elements.v1`：`ui` 服务的 `elements` 分页查询已实现控件，`update-elements` 提交有限布局属性及 Panel 父级/顺序，`reset-elements` 撤销当前插件覆盖。清单 `ui.element` 贡献可按命名 target 插入数据控件树或原生自定义控件，placement 支持 append/prepend/before/after/replace。C# 使用 `ElementLayoutContract.Update` 和 `ElementLayoutPatch`；原生工厂 context 带 target/placement。

控件属性覆盖保留原绑定；停用、故障、卸载和退出均撤销，导航后按稳定名称重新定位。运行时 ID 仅适用于当前控件实例，不强制实现虚拟列表。窗口核心和紧急恢复快捷键不开放；模板子节点不可任意脱离模板，可替换上层表现区域。完整限制、属性清单、代码参数与生命周期见 [UI_LAYOUT](UI_LAYOUT.md)。不再提供用户 JSON 布局入口；旧插件协议保留，不要求旧包重打包。

## 生成项目的存放位置

宿主 development/source 与 development/package 服务统一使用数据目录下的 PluginProjects，不再显示项目父目录设置或要求用户选取目录。仍需开发权限、正常审批及新项目 ID，不覆盖已有项目，也不编译或执行生成的源码。既有服务请求字段不变。

## 背景取色与外观（SDK 4.12）

可选能力 `appearance.background-accent.v1`，服务名 `appearance`。通过 `host` 的 services/capabilities 发现后再调用；不要求旧插件重新构建，SDK AssemblyVersion 仍为 4.0.0.0。

| operation | 参数 | 返回与约束 |
| --- | --- | --- |
| `read` | 无 | `accent`、`automatic`、`hasBackground`、`pending`、`motionEnabled`、`lyricsTransition`；需要 `ui-extend` |
| `extract-background-accent` | 无 | `found`、`color`（无可用颜色时为 null）；只分析用户当前背景，不修改主题，不接受文件路径 |
| `set-auto-accent` | `enabled: boolean` | 需要 `ui-extend` 和 `settings-write`，沿用设置审批；等待本次取色完成后返回外观状态 |

例如：`context.RequestAsync("appearance", new JsonObject { ["operation"] = "set-auto-accent", ["enabled"] = true })`。也可通过既有命令服务使用 `settings.set accentFromBackground true`；颜色仍由同一个宿主设置模型持久化，`host` 状态带 `accentFromBackground`。默认关闭；开启或背景变化时异步提取，关闭/移除背景后保留最后颜色。没有背景或透明背景时不虚构颜色，失败保留原主题并报告错误。只分析有界缩略图，不创建持续采样任务。

