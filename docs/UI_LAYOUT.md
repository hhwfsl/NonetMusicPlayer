# 桌面布局与插件扩展

SDK 4.15 的完整界面方案支持替换排版树、复用已有操作、独立表现页和运行期设置接管，参见 [INTERFACE_PRESETS](INTERFACE_PRESETS.md)。默认布局与旧控件级接口继续可用。

## 用户设置

设置 → 布局仅保留两个下拉选项：导航栏（左侧/右侧）、播放栏（底部/顶部）。拖动导航边界调整宽度；拖动三个播放按钮附近的短手柄调整播放栏高度，支持鼠标和触摸。

不再提供用户 JSON 布局文件的编辑、导入、导出、校验或重载。旧 `Data/Layouts/active.layout.json` 留存但不自动读取，不会影响新位置选项。恢复默认设置仍可重置布局偏好。

界面深度定制由插件提供，不在设置中展示插件布局代码。只安装可信来源；Ctrl+Shift+Esc 是不受插件覆盖的恢复快捷键，可停用界面扩展并打开插件中心。

## 控件级扩展（SDK 4.11 / ui.elements.v1）

插件声明 `ui-extend` 权限及可选能力 `ui.elements.v1`。通过 `ui` 服务调用：

1. `{operation:"elements",offset:0,limit:100}` 分页查询主窗口的控件目录，包括容器、模板子控件和已实现的列表行。最多 200 项/页、4096 个已实现控件。
2. 每项包含 id、name、type、parent、width、height、canReparent、acceptsChildren，不包含文字内容、密码或业务数据。
3. `{operation:"update-elements",patches:[{target:"PlayerPlay",properties:{width:56,margin:"4,0"}}]}` 提交一整份布局覆盖。相同插件再次调用替换其旧覆盖。
4. `{operation:"reset-elements"}` 仅撤销该插件的布局，不改变其他插件或用户位置偏好。

C# 可使用 `ElementLayoutContract.Update(new ElementLayoutPatch("PlayerPlay", new() { ["width"]=56d }))` 构造参数，再调用 ExtensionViewContext.RequestFromUserAsync("ui", arguments)。

唯一 name 可直接作为稳定 target；没有名字的控件使用目录返回的 element:N ID，仅在该实例存活期间有效。虚拟列表未创建的行不强制实现，以保持虚拟化；应通过模板/区域替换定制未实现内容。页面重建后命名目标自动重新定位，缺席目标等待相应页面出现。

支持：width/height（数字或 "auto"）、minWidth/minHeight/maxWidth/maxHeight（最大值可用 null 表示无限）、margin、padding、horizontalAlignment/verticalAlignment、Grid 的 row/column/rowSpan/columnSpan/rows/columns、zIndex、opacity、visible、fontSize、StackPanel 的 orientation/spacing、Canvas 的 left/top、dock。仅对具备相应属性的控件生效，目录返回属性名单。行列定义用 Auto、*、2* 或数字的逗号分隔字符串。有限值、大小范围、批次上限均受校验，不接受绑定表达式、XAML、脚本或任意反射。

patch 的 parent 可以指向 Panel 的 id/name，index=-1 放到末尾；用于移动原有 Panel 子控件或改变顺序，保留事件和命令。拒绝父级循环及同控件同属性的插件冲突；先校验、失败回滚。受模板管理的子节点不能直接脱离模板，应修改其布局属性或替换上层区域。

## 添加自定义控件

清单 contributions 中新增 `slot:"ui.element"`，填写稳定 `target`、`placement` 和 `view` 或 `native:true`：

```json
{
  "slot":"ui.element", "label":"附加操作", "action":"show",
  "target":"TitleBrand", "placement":"append",
  "view":{"type":"button","text":"附加操作","action":"my-action"}
}
```

placement 支持 append/prepend（Panel 内）、before/after（Panel 子项旁）和 replace（Panel、Decorator 或 ContentControl 的子项）。命名锚点不存在时等待对应页面；控件插入与所属插件启停同步。原生视图要求 managed、native-ui 和 ui.native.v1；工厂 context 包含 target 与 placement，用于区分同类贡献。进程/AOT 插件也可使用数据控件树，不需要共享 UI 程序集。

也可在运行时通过 `ui` 服务插入：`{operation:"mount-element",key:"my-button",target:"element:123",placement:"append",view:{type:"button",text:"操作",action:"my-action"}}`。target 允许目录返回的运行时 ID，适合没有名字的容器；每插件最多 32 个动态控件。同 key 替换原贡献，`{operation:"unmount-element",key:"my-button"}` 撤销指定控件；省略 key 撤销该插件全部动态控件。停用/故障会清除这些注册。原生运行时贡献可用 native:true，仍需完整权限。

大范围重排可继续使用 shell.workspace/navigation/player/titlebar 和 page.* 表现区域替换，插件可构建完整新区域，并通过公共命令/状态保持业务交互。主窗口和系统安全核心不作为可重新挂接的控件暴露。

## 生命周期和兼容

所有布局覆盖和插入控件由宿主按插件 ID 管理。停用、卸载、故障和窗口关闭会撤销并释放；导航前恢复原树，导航后重建当前页面的覆盖。原数据绑定和主题动态资源不被永久替换。多个插件不能同时控制同一控件同一属性。

旧 ui read/apply/reset 和 schema 1/2 解析器仅作为插件兼容适配器保留，运行期间在内存应用，停止该插件恢复宿主布局；不再作为用户文件配置功能。新插件应使用控件服务或区域/页面贡献。Contract v1/v2、页面 Schema 与 SDK AssemblyVersion 4.0.0.0 不变。

## 动画

按控件类型定制外观、状态过渡与变形动画使用 SDK 4.14 的可选 `ui.styles.v1`，详见 [风格扩展协议](STYLE_EXTENSIONS.md)。这与具体控件的布局覆盖分开：风格可以叠加，同属性由上层覆盖，关闭后恢复底层定义；不需要枚举每个 Button 或 ToggleSwitch 实例。

页面实际切换时进行 140 ms 短淡入，三个播放按钮使用 90 ms 按压反馈。不为播放进度、歌词滚动、长列表滚动、焦点恢复创建动画。Windows 系统关闭界面动画时自动停用；任意平台可用 NONET_REDUCED_MOTION=1 禁用。动画不改变命中区域和排版尺寸。
