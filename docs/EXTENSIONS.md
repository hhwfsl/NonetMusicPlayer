# 通用扩展开发：Contract v2

SDK 4.13 的 `network.library.v1` 允许复用宿主已配对设备曲库与共享事务，详见 [通用服务参考](UNIVERSAL_EXTENSIONS.md)。不向插件公开配对密钥或任意文件服务。

宿主提供布局、状态、动作、事件、权限和服务，插件拥有业务与流程。纯 SDK 直接引用主工程，不复制 DLL 源码或另写验证器。基础工程位于相邻 NonetMusicPlayerPlugin，具体插件各自独立目录和 Git。

## 运行方式

- process：独立进程，stdin/stdout JSON-RPC。可发布 Native AOT，或自包含 JIT。AOT 必须在目标 OS 原生编译，跨 OS 回退不能标成 AOT。闲置时无轮询，忙碌时每 200 ms 同步状态。
- managed：.NET DLL，实现 INonetExtension，由主进程加载并共享 SDK/运行时。首次启用需要明确确认信任；更新包撤销原信任。后台逻辑应异步且可取消，DisposeAsync 必须释放任务/事件。AssemblyLoadContext 可回收，不保证忽略取消或持有全局引用的插件被强制卸载。

两者都不是 OS 沙箱，安装前必须信任开发者。permissions 只限制通过宿主 API 进行的操作，不阻止插件自有 OS 操作。网络与文件权限必须在清单说明。

- declarative：纯数据控件树及宿主服务动作，不需要外部进程。不得附带程序或脚本；需 runtime.declarative.v1。

## 可选布局、审批与项目能力（SDK 4.3）

ui.elements.v1 提供控件查询、可撤销布局覆盖和自定义控件锚点，详见 [UI_LAYOUT](UI_LAYOUT.md)。旧 ui.workspace.v1 的 read/apply/reset 作为内存兼容适配器保留，不再从用户配置文件加载。可重排 Navigation/Content/Player 和播放器必要控件；新增 page.songs/page.history/page.albums/page.artists/page.playlist/page.statistics 替换插槽。设置与终端保留恢复入口，不开放任意 XAML 注入。

development.v1 的 development 服务需 ui-extend、plugins-control、plugin-development。package 生成并安装无程序的声明式插件；source 只生成新 C# 项目，不编译执行。目录由用户选择，ID 为 custom.<小写名称>，不覆盖旧项目。首次生成须选择工作区，设置中的数据与恢复可更改目录。

approvals.v1 通过清单 supportsApprovalModes=true 和配置 approvalMode=ask/assist/full 使用。ask 保留原确认；assist 自动批准可恢复设置和布局；full 批准已授权的宿主事务。模式升级由用户在宿主确认；安装包、模型和请求参数不能预授予，也不扩大系统权限。

纯数据页面可提供 initialState 与 actions 字典；action 是受权限约束的 service/arguments 数据，不求值脚本。新增字段是可选能力，原 Contract 1/2 接口与默认含义保持不变。

## 清单

稳定 id 是身份；数字 version 判断更新，仓库 owner/name 与 GitHub 一致。type=extension、contractVersion=2、runtime=process/managed/declarative、pageEntry 指向包内 Schema 2 JSON。managed 额外指定 extensionClass 和 in-process 权限；process 指定 process 权限。入口按 RID 声明，发行时每个 RID 一个 .impp。

requiredCapabilities 声明真正需要的协议，如 ui.tree.v2/state.v1/actions.v1/services.v1。初始化回报支持的能力；宿主缺少必需能力则拒绝启动，而不能猜测执行。可选能力采用协商与降级，不在旧字段上改变含义。

permissions 支持 navigation、player-control、music-read、library-write、settings-write、window-control、plugins-control、lyrics-write、user-files、ui-extend、plugin-services，以及 process/network/in-process。

## UI 树

schemaVersion=2，root 为通用节点，ownsHeader=true 时页面自己承担标题与标题行工具。类型包括 grid、stack、border、scroll、text、selectable-text、input、button、toggle、select、slider、image、repeat。没有脚本、动态 XAML、HTML、反射表达式。

节点支持 rows/columns、row/column、spacing、padding/margin、align、width/height、fontSize、背景资源名称。bind 是状态的点分路径；重复项用 item.xxx；visibleIf 是布尔状态路径。repeat 使用 template。input 将值写到 input 键，enterAction 拦截 Enter，Shift+Enter 保留换行。button 的 action 由插件自行定义，parameter 可读取 item 路径。图片 asset 只允许包内相对路径，文件应通过 --include 显式打包。

页面最多 128 KiB / 512 个静态节点 / 24 层；帧状态最多 2 MB，重复区单次最多 1000 项；插件应分页长数据。长任务只修改必要状态，宿主不重建整个页面，消息正文可使用 selectable-text 复制，容器无需选中。输入草稿随会话保留；页面离开仅解除 UI 订阅，不结束插件任务。

### 可选布局与菜单能力（SDK 4.1 / Nonet 0.4.0-beta.9）

新增字段不改变 Schema 2 或既有接口，使用时在 requiredCapabilities 中声明对应能力；不使用这些字段的原 v1/v2 包仍可运行。

- `ui.layout-bind.v1`：grid 的 `columnsBind` 读取状态字符串，例如 `"208,*"` / `"0,*"`，用于展开/收起后释放宽度。只接受原有安全尺寸语法，不执行表达式。首帧缺失时保留静态 columns，只有尺寸变化才重新验证和布局。
- `ui.context-menu.v1`：任意节点可带 `contextActions`，最多 16 个 type=button 的动作，使用 text/action/icon/parameter；parameter 的 item.id 指向当前重复项。由宿主提供原生右键菜单和键盘菜单操作，不授予新的业务权限，同页面只保留一个菜单。

示例：`{"type":"grid","columns":"208,*","columnsBind":"layoutColumns","contextActions":[{"type":"button","text":"删除","action":"delete","parameter":"item.id","icon":"Trash"}]}`。确认删除由插件请求 dialogs.confirm；菜单本身不能绕过宿主确认。

清单 author 是显示名称，不是 GitHub 用户名；未填或空白使用 Author。包文件名可用友好短名（如 nonet-agent-1.3.0-win-x64.impp），身份仍由清单 id 判定，更新来源仍使用 repositoryOwner/repositoryName。托管 SDK AssemblyVersion 固定 4.0.0.0，包版本可递增，不要求旧程序集重编译。

### 可选通用控件（SDK 4.2 / Nonet 0.4.0-beta.10）

声明 `ui.controls.v1` 可使用 icon、checkbox 节点，以及 variant/selectedIf/openMenuOnClick：

- icon 用宿主已有图标名，不执行矢量代码。
- checkbox 绑定可空布尔值（null 展示部分选中），交互值写入 input 并触发 action/parameter。插件负责全选和多选规则，不绑定特定业务。
- variant 仅接受 ghost/pill/row，颜色来自宿主主题；selectedIf 绑定布尔选中态。
- button 带 contextActions 和 openMenuOnClick=true 时，左键打开与原生右键相同的菜单。菜单不会直接执行按钮本身的空 action，键盘也可打开。

声明 `dialogs.prompt.v1` 可在当次用户操作中请求 dialogs.prompt，arguments 为 text（说明）/initial（初始文本）；返回 success/value。只能弹纯文本输入，不允许自动后台弹层或执行输入内容。插件验证业务名称，宿主限制长度；它不是脚本、命令或任意宿主方法接口。

所有字段都是新增可选值，原 v1/v2 默认行为和 INonetExtension 成员保持不变。

## RPC 和托管接口

所有 RPC 保留原 jsonrpc/id/method/params 结构；params 值是字符串，包括序列化的 DTO。进程启动：
initialize(contractVersion=2, configuration, storageDirectory, capabilities)
返回 contractVersion=2 和 capabilities 数组。

- extension.invoke：invocation 为 ExtensionInvocation(action, values) JSON。立即返回 ExtensionFrame；长任务在插件后台继续。
- extension.sync：返回当前帧。
- extension.event：event 为 ExtensionEvent(name,data) JSON。
- extension.complete：id 和 result，完成宿主服务请求。
- lifecycle.disable/uninstall/shutdown：清单显式声明的退出回调，限时后宿主结束进程。

帧包括 revision、busy、state 和 requests。busy=false 无闲置轮询，插件事件/用户动作可再次唤醒；busy=true 时同步。服务请求为唯一 id/service/arguments，宿主完成后回传结果。不得重用请求 ID；取消任务须移除未完成请求，避免取消后仍执行事务。

托管接口 INonetExtension 使用相同 DTO 的 InitializeAsync/InvokeAsync/SyncAsync/CompleteAsync/EventAsync/DisposeAsync，不依赖 Avalonia。业务代码不持有 UI 对象，使用状态渲染。源码夹具 tests/NonetMusicPlayer.ExtensionFixture 提供协议实现示例。

## 服务与安全

- catalog：返回按插件权限过滤的公共播放器命令。
- commands：operation 和 arguments，复用 UI/终端业务路由。只接受已有歌曲 ID，不接受模型路径。危险事务由真实用户确认，插件不能传 -y 绕过；结果移除本机路径、配置和敏感字段。
- config：仅用户动作打开宿主生成的插件配置表单。配置页由开发者 schema 定义；敏感值采用宿主保护存储。
- files.pick：必须有 user-files 和当次用户动作。选择后再次列出接收插件和文件，用户确认后只传名称/内容快照，不返回源路径。最多 8 文件、单文件 10 MB、总计 20 MB。不能自主选择、读取或删除源文件，接收插件仍须可信。
- lyrics：read 需 music-read；associate/unlink 需 lyrics-write，仅按现有 trackId 操作本软件数据。用户取消关联后，后台任务不得重新覆盖；原音频/歌词源文件不更改。
- dialogs.confirm/notify：显示带插件身份的纯文本提示，确认必须由当次用户操作引发。
- plugin:<id>/<service>：需 plugin-services，目标需提供 providedServices。目标收到 action=service:<name>，其 state.serviceResult 作为返回；拒绝自身调用和循环依赖，最长调用链四层。该初版服务为短操作，长任务使用事件/状态，不能让一个插件阻塞另一个。

订阅 events 后收到 player.track-changed、player.state-changed、operation.completed、library.lyrics-missing。事件仅包含受限数据，不提供文件句柄；自定义服务与状态可组成工作流。卸载保留文件时插件原目录和 Storage/<id> 保留，同 ID 更新也保留存储；选择同时删除文件时删除所属包、配置和存储，不能撤销。

## 插槽与页面替换

contributions 中每项为 slot/label/action/icon，可带通用节点 view：
lyrics.more、song.more、playlist.more、player.actions、titlebar.actions 为动作入口；
home.cards 为自定义卡片；overlay 为可移动/调整大小的独立浮层；
page.music/page.lyrics 可替换相应默认页。必须有 ui-extend；多个贡献按插件 id 确定顺序，页面替换采用第一项，停用即恢复默认。open-page 需 navigation，其他动作交给插件，提供 trackId/trackIds/playlistId 上下文。

主题插件 v1 继续支持；extension 具 ui-extend 时也可提供原四个颜色 tokens，宿主计算统一主题。设置和插件管理保留安全入口，不允许插件替换后阻止停用。

## 兼容保证与边界

旧插件可按相同 ID、更高版本单向迁移至通用 Contract v2，保留配置和自身存储；权限变化时停用并重新确认。v2 不允许反向迁移或任意类型替换。托管新包更新须重新确认信任，不从包清单接受执行授权。

首次 v2 已有方法、构造器、字段意义和默认值被冻结。后续采用新可选字段、独立可选接口和能力版本；不删除旧 v1/v2 适配。不得给 INonetExtension 添加必需成员，破坏已编译实现。运行 --extensions 回归会使用 artifacts/frozen-extension-v2 首次冻结的工作进程/程序集，不覆盖旧字节；另运行 --compatibility 验证 v1。

实时采样不经过 JSON-RPC；SDK 4.4 新增独立的可信托管 PCM 音效接口和原生视图接口，详见 [UNIVERSAL_EXTENSIONS](UNIVERSAL_EXTENSIONS.md)。解码器、设备和播放时钟仍为宿主核心。数据 UI 不支持任意 XAML。桌宠可由浮层、图片、状态和播放器命令组成，但 OS 桌面注入不属于宿主权限。

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

托管插件不是沙箱，也不是崩溃隔离。宿主可捕获自身调用、生命周期、事件及受监督任务中的普通异常并停用对应插件；栈溢出、任意线程未捕获异常、FailFast、原生致命崩溃或全局状态污染仍可能使 Nonet 退出。导入与首次启用显示风险提示，更新包撤销托管授权。取消令牌只支持协作停止；无法强制杀死不合作线程或保证立即卸载 DLL。需要崩溃隔离时选择 process，不因模式不同削减服务能力。

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

### 音源启动和目录导入

需要随应用恢复账号状态的扩展声明 `autoStart: true` 与既有 `runtime.startup.v1`。初始化应立即返回，再在后台验证保存的会话；验证过程中返回 `busy: true`，结束后返回状态文字及 `busy: false`，宿主会同步页面状态。不要把自动验证放在“打开插件页”动作中，也不要在配置重载的析构回调中清除会话。

`catalog.list` 中的 ProviderTrack 可直接携带封面与独立歌词轨道。宿主导入时落盘这些元数据，不等待首次播放。插件应分批返回数据并限制图片体积，保持状态帧不超过 Contract 上限；无封面或单张图片失败不应让整个歌单无法导入。搜索型音源应从自己的页面发起 `media/import-catalog`，插件中心不提供无目标的整库读取。

后台导入可声明 "media.import-background.v1" 并传入 "background: true"，使目标歌单先建立、引用先导入、信息随后分批补全。页面可声明 "ui.visibility-actions.v1"，通过 "hideTargets" / "showTargets" 独立切换局部结果可见性。完整参数和兼容行为见 [通用扩展接口](UNIVERSAL_EXTENSIONS.md)。
