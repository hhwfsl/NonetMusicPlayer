# 通用扩展层 v2 / SDK 4.5

新版插件优先使用通用扩展，而不是请求宿主新增某个业务部件。最低宿主为 Nonet 0.4.0-beta.8。原 Contract v1 / 页面 Schema 1 / 旧 RPC 保留；LDDC 等旧插件通过原能力适配层运行，不按宿主发行版本淘汰插件。

SDK 4.1 / Nonet 0.4.0-beta.9 新增可选动态列宽与右键菜单，原基线保持不变，旧包无需重编译。未填作者时显示 Author；作者显示名与 GitHub 账户分离。

SDK 4.2 / Nonet 0.4.0-beta.10 新增可选原生勾选框、图标、左键菜单、主题化样式和用户文本输入服务，不改变旧接口。

SDK 4.3 增加可选审批、整体布局、声明式运行形态与新项目生成服务。旧 Contract 1/2 基线继续保留。

详见 [通用扩展开发](EXTENSIONS.md)。本文件下方保留 v1 接口参考，用于维护已有插件。插件是普通用户权限的软件，不是安全沙箱；JSON UI 白名单仅约束宿主 API，无法限制可执行插件自行访问 OS。

SDK 4.4 的完整非核心扩展入口、权限、原生视图、流程和音效规范见 [UNIVERSAL_EXTENSIONS](UNIVERSAL_EXTENSIONS.md)。旧插件无需重编译；新接口均为可选。

## 旧版接口参考

适用于 Nonet 桌面版及 CLI 0.4.0-beta.7，插件 SDK 3.5.0、清单 Contract v1、页面 Schema v1。本文只描述已实现的桌面及 CLI 接口。

## 兼容与更新约定

宿主发行版本与 Contract 版本独立。已经支持的 Contract v1、页面 Schema v1、旧字段默认值及 RPC 方法继续支持，不因播放器版本提高要求旧插件重新打包。新增功能优先新增可选能力；将来若引入新的协议版本，必须同时保留旧版本适配和冻结旧包回归。安全修复仍可拒绝本来就越权或不合法的包，不能以版本升级为由移除正常旧插件。

更新现有插件保持 `id`，只递增数字 `version`（如 1.0.0 → 1.0.1）。Nonet 桌面版 0.4.0-beta.3 起，在插件中心导入同 ID 新包并确认即原地更新，不先卸载：保留配置；权限集合不变时保留启用状态及既有音频写入授权，权限变化时停用并撤销该授权，需用户重新启用/确认。更新先验证旧配置对新规范及页面的兼容性，停止旧实例再替换；失败恢复旧文件和配置，成功删除临时旧包，不保留两份已安装插件。已注册插件的相同/更低版本及改变类型或 Contract 的同 ID 包拒绝覆盖。已解除注册但保留原目录的同版本包可重新接入并恢复非敏感配置，仍禁止降级。CLI 同样支持同 ID 高版本更新与原目录重新接入。

歌词搜索部件在 0.4.0-beta.3 起支持直接编辑预览，编辑区没有 tooltip。关联（含音频嵌入）与 UTF-8 下载统一使用点击操作时编辑区的文本；自动导入匹配和歌词菜单匹配仍遵循插件配置。清单、RPC 和 SDK 无变化，旧的歌词插件也可使用改进后的部件。

SDK 3.0.0 的程序集和 C# 命名空间由 LittleMusicPlayer.PluginSdk 改为 NonetMusicPlayer.PluginSdk，源码插件工具需更新引用后重新构建。声明式 `.impp` 的 Contract 和数据格式不变，原有 v1 插件包无需改后缀或重新打包。

清单、配置规范、页面约束、包验证和打包器位于独立 `NonetMusicPlayer.PluginSdk`，无 UI、音频或外部 NuGet 依赖。下载与进程通信仍位于 `NonetMusicPlayer.Core/Plugins`，桌面页面渲染位于 Desktop。为兼容既有调用，SDK 类型命名空间保留 `NonetMusicPlayer.Core.Plugins`。JSON Contract 保持版本 1，不因程序集移动改变插件包格式。CLI 支持音源进程插件及配置，不能启用需要图形页面、桌宠或主题的插件。CLI 数据与桌面数据独立。

页面动作 label 可以显式指定英文宿主资源 ID（如 `Common.Next`、`Playback.PlayPause`、`Home.Music`），跟随界面即时切换语言；自由文本保持原样，不再按中文原文匹配。插件作者应自行组织自定义文案。search 动作进入主页下的歌曲浏览，而不是主页本身。播放、喜欢、搜索等宿主动作同时进入内置终端结果流。

## 1. 工程与包

插件包使用 **.impp**（Nonet Plugin），内容为 ZIP。仅接受 .impp 后缀，其他后缀在读取包内容前拒绝。根目录必须有 manifest.json；最多 1000 个条目、解压后 256 MB。重复 / 大小写冲突路径、绝对路径、盘符、穿越、符号链接会拒绝。

插件模板 `NonetMusicPlayerPlugin` 与播放器仓库分开；开发依赖播放器源码中的 API，而不是模板内复制的 SDK。此组织方式借鉴 [AstrBot 开发指南](https://docs.astrbot.app/dev/star/plugin-new.html)：分别取得主项目和插件源码，在真实宿主中调试。Nonet 是 C#/.NET 项目，不加载 Python，也不改变已发布的 JSON Contract。

推荐目录（插件不会被主解决方案自动编译或发布）：

```text
NonetMusicPlayer/
  src/NonetMusicPlayer.PluginSdk/       API/DTO/验证器唯一来源
  tools/NonetMusicPlayer.PluginPackager/ 打包器唯一来源
NonetMusicPlayerPlugin/                 独立 Hello World 模板
MyNonetMusicPlayerPlugin/
  my-plugin/
    Directory.Build.props              NonetPlayerRoot / ProjectReference
    plugin/manifest.json                身份、仓库、版本、能力
    src/                               可选 C# 进程入口
    tests/                             专用宿主集成测试
    dist/                              只放最终 .impp
```

先克隆并构建主项目，再复制独立模板，或在已有插件的 props 中指定：

```xml
<NonetPlayerRoot Condition="'$(NonetPlayerRoot)' == ''">../NonetMusicPlayer</NonetPlayerRoot>
```

C# 插件工程通过 `ProjectReference Include="$(NonetPlayerRoot)/src/NonetMusicPlayer.PluginSdk/NonetMusicPlayer.PluginSdk.csproj"` 使用宿主的 `PluginManifest`、`RpcRequest`、歌词 DTO 等 API。不同目录结构用 `-p:NonetPlayerRoot=<主项目绝对路径>` 覆盖。SDK 无 UI/音频依赖，所以进程插件不必引用整套 Avalonia；依赖主项目源码不代表把主应用打包到插件里。

```text
dotnet run --project <主项目>/tools/NonetMusicPlayer.PluginPackager -c Release -- pack --source ./plugin --output ./dist/author.my-plugin.impp
dotnet run --project <主项目>/tools/NonetMusicPlayer.PluginPackager -c Release -- validate ./dist/author.my-plugin.impp
dotnet run --project <主项目>/src/NonetMusicPlayer.Desktop -c Release
```

在真实宿主的插件中心导入、启用、配置并检查生命周期；更新保持 ID、递增版本、重新导入。打包器保护旧输出，显式 `--force` 才替换自己构建的包；参数 `--include` 指定进程运行时文件。只分发必需的清单、页面、配置、许可及可执行入口，不分发播放器、SDK、测试、源码 ZIP 或个人数据。声明式 UI 插件只读取 JSON 部件，不能执行任意 C#/XAML；需要独立 C# 代码的音源/歌词插件使用受信任进程及 JSON-RPC，而不是绕过权限。

### 仓库与更新

新插件在源码的 `manifest.json` 声明 `repositoryOwner`（GitHub 账户）和 `repositoryName`（与仓库名完全相同，推荐 `nonet_plugin_<短名>`）。仓库尚未创建也提前确定此名称；`id` 是稳定唯一身份，不由文件夹、文件名或仓库名推导；`version` 是包内数字版本，如 1.2.0。仓库附件命名可变化，不影响身份和版本判断。

本地安装按两个属性请求 `https://api.github.com/repos/<owner>/<repositoryName>/releases/latest`；远程安装由宿主保存实际导入的仓库坐标，后续更新优先使用该来源。来源属于安装状态，插件包无法预设。稳定更新应发布正式 Release（非 draft/prerelease），标签使用与源代码清单 version 一致的数字版本（如 v1.0.3），附件按平台分别发布，禁止将多个平台运行时混装为新包。没有仓库、Release 或 .impp 附件时不修改已安装插件。

启动时后台读取轻量 Release 元数据，不下载插件；数字 tag 仅用于发现更新，在插件标题旁显示 New。点击 New 或卡片检查更新，显示只读且无 tooltip 的更新日志弹层，确认后关闭弹层并在右上角显示可取消进度。下载后仍以包内 ID/版本判断，不把 Release tag 或文件名当作插件身份。相同版本提示已安装，低版本禁止覆盖；高版本需用户确认，沿用同 ID 原子替换与权限检查。拖入或文件导入遵循同一规则；本地新包不会丢失原远程来源。没有仓库属性的旧 v1 包继续正常使用与本地更新，只是不能自动检查仓库；新打包器要求新开发插件声明仓库属性。不要将安装状态、配置、启用状态或写音频授权写入源码清单。

## 2. 清单

```json
{
  "id": "author.music-pet",
  "name": "音乐猫",
  "navigationLabel": "音乐猫",
  "author": "作者",
  "repositoryOwner": "YourGitHubAccount",
  "repositoryName": "nonet_plugin_music_pet",
  "version": "1.0.0",
  "contractVersion": 1,
  "type": "ui",
  "description": "控制播放、搜索和显示歌曲变化。",
  "permissions": ["player-control", "navigation", "desktop-widget"],
  "pageEntry": "page.json"
}
```

id 匹配 `^[a-z][a-z0-9.-]{2,80}$`，名称最多 100 字符，版本为数字形式 1.0.0。navigationLabel 默认名称，最多 60 字符；pageEntry 是安全相对 JSON 路径，最多 240 字符 / 8 层，不得使用 manifest.json。

类型：

| 类型 | 能力 | 权限与入口 |
| --- | --- | --- |
| provider | 服务器协议适配、曲库与流媒体 | network + process，按 RID 指定 entryPoints |
| theme | 主题颜色 Token | 无程序入口，tokens |
| widget | 兼容旧原生文本卡片，独立侧栏页面 | widgets 标题 / 文本数组 |
| ui | 原生页面、动作、统计、游戏、浮动桌宠 | pageEntry 与明确能力权限 |

安装后默认禁用。启用 provider 不立即运行，其首次读取曲库或播放时启动。页面插件启用后在独立侧栏栏目显示入口；桌宠可以在启用后显示浮窗。禁用、卸载、主机关闭均释放资源。

UI 包只允许 manifest.json、声明的 pageEntry、开发者配置规范，以及实际使用的目录项；不加载额外代码或资源。根目录可附带 LICENSE、LICENSE.txt、COPYING、NOTICE、THIRD_PARTY_NOTICES.txt，每份必须是 256 KiB 以内的 UTF-8 文本，不能包含空字符；这些文件不会作为代码运行。打包器自动保留存在的许可文件。UI 清单禁止 entryPoints、tokens 和旧 widgets。它只接受 player-control、navigation、statistics、desktop-widget 权限，不接受 process、network、filesystem。

## 3. 页面部件

```json
{
  "schemaVersion": 1,
  "title": "音乐工具",
  "description": "原生操作页面",
  "widgets": [
    {"type": "text", "title": "说明", "text": "使用按钮控制播放器。"},
    {
      "type": "actions",
      "title": "播放",
      "actions": [
        {"label": "上一首", "action": {"kind": "previous"}},
        {"label": "播放 / 暂停", "action": {"kind": "play-pause"}},
        {"label": "下一首", "action": {"kind": "next"}},
        {"label": "搜索", "action": {"kind": "search", "value": "music"}}
      ]
    },
    {"type": "listening-summary", "title": "聆听"}
  ]
}
```

页面最多 32 KiB、深度 12、1–12 个部件；标题 / 描述最多 100 / 2000 字符，每个部件标题 / 文本最多 100 / 4000 字符。未知字段和重复字段拒绝，不执行表达式、任意绑定、脚本、HTML、XAML 或 DLL。

- text：标题和文本。
- actions：1–8 个用户触发的按钮，每个 label 最多 60 字符。
- listening-summary：本地今日分钟、累计小时、次数；需要 statistics，刷新间隔 5 秒，切页后停止。
- snake：宿主定义的贪吃蛇规则，每页最多一个。columns 12–40、rows 10–30、tickMilliseconds 80–600；默认 24/18/150，wrapWalls 默认 false。background/snake/food 为 #RRGGBB。
- pet：每插件最多一个原生矢量音乐猫，可组合主机动作、眨眼动画与消息流程。需要 desktop-widget；name 最多 60 字符，body/accent 为 #RRGGBB，floating 默认 true，actions 1–8 项。不是外部脚本角色或可执行图像；当前不加载外部位图、SVG 或任意动画算法。

游戏页面离开后销毁，键盘域不会触发播放器快捷键；浮动桌宠可继续运行。桌宠动画 400 ms 一帧，最多同时 8 个浮窗。关闭单个浮窗只隐藏该实例；重新停用再启用可重新打开。

## 4. 主机动作与流程

| kind | value | 权限 |
| --- | --- | --- |
| play-pause | 无 | player-control |
| previous / next | 无 | player-control |
| favorite | 无，切换当前曲目喜欢状态 | player-control |
| navigate | library / albums / artists / statistics / settings / lyrics / playlist:liked | navigation |
| search | 最多 200 字符，打开音乐库并设置查询 | navigation |
| show-message | 最多 200 字符，仅流程使用 | desktop-widget |

动作不接受控制字符，不提供 shell、反射、任意文件路径、网络请求或任意 VM 方法。按钮必须由用户操作；已停用或卸载的插件不能执行动作。

桌宠流程示例：

```json
{
  "flows": [
    {
      "event": "track-changed",
      "action": {"kind": "show-message", "value": "正在播放：{title} · {artist}"}
    },
    {
      "event": "interval",
      "intervalSeconds": 120,
      "action": {"kind": "show-message", "value": "当前歌曲：{title}"}
    }
  ]
}
```

flows 位于页面根对象，最多 4 条且需要 pet 部件。track-changed 接收曲目变化，interval 为 60–3600 秒。自动流程仅支持 show-message，不自动切歌、搜索或启动程序，以免出现事件反馈循环。消息只替换 {title} 与 {artist} 两种占位符，不解释代码。主机停用 / 卸载时取消所有计时与订阅。

此 Contract 可调整所提供的 UI 和交互流程，但不能任意改写主机内部 UI 树、替换播放算法或注入任意代码。需要新能力时，应先扩展宿主受约束组件和权限，再提升相应接口版本。

## 5. 主题与兼容卡片

theme 的 tokens 只允许 Accent、Background、Surface、Text，值为 #RRGGBB。启用后应用，停用后恢复用户主题。旧 widget 的 widgets 数组保存 title/text，每项最多 100/4000 字符；内容现在展示为独立插件页，不混在插件管理中心。

用户手写布局是另一个协议：设置中的 JSON 布局定义必要区域和控件，详见 [UI_LAYOUT.md](UI_LAYOUT.md)。页面 Contract 不接受布局绑定，也不能绕过必要控件校验。

## 6. 音源进程与传输

provider 示例：

```json
{
  "id": "author.server-adapter",
  "name": "服务器适配器",
  "author": "作者",
  "version": "1.0.0",
  "contractVersion": 1,
  "type": "provider",
  "description": "适配已有音乐服务器。",
  "permissions": ["network", "process"],
  "entryPoints": {"win-x64": "Provider.exe"}
}
```

入口按 .NET RID 指定：win-x64、osx-x64、linux-x64。路径必须位于包内，入口文件必须存在。可以分别打包或包含实际支持的平台入口。

宿主直接启动入口，无 shell 拼接，工作目录为插件目录。stdin/stdout 是 UTF-8 逐行 JSON-RPC 2.0，每行一个完整消息。stdout 只写协议，stderr 写日志；宿主不持久化 stderr。单请求超时 20 秒，响应行最多 8 MB，当前请求串行。响应匹配 jsonrpc 和 id；主动推送通知未实现。

params 字段均为字符串，configuration 也是 JSON 字符串。

### initialize

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"contractVersion":"1","hostVersion":"0.2.0","configuration":"{\"catalogUrl\":\"https://music.example/api/tracks\"}"}}
{"jsonrpc":"2.0","id":1,"result":{"contractVersion":1,"capabilities":["catalog","loopback-http"]}}
```

插件负责配置、认证和协议适配，主机验证 contractVersion=1。主机根据统一配置规范生成应用内表单，不加载插件提供的 XAML 或任意界面代码；规范见第 10 节。

### catalog.list

请求 params 为 {"cursor":""}，后续传入 nextCursor。返回：

```json
{
  "jsonrpc": "2.0", "id": 2,
  "result": {
    "tracks": [{
      "id": "server-track-id", "title": "歌曲名",
      "artist": "艺术家", "album": "专辑",
      "durationSeconds": 220.5, "format": "flac",
      "coverBase64": "可选图片字节的Base64",
      "lyrics": "[00:01.00]可选歌词"
    }],
    "nextCursor": null
  }
}
```

协议实际传输必须单行。每页最多 1000 首，完整曲库最多 50000 首，游标必须前进。id 最多 300 字符、title 最多 1000 字符且不能为空；稳定曲目标识为 pluginId:trackId。coverBase64 最长 2000000 字符，lyrics 最长 100000 字符，分别写入托管封面与歌词目录。不要返回带凭据的远程封面 URL。

### playback.resolve

请求 params 为 {"trackId":"server-track-id"}。返回：

```json
{"jsonrpc":"2.0","id":3,"result":{"kind":"loopback-http","url":"http://127.0.0.1:45678/audio?token=至少32位十六进制会话令牌&track=server-track-id"}}
```

只接受 http + 127.0.0.1 / ::1 的 IP 字面地址，拒绝 localhost、DNS、远程地址、URL 用户信息和重定向。token 至少 32 位十六进制，随机不少于 128 bit，每个进程会话更换。插件绑定 loopback，验证令牌，只代理已公开曲目，不做任意 URL 代理。

音频接口：

- HEAD：200，准确 Content-Length，Accept-Ranges: bytes。
- GET 无 Range：200，音频字节。
- GET 单 Range：206，准确 Content-Range 与 Content-Length。
- 越界：416，Content-Range: bytes */total。
- 无效令牌 403，未知曲目 404。

流必须长度固定、可定位。客户端按需读取，单块最多 256 KiB，不先下载整首 PCM。插件适配上游认证、Range 和超时；无长度的直播、HLS、DRM、provider-stream 未实现。退出时必须停止本地服务。

失败响应：

```json
{"jsonrpc":"2.0","id":3,"error":{"code":-32000,"message":"Provider operation failed"}}
```

不得返回密码、令牌、授权头或敏感上游 URL。主机展示通用错误，不直接显示插件错误详情。

## 7. 示例服务器配置

音源适配器是独立进程，可以使用 .NET 10、Rust、Go 等语言，只需遵守协议。它应在自己的项目中维护，不属于播放器解决方案。

```json
{
  "catalogUrl": "https://existing-server.example/api/tracks",
  "streamUrlTemplate": "https://existing-server.example/api/tracks/{trackId}/audio",
  "authorization": "Bearer TOKEN"
}
```

示例 URL 不是服务器规范要求。修改插件中的字段映射和请求构造即可适配已有接口。上游音频需支持固定长度 / Range，或由插件实现对应可定位代理。示例 HTTPS 校验仅允许 loopback 的 HTTP 测试，不跟随重定向。

无服务器测试可用 {"fixtureFolder":"D:/Music/TestFixtures"}，只读取指定目录、不递归、最多 1000 首；该示例因此另声明 filesystem。正式适配器不需要本地扫描时应移除这项代码和权限。

## 8. 安全与生命周期

独立进程 provider 是本机程序，权限声明 **不是操作系统沙箱**，没有签名认证；仅安装可信作者。声明式 UI 通过不提供代码 / 外部资源执行降低风险，也不保证抵御拥有本机写权限的其他软件。

完整配置在当前会话传给 provider。持久化时递归剔除字段名中的 token/password/secret/authorization/cookie/credential/api-key，以及规范标记 sensitive 或 password 控件的字段；非 Agent 插件的这些值重启后需重新输入。Agent 桌面插件完整配置采用账户加密 JSON 持久保存，详见第 15 节。不要将凭据藏在普通字段或 URL 查询中。

禁用音源停止其进程并使其歌曲不可用。卸载前先禁用，并询问是否删除插件文件：默认仅解除装载注册，文件留在 `Plugins/<id>` 原位置，非敏感配置与实际仓库来源保留在原清单，同 ID 同版本重新导入即可恢复接入；选择删除则直接删除已验证的原插件目录，不可撤销，不生成 Retained，也不删除音乐源文件。声明式页销毁会停止游戏 / 统计计时器，桌宠停用 / 卸载 / 退出会关闭窗口、动画和消息订阅。扩展原生组件前必须定义数量、体积、计时频率和销毁边界。

### 可选音源生命周期回调

provider 在 manifest.json 声明 `"lifecycleMethods": ["lifecycle.disable", "lifecycle.uninstall", "lifecycle.shutdown"]`，未声明的回调不发送，旧插件无需改动。插件实现同名 JSON-RPC 请求，params 为空字典，成功返回任意有效 result（示例返回 `{"ok":true}`）。

- `lifecycle.disable`：禁用或配置重建前，通知正在运行的进程释放服务和订阅。
- `lifecycle.shutdown`：宿主退出时，在终止进程前通知释放资源。
- `lifecycle.uninstall`：禁用完成后，用新的短生命周期进程发送，再移除或保留文件。该方法必须在未调用 initialize 时也可执行，不依赖已丢失的会话密钥；清理插件自身持久资源应幂等。

每次回调最多等待 2 秒。异常、超时仍关闭进程并完成宿主清理，插件不能阻止退出或无限拖延卸载。声明式 UI 没有任意代码的“析构函数”，其控件、计时器、事件和浮窗由宿主 Dispose；提供新可执行 UI 能力前需另行设计安全接口。示例音源已实现三种回调，并在自己的目录写入不含凭据的 `lifecycle.events.jsonl`，便于检查回调顺序。

## 9. 回归检查

```powershell
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/plugin-ui-checks
```

测试使用隔离目录，不安装到真实用户数据。覆盖旧包兼容、默认禁用、严格页面字段、非硬编码插件 ID、游戏键盘与生命周期、主机切歌 / 喜欢 / 搜索、桌宠消息、同排开关与卸载、权限及自动流程限制。

## 10. 统一配置规范

插件根目录使用固定文件名 **plugin_config_schema.json**；也接受扩展名为空的 **plugin_config_schema**（内容仍为 JSON），两者不能同时提供。本版统一选择 JSON，不解析 YAML。该结构兼容常用 AstrBot 风格字段描述，不宣称支持其全部扩展组件。

```json
{
  "enabled": { "description": "启用功能", "type": "bool", "default": true },
  "mode": {
    "description": "模式", "hint": "选择插件的工作方式。", "type": "string",
    "enum": [{ "value": "normal", "label": "标准" }, { "value": "quiet", "label": "安静" }],
    "default": "normal"
  },
  "interval": { "description": "间隔（毫秒）", "type": "int", "min": 80, "max": 400, "default": 150 },
  "names": { "description": "名称列表", "type": "list", "items": { "type": "string" }, "default": [] },
  "options": {
    "description": "附加设置", "type": "object", "items": {
      "title": { "description": "标题", "type": "string", "default": "播放器" }
    }
  },
  "authorization": { "description": "认证", "type": "string", "ui:widget": "password", "sensitive": true, "default": "" }
}
```

- type 支持 string、text（多行）、bool、int、float / number、list、object。
- description 为标签，hint 为辅助说明；enum 可以是原始值数组或 value / label 对象数组。
- default 为默认值，required、min / max、max_length 用于校验。建议显式提供合法默认值；缺省使用对应类型的空值。
- object.items 是开发者定义的子字段描述，用户不能增删或改名字段；空 items 显示“无配置”。list.items 描述单个元素，省略时新增元素为字符串；用户可按开发者声明的列表类型填写列表值。
- ui:widget=password 隐藏输入内容；sensitive=true 和密码字段不进入插件索引；非 Agent 插件仅会话保留，Agent 使用加密配置存储。勿将真实密钥放入默认值、页面或发布包。
- 规范 / 配置上限 256 KiB、描述字段最多 200、嵌套最多 8 层、列表和对象最多 200 项。字段名为 ASCII 字母 / 下划线开头，可含数字、点和短横线，最长 100 字符。

齿轮打开带滚动区域和固定底部操作的应用内弹层。没有配置规范或规范为空时仅显示“无配置”和关闭，不提供通用 JSON/字段结构编辑器。表单结构只能由开发者随插件提供的 Schema 改变。文本输入回车或失焦更新草稿，布尔 / 枚举更新草稿；保存时整体验证，关闭取消。保存成功后已启用页面 / 桌宠立即重建，音源旧进程关闭，在下一次请求中传入新配置；当前播放音源修改前先停止该音源播放。不重新启动应用。

声明式 page.json 可以使用 `${config.key}`。整值占位保留 JSON 数字 / 布尔类型；嵌入文本占位仅作字面替换，不执行表达式。替换后仍执行原有页面、颜色、动作权限和体积校验。非法替换不覆盖原配置。示例：`"tickMilliseconds": "${config.interval}"`。小游戏和桌宠页面可用同样的配置规范动态调整受支持的参数。

## 11. GitHub Release 分发

支持公开 HTTPS 链接：`https://github.com/作者/仓库/releases/tag/标签`、`.../releases/latest`、`.../releases/download/标签/插件.impp`。不接受仓库首页、源码 archive、任意外部下载地址、私有仓库凭据或 URL 查询认证。

主机通过 GitHub Release API 读取更新日志、版本标签及 .impp 附件，精确匹配当前系统/进程 RID，不退回其他平台。下载最多 128 MiB，限制跳转为 GitHub 及其附件域名，检查声明长度；若 Release 提供 SHA-256 digest 则校验。临时包在受管 Plugins/.downloads 中，完成、取消或失败后删除。下载完成仍需确认清单和权限，安装后默认禁用。传输校验不是作者身份认证或沙箱。

获取和下载进度由主窗口内可取消的提示框显示；没有 HTTP Content-Length 时使用 Release 附件的 size，均未知时显示等待动画。下载和校验成功后，先移除进度框，再显示约 4 秒后自动关闭的完成提醒。插件开发者无需实现下载提示界面。

离线文件选择和插件页拖拽走相同检查流程。开发测试使用模拟 HTTP 响应，不需要上传真实 Release。

## 12. 源码执行边界

当前声明式插件可直接分发 JSON 页面而无需编译，但不是通用脚本。独立进程音源必须提供当前平台可执行入口。

源码插件理论上可通过专门的 Python / JavaScript 运行时实现，但需要额外处理解释器、依赖安装、版本隔离、主机 API、更新验证及安全边界；仓库内容本身不能凭空执行。C# / Avalonia 源码需要构建为程序集或使用专门编译服务，不能按 Python 导入语义直接运行。本版不自动拉取、编译或执行仓库源码。

## 13. 菜单贡献与歌词编辑（0.3.0-beta.3）

UI 插件的 manifest 可声明 `menuContributions`（最多 8 项）。当前白名单位置为 `lyrics.more`，动作 `open-page` 打开本插件的页面。必须声明 `navigation` 权限；启用状态在打开菜单时实时读取，禁用后不再贡献菜单项。无插件 ID 硬编码，不接受任意 XAML、脚本或对其他控件的注入。

```json
{
  "menuContributions": [
    { "location": "lyrics.more", "label": "精准歌词工具", "action": "open-page" }
  ]
}
```

页面部件 `lyrics-timing` 需要同时声明 `lyrics-editor` 和 `player-control` 权限，每页最多一个。主机负责文件选择、音频时钟、键盘隔离和 LRC 写入；插件不获取任意文件写入或全局键盘监听权限。标注期间取得受生命周期约束的播放租约：只能播放选定歌曲，模式和切歌被拒绝，自然曲终暂停；仍可暂停、继续和调整本曲进度。

部件默认选择当前歌曲，开始前允许改选音乐库、最近临时曲目中的歌曲。选歌不立即播放，开始后选歌控件被锁定。空格由主窗口优先路由，焦点移到播放栏或列表仍能标注；不注册其他应用中的全局热键。

```json
{ "type": "lyrics-timing", "title": "逐行时间标注" }
```

首行固定为 0，后续空格记录音频后端时钟（不是界面采样时钟）；保留到毫秒。源文件限 2 MB、1–5000 个非空文本行，每行最多 4000 字符。忽略元数据、空行和原时间标签，按原文本行顺序标注，不猜测原文/译文语言。提供撤销，上一个时间戳之后才能继续标注。

保存必须完成所有行；写入受管歌词文件夹，覆盖现有关联前由用户确认，原始输入文件不修改。取消、页面卸载、禁用或卸载插件释放租约并暂停，不保存未完成内容，恢复原队列/模式。插件只能调用主机已提供的部件和动作；新增贡献位置或业务部件需通过后续 Contract 扩展，不能任意接管软件流程。

开发时间标注插件时，复制基础工程的 Hello World 清单和页面，声明上述权限、菜单贡献与 `lyrics-timing` 部件，再用共用打包器生成独立项目 `dist` 内的包。播放器仓库不分发具体工具插件，不自动安装至用户数据。

## 14. 歌词来源插件（SDK 3.1 / 桌面 0.4.0-beta.2）

`type: "lyrics"` 为受信任的独立进程扩展，不是声明式 UI 插件。必须声明 `network`、`process`、`lyrics-search` 权限；可选 `navigation`（贡献歌词菜单）和 `audio-tags`（请求宿主写入标签）。原生进程可拥有操作系统级能力，并非安全沙箱，只安装可信插件。CLI 当前不启用此需要桌面交互与音频源文件确认的类型。

从基础工程的 `plugin` 模板创建清单与配置，加入各运行平台的可执行 `entryPoints`，页面使用一个 `lyrics-search` 原生部件。共用主项目的打包器；进程运行时文件通过 `--include` 显式加入。可声明 `lifecycle.disable`、`lifecycle.uninstall`、`lifecycle.shutdown`，通知最多等两秒再释放进程。

标准输入输出采用音源插件同款单行 JSON-RPC 2.0；所有 params 值仍为字符串，内部对象用 JSON 字符串传入。协议 DTO 为 SDK 的 `LyricsQuery`、`LyricsCandidate`、`LyricsSearchResult`、`LyricsFetchResult`，宿主用 `LyricsPluginJson` 的源生成元数据序列化。仅传入标题、艺术家、专辑、时长，不向进程提供源音频路径。包根目录的 UTF-8 许可文本仍限 256 KiB，跨平台运行时通知也须遵守该限制。

| 方法 | params | result |
| --- | --- | --- |
| `initialize` | contractVersion、hostVersion、configuration | `{ "contractVersion": 1 }` |
| `lyrics.search` | `query`：序列化 LyricsQuery | `{ "candidates": [...], "warnings": [...] }` |
| `lyrics.fetch` | `candidate`：候选 JSON；`format`：line / word | `{ "text": "...", "format": "lrc", "source": "kugou", "wordTimed": true, "warning": "" }` |
| `lyrics.auto` | `query`：序列化 LyricsQuery | 同上；没有可靠匹配时 text 为空，不能把错误或无关文本当歌词 |

歌词最大 2 MB，非空结果必须有可解析的时间行。逐字输出采用 Enhanced LRC `<mm:ss.mmm>` 或播放器支持的方括号内联时间戳；无逐字数据时只能回退真实逐行时间，不能伪造时间。请求总限时 50 秒，失败进程在下一次调用可重建；来源失败应当独立隔离。

标准配置约定（所有字段也需开发者声明到 `plugin_config_schema.json`）：`autoFetch` 为导入缺失歌词的开关（默认 true），`embedLyrics` 默认 false。插件可自行增加来源优先级和格式配置。宿主在导入提交后异步调用，检查外部/内嵌歌词是否存在，结果回来后再次确认歌曲仍有效且用户尚未手动关联。

`embedLyrics` 从关闭变开启时必须在宿主配置表单确认；无确认的终端配置调用拒绝该修改。授权不由包内容提供，安装器清除包内的 `audioTagWriteConsent`；关闭嵌入或卸载会撤销。宿主写标签前重新验证授权，停止插件或迁移数据会取消待提交写入。

嵌入仅支持经 TagLib 校验可读回歌词的 MP3、FLAC、M4A/M4B、OGG/OPUS、APE、WMA。先完整备份源文件到数据备份目录 AudioTags，再在音频旁路副本写标签，读回一致才原子替换；文件只读、被独占或发生变化时不覆盖。不另外创建歌词文件，旧的受管歌词副本可删除以使用内嵌歌词；用户原始外部歌词从不删除。用户主动点击下载文件仍可另存一份歌词，不属于自动关联策略。

菜单可以声明 `{ "location":"lyrics.more", "label":"用LDDC匹配歌词", "action":"match-lyrics" }`。仅歌词插件支持该动作，为当前曲目立即执行 `lyrics.auto` 并遵循配置关联，不导航离开歌词页；失败不改已有歌词。`open-page` 仍可打开手动搜索工具。

歌词搜索的实现应在独立插件项目中维护，使用 Hello World 模板、SDK DTO 和共用打包器；源码、专用测试及发行包不放入播放器仓库。参考第三方实现时，须保留原许可证、上游版权和对应源码，不因歌词文本清理删除软件许可。

## 15. 独立平台包（SDK 3.3）

源码清单可保留各 RID 的 entryPoints；通过共享打包器分别生成发行包，进程插件必须指定 --rid。输出清单只包含该 RID 入口并新增可选的 platform 属性。清单与页面 Contract 仍为 1，旧包省略此属性仍兼容。

```powershell
dotnet run --project <宿主>/tools/NonetMusicPlayer.PluginPackager -c Release -- pack --source ./plugin --output ./dist/author.my-plugin-1.0.0-win-x64.impp --rid win-x64 --include bin/win-x64/worker.exe
dotnet run --project <宿主>/tools/NonetMusicPlayer.PluginPackager -c Release -- pack --source ./plugin --output ./dist/author.my-plugin-1.0.0-osx-x64.impp --rid osx-x64 --include bin/osx-x64/worker
dotnet run --project <宿主>/tools/NonetMusicPlayer.PluginPackager -c Release -- pack --source ./plugin --output ./dist/author.my-plugin-1.0.0-linux-x64.impp --rid linux-x64 --include bin/linux-x64/worker
```

发行命名为 <id>-<version>-<rid>.impp；同一 Release tag 为 v<version>，三个附件的稳定 ID、数字版本和权限保持一致。此名称只用于平台附件选择；身份和版本仍来自包内源码清单。未来 ARM 包须使用实际构建的 RID，不得把 x64 包改名为 arm64。

构建阶段仅显式加入所需运行文件；原生依赖放在相应 RID 子目录（如 bin/win-x64），共享配置、页面、许可保留在根目录。声明式 UI/主题包也可由 --rid 生成三个独立附件。未指定 RID 的旧平台无关包仍可安装。

宿主安装前检查 platform / entryPoints，安装错误平台的包不会停止或覆盖旧实例。旧多平台包不强制重打包：提取本机入口与共享资源，剔除已知其他 RID 目录/入口。升级到本版也会清理已安装旧包中的明确异平台文件，不删除配置或共享许可。

更新高版本包保持原 ID，不先卸载；下载取消、校验失败或版本/身份不符不改变旧插件。首次远程导入仍需审查作者、权限，原地更新若增加权限会再次确认并停用新实例。后台检查没有仓库或没有当前平台附件时不显示 New。

## 16. Native AOT 进程插件

Native AOT 是进程入口的发布方式，不是新的插件类型或 Contract。宿主仍直接启动 entryPoints 的本机可执行文件，使用原有 UTF-8 单行 JSON-RPC；不要将原生入口作为 DLL 加载到主进程。旧 JIT、自包含进程包继续兼容，插件更新仍保持 id、递增数字 version，并保留原配置及权限规则。

适合网络协议适配、歌词解析等依赖较少的进程。在 C# 入口工程中开启 PublishAot，并使用明确的 JsonSerializerContext / JsonTypeInfo 重载序列化协议类型；只设置 JsonSerializerOptions 并不足以消除 AOT 分析器警告。匿名响应、object 参数与动态类型发现应改为已注册 DTO 或显式 JSON 节点。关闭反射式 JSON 后测试初始化、正常结果、失败响应和所有声明的生命周期回调，不能通过压制裁剪警告掩盖问题。

构建需要目标系统的原生工具链：Windows 的 Visual Studio C++ 桌面开发工作负载、Linux 的 clang/链接器/zlib 开发库、macOS 的 Xcode 命令行工具。Native AOT 不支持普通的跨操作系统编译，Windows/Linux/macOS 包应在各自环境构建；没有构建成功的平台不提供伪装或改名的包。详见 [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) 和 [跨平台编译限制](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile)。

只将原生入口、所需原生依赖及许可加入对应 RID 的 impp；PDB、dbg、dSYM 等调试符号留在开发资产目录。运行时和第三方许可仍须保留。最终包必须用实际宿主版本验证；原生化不等于沙箱，也不免除音频写入授权和生命周期释放要求。Native AOT 仍包含托管堆、GC 和必要运行时，资源收益需用相同任务实测，不能仅凭可执行文件大小判断内存占用。

## 15. 可选 Agent 能力（SDK 3.4）

桌面宿主 0.4.0-beta.6 新增 type=agent，稳定 Contract 和页面 Schema 仍为 v1。已有插件类型/字段/RPC 保持兼容；只有使用本可选能力的新插件需要新宿主。CLI 拒绝启用需要图形聊天页的 Agent，不改变旧音源插件行为。

清单需 network、process、agent-control，可附加 navigation；提供 pageEntry 和按 RID 的 entryPoints。页面 widgets 中声明 agent-chat，宿主绘制聊天框。生命周期沿用 lifecycle.disable/uninstall/shutdown，最多两秒。进程不是系统沙箱，只安装可信插件。

initialize 返回 contractVersion=1。agent.step 的 params.turn 是源生成 AgentTurn JSON 字符串，包含 messages 和 tools；结果是 AgentReply，含 text 和 calls。SDK 3.5 新增可选 reasoning、inputTokens、outputTokens 和 finishReason；旧响应不需要这些字段。AgentMessage 可携带可选 reasoning 用于兼容思考模型的工具往返。每个 AgentToolCall 为 id、operation 和字符串数组 arguments。DTO 与校验位于 AgentPluginContract.cs，开发进程应直接引用主项目 SDK，禁止复制协议。

模型执行不进入进程。进程只提出调用，宿主 AgentCommandPolicy 检查白名单、参数、引用 ID，再经共享命令路由执行。删除、移除、设置修改、插件启停和窗口关闭须用户确认；模型参数不能传入确认选项。任意文件操作、插件安装/配置、系统 Shell、数据恢复等不提供。拒绝的操作在本轮不重复确认；停用/卸载/离页后取消请求并禁止后续动作。

首次发送前显示配置接口及发送确认。查询结果剔除路径、目录、凭据字段，最多 100 项；回复有界，不持久化聊天。apiKey 用 sensitive/password 配置字段，不进入明文插件索引；SDK 3.5 宿主将 Agent 完整配置写入 Plugins/Configurations/<id>.json，公开参数在 values，加密完整配置在 protectedConfiguration。Windows 使用当前账户 DPAPI；Linux/macOS 使用 AES-GCM 和仅该用户可读的 .key。跨账户/平台迁移需重新输入密钥；Unix 密钥与配置同时泄露时不能保证保密。插件升级、断开连接及重启保留配置；勾选删除插件文件时删除该配置。默认允许 HTTPS 或本机 HTTP，不跟随重定向，不关闭证书校验。

每回合最多八轮，每回复最多四个工具；消息总长度和响应长度由 SDK/宿主/进程分别限制。模型误判或用户取消不会撤销已完成业务动作。插件应明确说明元数据发送范围、模型费用、平台运行时方式与最低宿主能力，不把提示词当作权限边界。

Native AOT 使用源生成 AgentPluginJson。应在目标系统构建 AOT；无原生构建环境时可以单独发布明确标记的 self-contained 包，不能称其为 AOT。平台包继续使用一个 RID 一个 impp。

### 15.1 模型发现与配置控件（SDK 3.5，可选）

使用新控件的插件需要 Nonet 0.4.0-beta.7 或更新版；旧插件及仅使用 SDK 3.4 Agent 基本能力的包仍可安装，不按应用版本淘汰。

- 配置 enum 选项可声明 defaults 对象。选择该项将更新 Schema 已声明的字段；不能声明未知配置键、脚本或表达式。改变提供商应清空旧密钥，避免跨站鉴权。
- ui:group 给字段分组，ui:collapsed=true 使所属组默认收起。无此元数据的旧 Schema 表单布局保持不变。
- string 字段 ui:widget=agent-model 提供手工模型 ID、获取模型、列表选择和取消。仅 agent 插件支持；获取前宿主展示草稿接口和鉴权发送确认，不发送聊天。
- text 字段 ui:widget=json-object 在应用时要求内容为 JSON 对象，仍只是配置数据。
- agent.models 无参数，返回 {"models":["model-id", "..."]}。宿主使用当前表单草稿启动短生命周期进程并 initialize，完成或取消后销毁，不覆盖持久配置。最多 1000 个 ID，每个 200 字符。
- agent.step 不再施加固定 150 秒 RPC 期限，由插件设置请求超时；用户取消、停用、卸载、离页仍终止进程。限额是插件业务配置，不由此宿主接口规定。新插件可采用 -1 表示无客户端限制、0 表示零值；需在规范和请求实现中保持一致，服务端仍有自身限制。
- 聊天视图使用只读可复制消息卡片、可展开工具结果/思考信息、固定输入区。聊天不保存磁盘；额外请求参数不得改变宿主的权限白名单。

兼容性验证应覆盖冻结旧包、无可选字段的旧 AgentReply、Schema 无分组/预设时的原控件、初始化/取消/退出、新 manager 的配置恢复，以及日志/安装索引不含明文凭据。

SDK 4.5 新增包内图片图标、复合输入区、用户交互与内联审批、独立原生浮层，详见 UNIVERSAL_EXTENSIONS；插件仍按能力协商，不按应用版本淘汰。
