# 通用扩展开发：Contract v2

宿主提供布局、状态、动作、事件、权限和服务，插件拥有业务与流程。纯 SDK 直接引用主工程，不复制 DLL 源码或另写验证器。基础工程位于相邻 NonetMusicPlayerPlugin，具体插件各自独立目录和 Git。

## 运行方式

- process：独立进程，stdin/stdout JSON-RPC。可发布 Native AOT，或自包含 JIT。AOT 必须在目标 OS 原生编译，跨 OS 回退不能标成 AOT。闲置时无轮询，忙碌时每 200 ms 同步状态。
- managed：.NET DLL，实现 INonetExtension，由主进程加载并共享 SDK/运行时。首次启用需要明确确认信任；更新包撤销原信任。后台逻辑应异步且可取消，DisposeAsync 必须释放任务/事件。AssemblyLoadContext 可回收，不保证忽略取消或持有全局引用的插件被强制卸载。

两者都不是 OS 沙箱，安装前必须信任开发者。permissions 只限制通过宿主 API 进行的操作，不阻止插件自有 OS 操作。网络与文件权限必须在清单说明。

## 清单

稳定 id 是身份；数字 version 判断更新，仓库 owner/name 与 GitHub 一致。type=extension、contractVersion=2、runtime=process/managed、pageEntry 指向包内 Schema 2 JSON。managed 额外指定 extensionClass 和 in-process 权限；process 指定 process 权限。入口按 RID 声明，发行时每个 RID 一个 .impp。

requiredCapabilities 声明真正需要的协议，如 ui.tree.v2/state.v1/actions.v1/services.v1。初始化回报支持的能力；宿主缺少必需能力则拒绝启动，而不能猜测执行。可选能力采用协商与降级，不在旧字段上改变含义。

permissions 支持 navigation、player-control、music-read、library-write、settings-write、window-control、plugins-control、lyrics-write、user-files、ui-extend、plugin-services，以及 process/network/in-process。

## UI 树

schemaVersion=2，root 为通用节点，ownsHeader=true 时页面自己承担标题与标题行工具。类型包括 grid、stack、border、scroll、text、selectable-text、input、button、toggle、select、slider、image、repeat。没有脚本、动态 XAML、HTML、反射表达式。

节点支持 rows/columns、row/column、spacing、padding/margin、align、width/height、fontSize、背景资源名称。bind 是状态的点分路径；重复项用 item.xxx；visibleIf 是布尔状态路径。repeat 使用 template。input 将值写到 input 键，enterAction 拦截 Enter，Shift+Enter 保留换行。button 的 action 由插件自行定义，parameter 可读取 item 路径。图片 asset 只允许包内相对路径，文件应通过 --include 显式打包。

页面最多 128 KiB / 512 个静态节点 / 24 层；帧状态最多 2 MB，重复区单次最多 1000 项；插件应分页长数据。长任务只修改必要状态，宿主不重建整个页面，消息正文可使用 selectable-text 复制，容器无需选中。输入草稿随会话保留；页面离开仅解除 UI 订阅，不结束插件任务。

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

托管接口 INonetExtension 使用相同 DTO 的 InitializeAsync/InvokeAsync/SyncAsync/CompleteAsync/EventAsync/DisposeAsync，不依赖 Avalonia。业务代码不持有 UI 对象，使用状态渲染。源码夹具 tests/NonetMusicPlayer.ExtensionFixture 可用于阅读协议，不含任何私人插件。

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

首次 v2 已有方法、构造器、字段意义和默认值被冻结。后续采用新可选字段、独立可选接口和能力版本；不删除旧 v1/v2 适配。不得给 INonetExtension 添加必需成员，破坏已编译实现。运行 --extensions 回归会使用 artifacts/frozen-extension-v2 首次冻结的工作进程/程序集，不覆盖旧字节；另运行 --compatibility 验证 v1。

此层不等于可以在 JSON-RPC 中传每个音频采样，也不支持任意运行 XAML。实时 DSP、解码器等需要独立、版本化的低延迟音频接口，当前基线没有提供这些接口。桌宠可由浮层、图片、状态和播放器命令组成，但 OS 桌面注入不属于宿主权限。
