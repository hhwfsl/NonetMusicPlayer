# 完整界面方案

SDK 4.15，能力 `ui.composition.v1`。它扩展既有 `styles` 服务，不改变 Contract v1/v2、页面 Schema 或 SDK 程序集基线 4.0.0.0。

## 1. 运行模型

界面方案沿用 `style.json`：配色、类型级样式和动画定义不变，新增可选 `layout`。只有风格的旧文件继续有效。文件是有界数据，不加载脚本、XAML、程序集、网络资源或任意本机路径。

插件负责导入、保存和排序，通过 `styles/apply` 一次提交全部已启用层，顺序为从底到顶。相同控件属性、设置键以及区域由后面的层覆盖；没有冲突的定义共存。区域是一棵完整树，不对两个区域树做隐式拼接。动画参数与歌词颜色逐字段叠加，省略字段保留下层定义。

所有更改属于调用插件的运行期租约。停用、卸载、故障、关闭窗口会撤销；导航和窗口变化先还原，再定位新页面的稳定控件。原控件的事件、数据绑定、快捷键和列表虚拟化继续由宿主管理。Ctrl+Shift+Esc 保留为恢复入口。

## 2. 插件接入

清单声明 `ui-extend`、`ui.styles.v1`、`ui.composition.v1`。如需启动时恢复，再声明 `autoStart:true` 与 `runtime.startup.v1`。托管/进程/AOT 扩展使用相同数据服务，不需要引用桌面程序集。

```csharp
using NonetMusicPlayer.Core.Plugins;
using System.Text.Json;
using System.Text.Json.Nodes;

using var input = File.OpenRead(userSelectedFile);
var preset = StyleSheetContract.Read(input);
var response = await context.RequestFromUserAsync("styles", new JsonObject
{
    ["operation"] = "apply",
    ["layers"] = JsonSerializer.SerializeToNode(new List<StyleSheet> { preset },
        StyleSheetJson.Default.ListStyleSheet)
});
```

`styles` 的 `composition-catalog` 操作返回可接管设置、表现组件、保护对象和已有控件目录；既有 `catalog`、`reset`、`morph` 不变。进程扩展通过 `ExtensionFrame.Requests` 发送等价参数，并在 `CompleteAsync` 移除已完成请求。

## 3. 最小文件

```json
{
  "schemaVersion": 1,
  "id": "example.interface",
  "name": "Simple Interface",
  "version": "1.0.0",
  "layout": {
    "settings": { "navigationRight": false, "playerTop": false },
    "regions": {
      "SidebarPanel": {
        "kind": "stack",
        "properties": { "spacing": 4, "margin": "12" },
        "children": [
          { "kind": "navigate", "page": "library", "label": "Home.Music", "icon": "Home" },
          { "kind": "navigate", "page": "view:playlists", "label": "Playlists.Playlists", "icon": "Playlist" },
          { "kind": "navigate", "page": "settings", "label": "Common.Settings", "icon": "Settings" }
        ]
      }
    },
    "pages": {
      "playlists": { "kind": "playlists", "label": "Playlists.Playlists" }
    }
  }
}
```

这只重组现有入口和数据，不创建新的音乐、扫描服务或业务命令。应保留常用功能入口；新建歌单可用 `action` 组件复用 `playlist.create`。不能将界面文件当成功能插件执行。

## 4. 设置临时接管

`layout.settings` 每个键同时表示“禁用对应用户编辑器”和“界面运行时使用此方案的值”。用户保存的值不被替换，设置页仍显示原值，并说明当前由哪个方案接管。关闭方案后解除禁用，原值立即生效。拖动调整被接管的区域尺寸也会禁用。

可用键：`navigationRight`、`playerTop`、`sidebarWidth`、`playerHeight`、`myMusicExpanded`、`pluginsExpanded`、`playlistsExpanded`、`controlCornerRadius`、`fontSize`、`theme`、`accent`、`accentFromBackground`、`uiOpacity`、`titleBarOpacity`、`navigationOpacity`、`contentOpacity`、`playerOpacity`、`backgroundImageOpacity`、`backgroundImageStretch`、`titleButtonsOnLeft`。

仅允许视觉参数，不接受音量、音频设备、网络授权、数据路径、文件权限或插件审批。布尔键必须是 JSON 布尔值；尺寸、透明度和颜色按 SDK 校验范围填写。未声明的键不受方案控制。用户通过终端改变原偏好不会改变方案的有效值；这些偏好在关闭方案后生效。

## 5. 控件树

`regions` 的键是稳定控件名，树替换该容器的内容。可从目录发现命名控件；尚未创建的页面区域延迟至导航时组装。常用锚点包括 `ShellLayout`、`SidebarPanel`、`PlayerColumns`、`PlayerArtwork`、`LyricsLayout`、`LyricsInfo`、`LyricsTextPanel`。

| kind | 用途 |
| --- | --- |
| grid / stack / border / scroll / canvas / spacer | 新建排版、卡片、隐藏滚动条容器或空白区域 |
| ref | 借用 `target` 指定的原控件，保留其业务功能；不可重复引用或形成父级循环 |
| navigate | 使用已有导航目的地；`view:名称` 打开方案内表现页，`back` 返回歌词前的页面 |
| action | 复用 `target` 指定的既有操作，不运行任意终端命令 |
| playlists | 虚拟化展示本机及互听歌单，保留来源标记、原菜单与本机歌单拖动行为 |
| artwork | 绑定当前歌曲封面；可配合 blur 与透明遮罩组成背景 |
| text / icon | 静态或宿主本地化文本、已有矢量图标 |

`navigate.page` 可用 library、songs、history、albums、artists、statistics、devices、settings、plugins、terminal、favorites、lyrics、back、view:名称。`action.target` 可用 playlist.create、player.play-pause、player.previous、player.next、lyrics.toggle。数据树不能指定新的业务函数。

`text.target` 可绑定 CurrentTitle、CurrentArtist、PageTitle、VersionText；其他文本使用 label，资源 ID 由宿主翻译。icon 使用宿主 IconKind 名称。`classes` 为样式类名，可用于类型级风格匹配。`ref.optional:true` 允许当前页面没有该控件时保留空位。

`properties` 支持控件级布局属性，另有 background、foreground、cornerRadius、blur、clipToBounds、textAlignment、textWrapping、maxLines。并非每个控件具备所有属性；提交时发现不适用或非法值会拒绝，并还原旧布局。延迟创建页面时发现的错误归属到贡献插件，报告故障并停用插件，不进入 Dispatcher 未处理异常。颜色可填十六进制或 `@` 加公共资源名。`narrowProperties` 在窗口宽度小于 900 DIP 或高度小于 620 DIP 时逐项覆盖，便于手机比例桌面、平板和低分辨率屏幕适配。

`whenPage` 支持精确页名、结尾 `*` 前缀以及开头 `!` 取反，例如 lyrics、!lyrics、playlist:*。仅决定表现节点是否创建；不是脚本表达式，不能改变播放数据。

`pages` 为方案内的表现页，只能新建表现节点，不借用 Shell 的现存控件。根节点 label 可作为页标题。页面退出时解除数据订阅并释放控件，避免长期持有歌单或歌词页面。

`playlists` 默认使用列表。声明可选能力 `ui.playlist-cards.v1` 后，可设 `presentation: "cards"`，复用专辑/艺术家相同的视口虚拟化规则，将本机及互听歌单呈现为独立卡片。点击、右键和本机拖动仍调用既有功能；它不创建新的歌单业务。未声明 presentation 的旧文件保持列表显示。

原生插件页面可能在一次同步布局事务中短暂离开可视树。宿主等待该事务结束，只有页面未重新挂接时才释放原生视图；应用方案不会重建正在操作的插件会话。

## 6. 完整 Shell 与安全边界

替换 `ShellLayout` 可以用任意栅格、堆叠或 Canvas 重新安排导航、内容和播放区，不必遵守默认三分区。典型方式是借用 SidebarPanel、ContentSurface、PlayerSurface，并放入新的容器。

完整 Shell 必须恰好保留 TitleBarSurface 和 FullLyricsHost 引用；二者可以调整行列、跨度、边距和歌词裁剪，不能被条件隐藏。窗口本身、系统按钮、确认权限与恢复快捷键不作为可替换的业务数据控件。应保证标题栏可见、歌词视口不侵入播放区、窄窗口下没有功能重叠。

## 7. 动画与歌词

```json
{
  "motion": { "lyricsDurationMs": 320, "lyricsCornerRadius": 18, "pageDurationMs": 180 },
  "lyrics": { "activeColor": "@AccentBrush", "pendingColor": "#C8D0DA", "translationColor": "#ADB9C8" }
}
```

以上为 layout 内部字段。动画只控制转场时长和连续圆角，不改变歌词时钟、逐字解析或跳转语义。歌词先定位再参与转场；缩放窗口、取消与导航会释放旧快照和透明度覆盖。减少动画偏好优先。

`StyleRule.scope` 可以限定到具有指定样式类的祖先范围，例如只改变沉浸播放器内的 TextBlock 或 VectorIcon，而不影响设置页。VectorIcon 的 foreground 对应其画刷。原有类型级规则、按压反馈和 morph 操作继续可用，详见 [STYLE_EXTENSIONS](STYLE_EXTENSIONS.md)。

`lyrics.edgeToEdge:true` 在歌词页移除窗口内侧描边，退出后恢复正常窗口外观。`artwork` 表现节点在方案仍启用时复用封面控件和模糊实例；需要常驻背景时不要为它设置 `whenPage`，可用内容层及条件遮罩决定可见范围。移除方案时释放引用。自动外观扩展声明 `runtime.startup.v1`、`autoStart:true` 及样式/布局能力，宿主在显示首个窗口前完成其初始贡献；桌宠等依赖可见窗口的扩展仍在窗口打开后启动。

歌词菜单扩展位置 `lyrics.more` 保持有效，入口统一在播放栏“更多”菜单的歌词分组。旧 `LyricsMore` 命名锚点保留为不可见占位，不再提供独立按钮。

外观初始化应短小且可取消。启动准备超过 12 秒会按插件启动故障停用并恢复默认界面，不能在初始化中等待用户交互或执行长期任务。

主窗口在样式请求完成后，继续隐藏并准备模板、实际尺寸、逻辑树样式与有界离屏首帧，再显示窗口。歌词展开的旧页底图与歌词快照各自有像素上限，首帧直接使用起始坐标；结束、取消、最小化或退出后释放。

## 8. 验证清单

分类页回顶使用标题行右侧的 `PageBackToTop`，复用当前页面滚动接口。旧锚点 `GroupsHeader`、`GroupsBack`、`GroupsBackToTop` 保留在默认隐藏的兼容区域，不再占用额外一行；旧方案引用仍可解析。`StatisticsWeekCard`、`StatisticsHourCard` 及其图表控件可从元素目录发现，图表配色与卡片类见 [STYLE_EXTENSIONS](STYLE_EXTENSIONS.md)。

在真实宿主中测试导入、同时启用、覆盖、关闭、卸载、故障和重启恢复。至少检查 640×480、960×600、常见桌面和高 DPI；长歌名、长歌词、空歌单、大歌单、互听连接变化和原来在右侧/顶部的用户偏好均应覆盖。关闭方案后逐项比较用户设置与原控件父级，不以“看起来像默认布局”代替恢复验证。
