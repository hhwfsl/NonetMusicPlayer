# 桌面布局与插件扩展

SDK 4.15 的完整界面方案支持替换排版树、复用已有操作、独立表现页和运行期设置接管，参见 [INTERFACE_PRESETS](INTERFACE_PRESETS.md)。默认布局与旧控件级接口继续可用。

## 用户设置

设置 → 布局提供导航栏（左侧/右侧）、播放栏（底部/顶部）两个下拉选项，以及“恢复默认窗口大小”按钮。拖动导航边界调整宽度；拖动三个播放按钮附近的短手柄调整播放栏高度，支持鼠标和触摸。

主窗口只保存普通状态下的逻辑宽高，缩放停顿后保存，关闭前补存最新值；最大化、最小化和 DPI 切换的临时尺寸不覆盖记忆。每次冷启动根据主屏工作区居中，不保存位置；运行期间隐藏到托盘再显示保留当前位置。“恢复默认窗口大小”在当前屏幕还原为普通窗口并居中，默认使用工作区约 90%、上限 1360×860 DIP。旧配置或无效尺寸使用默认值，记忆尺寸超过当前工作区时自动收敛。尺寸是宿主窗口偏好，不属于界面方案的 `layout.settings` 接管范围；方案启用时仍可使用恢复按钮。`--window-size-only` 验证实际存储和重启，`--native-window-size` 在 Windows 原生窗口执行相同生命周期检查。

桌面窗口可从四边或四角调整大小。鼠标使用系统原生缩放；触摸/笔使用较宽的边缘命中区与独立捕获，位置按物理像素、尺寸按逻辑像素换算，最小尺寸处固定对边。最大化、隐藏及失去捕获时结束触摸缩放。

歌曲页 `LibraryActionsFlow` 保留原名称、WrapPanel 基类/类型选择器和操作控件，采用自适应测量：搜索区可收缩至 120 DIP，按钮按实际测量宽度成组排列，空间确实不足时搜索区和按钮组分行；多行按钮会均衡分组。插件仍可按稳定名称引用容器。批量状态在窄窗口独立成行，简短动作标签与完整无障碍提示分开呈现，数量列使用剩余空间。播放栏工具区预留全部六个按钮的空间，触摸命中区不小于 44×44 DIP，歌词与队列按钮均参加可见范围校验。

专项检查：`--touch-responsive-only` 使用隔离数据，`--style=<文件>` 显式传入外部方案；`--native-touch-responsive` 使用 Windows 原生窗口和模拟触摸/笔事件。覆盖 480–1360 DIP 宽度、640×480 最小窗口、14/22 DIP 字体、多页面及批量操作。DPI 数学检查覆盖 100%、150%、200%；实体触摸硬件验收单独记录。

“适合窗口”按分配后的实际可用区域判断：必需控件保持可见、达到最小尺寸，并位于父容器及分配的行列内。界面使用 DIP（逻辑像素）；150% 下 640×480 DIP 约为 960×720 物理像素，不能直接把截图像素当作布局尺寸。框架布局取整允许一个物理像素的边缘差，容差随显示缩放换算，不放宽真正的控件越界。`--layout-validation-only` 覆盖高 DPI 首次显示、连续缩放与鼠标/触摸切换，并注入真实越界确认仍被拒绝。默认布局不会被误称为“自定义布局”，控件 ID 与几何诊断只进入日志。

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

## 分类页标题工具栏

`CatalogSearchHeader` 包含名称搜索及歌单的新建入口，`CatalogSearch` 为编辑器，`CatalogSearchButton`、`CatalogSearchClear` 为图标按钮，`CatalogCreatePlaylist` 为新建歌单按钮。`PageBackToTop` 位于它们右侧。专辑、艺术家与歌单搜索互相独立，不改变歌曲过滤条件。原版和 `interface:playlists` 共用这一工具栏。

## 动画

按控件类型定制外观、状态过渡与变形动画使用 SDK 4.14 的可选 `ui.styles.v1`，详见 [风格扩展协议](STYLE_EXTENSIONS.md)。这与具体控件的布局覆盖分开：风格可以叠加，同属性由上层覆盖，关闭后恢复底层定义；不需要枚举每个 Button 或 ToggleSwitch 实例。

页面实际切换时进行 140 ms 短淡入，三个播放按钮使用 90 ms 按压反馈。不为播放进度、歌词滚动、长列表滚动、焦点恢复创建动画。Windows 系统关闭界面动画时自动停用；任意平台可用 NONET_REDUCED_MOTION=1 禁用。动画不改变命中区域和排版尺寸。


桌面播放栏新增稳定命名控件 `PlayerQueue`，完整方案可用既有 ref 节点将它放在 `PlayerDesktopLyrics` 右侧。旧方案和 Schema 继续兼容。歌词转场读取实际 `PlayerSurface` 朝向内容区的边缘作为裁剪起点，方案无需指定封面锚点；最大化与还原始终直接执行系统状态切换。


本轮播放列表保存在窗口独立覆盖层，不属于方案可重建的 ShellLayout；反复导航、启停/重载方案不会丢失浮层。原生浮窗外部点击先结束旧弹层，再交给目标按钮；展开期间按钮带有 `popup-open` 类。
