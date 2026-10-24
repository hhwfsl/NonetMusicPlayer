# 类型级风格与动画扩展

完整布局可在风格文件的可选 `layout` 中定义，使用 SDK 4.15 的 `ui.composition.v1`；旧文件保持有效。控件树、设置接管、条件布局和转场见 [INTERFACE_PRESETS](INTERFACE_PRESETS.md)。

SDK 4.14，能力 `ui.styles.v1`，服务 `styles`。Contract v1/v2、页面 Schema 与 SDK AssemblyVersion 4.0.0.0 不变；旧插件不需要修改或重新打包。

可选补充：类型目录支持 `NumericUpDown`，可通过 `part: PART_TextBox` 处理内部编辑器，避免独立输入框风格遮挡步进按钮。`morph.windowEnabled:false` 只关闭窗口最大化/还原的变形，保留容器与歌词转场；省略时沿用旧行为。此开关按字段参与风格叠加。

## 1. 最小接入

在扩展清单中声明 `ui-extend` 与所需能力；使用原生页面时另需 `native-ui`、`in-process`、`ui.native.v1`。

```json
{
  "permissions": ["ui-extend"],
  "requiredCapabilities": ["ui.styles.v1"]
}
```

这是清单片段，完整清单仍需身份、版本、运行形态等字段。托管、独立进程与声明式扩展使用相同的数据服务。风格不允许 XAML、脚本、程序集、任意资源路径或绑定表达式。

```csharp
using NonetMusicPlayer.Core.Plugins;
using System.Text.Json;
using System.Text.Json.Nodes;

// 从用户主动选择的文件读取，校验与宿主使用同一份 SDK 实现。
using var stream = File.OpenRead(selectedFile);
var sheet = StyleSheetContract.Read(stream);
await context.RequestFromUserAsync("styles", new JsonObject
{
    ["operation"] = "apply",
    ["layers"] = JsonSerializer.SerializeToNode(new List<StyleSheet> { sheet },
        StyleSheetJson.Default.ListStyleSheet)
});
```

`context` 为原生视图的 `ExtensionViewContext`。非原生插件通过 `ExtensionFrame.Requests` 发送相同服务和参数，在 `CompleteAsync` 移除已完成请求。使用 Source Generation 序列化，不依赖反射。

## 2. 服务操作

| operation | 参数 | 行为 |
| --- | --- | --- |
| `catalog` | 无 | 返回类型、属性、资源名称与当前活动层目录 |
| `apply` | `layers: StyleSheet[]` | 替换调用插件的整份风格栈，数组从底层到顶层排列 |
| `reset` | 无 | 仅移除调用插件的风格栈 |
| `morph` | `target: string` | 用户手势发起，将 Panel 内的指定控件展开至应用内容区；Esc 或关闭还原 |

每个插件最多 24 层，宿主总计最多 48 层。一次请求不超过 2,000,000 字符，单风格文件不超过 512 KiB、192 条规则。导入、存储、管理排序由插件负责；宿主只拥有可撤销的运行期外观层。无风格时不保留控件引用或运行动画定时器。

## 3. 风格文件

文件夹入口约定为 `style.json`，其他 README、Changelog 和许可证不参与执行。建议为每个风格单独维护文件夹。

```json
{
  "schemaVersion": 1,
  "id": "example.rounded",
  "name": "Rounded",
  "version": "1.0.0",
  "author": "Author",
  "description": "Rounded buttons and gentle motion.",
  "resources": { "AccentBrush": "#1677FF", "ControlCornerRadius": "18" },
  "light": { "SurfaceBrush": "#F4F6FA", "TextPrimaryBrush": "#182438" },
  "dark": { "SurfaceBrush": "#202936", "TextPrimaryBrush": "#F0F5FD" },
  "backgroundBlur": 18,
  "morph": { "enabled": true, "durationMs": 360, "cornerRadius": 24, "arc": 24 },
  "rules": [
    {
      "type": "Button",
      "setters": { "cornerRadius": "18", "padding": "14,8" },
      "durationMs": 160,
      "curve": "parabolic",
      "motion": { "pressScale": 0.97, "hoverScale": 1, "enterOffsetY": 3, "enterDurationMs": 160 }
    },
    { "type": "Button", "states": ["pointerover"], "setters": { "background": "@SurfaceHoverBrush" } }
  ]
}
```

`id` 稳定且唯一，使用小写字母、数字、点、连字符或下划线。版本为数字版本。未填写的资源、控件属性和动画不受该风格影响。`light`/`dark` 在相应主题下覆盖 `resources`；缺省主题判断沿用宿主的系统主题设置。

## 4. 控件选择与覆盖

`type` 匹配类型及其派生类，不是某一个控件实例。例如 `Button` 也匹配派生的 ToggleButton；后续 `ToggleButton` 规则可覆盖相同属性。`classes` 为必须同时满足的类名列表。`part` 可选，匹配该类型控件模板中指定名称的子部件；模板部件随宿主主题版本可能变化，应先在实际宿主验证。

`states` 支持 `pointerover`、`pressed`、`checked`、`disabled`、`focus`；多个状态需同时满足。资源与规则按层从底到顶处理，层内按文件顺序处理。同一个属性最后匹配的定义生效，不按选择器复杂度另算权重。A 定义 Button、B 定义 ToggleSwitch，两者可共存；B 只定义 Button 的背景时，不会清掉 A 的圆角或动画。

新创建的控件、虚拟化后才出现的行和弹层也匹配类型规则。已离开可视树的控件立即释放覆盖，不为了风格实现未显示的列表项。窗口系统边框、实际窗口状态和安全恢复入口仍由宿主管理；主窗口最外层圆角不接受通用 Border 规则，避免最大化时露出黑角。

支持的类型和资源由 `catalog` 发现。常用类型有 Button、ToggleButton、ToggleSwitch、CheckBox、TextBox、ComboBox、ListBoxItem、Border、TextBlock、ContentPresenter、Panel、Grid、Slider、Expander 等。

`setters` 支持背景/前景/边框颜色、边框厚度、圆角、内外边距、字体大小和字重、不透明度、最小尺寸和尺寸、阴影，以及模板图形部件的 fill/stroke/strokeThickness。不具备相应属性的控件跳过该项。画刷可使用固定颜色或 `@` 加目录中的资源名；不接受任意资源查询或表达式。

## 5. 动画与性能

- `durationMs` 控制状态属性过渡，范围 0–2000；0 表示立即应用。
- `curve` 支持 `parabolic`（`1-(1-t)^2`）、`linear`、`cubic-out`。抛物线缓动不越过终点。
- `motion` 可为原本没有动画的控件加入按压/悬停缩放和进场位移，保留原控件业务、命令、焦点与无障碍功能。缩放限 0.8–1.1，位移限 ±80 DIP。
- `morph` 为窗口最大化/还原及容器展开提供快照变形。位置、尺寸和圆角共享一个时钟，路径弯曲项为 `4*arc*t*(1-t)`。容器展开后承载的是真实控件，可交互，不是静态截图；退出恢复原父级与排列位置。
- 原生窗口状态和尺寸由操作系统决定；动画呈现在应用客户区，系统边界和合成器行为因平台而异，不承诺改写 Wayland 等系统的窗口管理动画。
- 快照最多 200 万像素，完成、取消、停用和关闭时释放。连续操作取消过期转场，不冻结播放时钟。系统减少动画设置和 `NONET_REDUCED_MOTION=1` 优先，关闭时直接切换。
- `backgroundBlur` 范围 0–40，仅模糊应用背景图片，不模糊文字或整个面板。无背景图时保留普通底色，不能将纯色背景误称为真实桌面背板模糊。

## 6. 启动恢复与原生主页

风格管理等后台功能可声明 `autoStart:true` 与 `runtime.startup.v1`，宿主在已启用状态下自动初始化，无需先打开页面。只初始化一次，停用和故障沿用正常释放流程；旧插件默认 false。

可信托管扩展可声明 `nativePage:true`，实现既有 `INonetNativeViewExtension`，工厂收到 `slot=plugin.page`。需 managed、native-ui、ui-extend、in-process、ui.native.v1 及 hasPage=true。仍保留 `pageEntry` 作为有效页面描述。旧插件默认 false，不改变其页面解析。原生页面离开时释放，但后台会话继续存在。

所有覆盖与插件 ID 绑定。停用、卸载或故障撤销其覆盖；底层数据绑定、动态资源和本地属性值继续有效。风格不会永久修改用户设置，移除风格不删除用户下载的源文件。

## 7. 统计图表与卡片（SDK 4.16）

可选能力 `statistics.charts.v1` 提供类型 `ListeningBarChart`，可覆盖 `fill`（柱色）和 `foreground`（坐标文字颜色）。图表不接受插件代码或额外数据查询。普通信息卡片使用 `Border.surface-card`，趋势卡片还带 `statistics-chart-card`；可分别设置背景、描边、内边距和圆角。输入框、焦点提示等不是卡片，不建议用全局 Border 规则清除其可辨识边界。

```json
{"type":"Border","classes":["statistics-chart-card"],"setters":{"background":"@SurfaceRaisedBrush","borderThickness":"0"}}
```

实现依据：[Avalonia 属性优先级](https://docs.avaloniaui.net/docs/properties/value-precedence)、[动画与过渡](https://docs.avaloniaui.net/docs/graphics-animation/control-transitions)。
