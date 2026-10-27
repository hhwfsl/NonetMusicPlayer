# 页面过渡扩展

可选能力：`ui.page-transitions.v1`。通过已有 `styles` 服务应用、查询和移除；权限为 `ui-extend`。页面 Schema、Style Schema 1、Contract v1/v2 和 SDK AssemblyVersion 4.0.0.0 保持兼容。

## 默认行为

普通页面内容采用 180 ms 的淡入与向上归位，初始透明度为原透明度的 90%，初始纵向位移为 12 个逻辑像素。导航栏、窗口标题与播放器位置不随内容移动。

仅实际导航触发过渡；初次显示窗口、修改设置和普通数据刷新不重播入场动画。快速导航会立即取消前一次过渡。动画仅覆盖渲染属性，结束或取消后释放绑定，恢复原透明度、变换与样式绑定，不重新挂接原生插件页面。窗口最小化、关闭或移除相关风格也会释放覆盖。

普通页面不创建页面截图，不按帧改变布局尺寸。系统减少动画偏好或 `NONET_REDUCED_MOTION=1` 优先于风格配置。

动画时钟释放绑定后保持完成态，最后一帧与真实页面位置、透明度连续衔接。

## 风格定义

动画插件可以只提交下面的动画风格，无须提供布局、颜色或控件规则：

```json
{
  "schemaVersion": 1,
  "id": "example.navigation-motion",
  "name": "Navigation Motion",
  "version": "1.0.0",
  "pageTransitions": {
    "defaults": {
      "enabled": true,
      "effect": "fade-slide",
      "durationMs": 220,
      "offsetX": 0,
      "offsetY": 10,
      "startOpacity": 0.9,
      "easing": "cubic-out"
    },
    "rules": [
      { "from": "playlist:*", "to": "playlist:*", "profile": { "effect": "slide", "offsetX": -16, "offsetY": 0 } },
      { "to": "terminal", "profile": { "effect": "fade", "durationMs": 120 } },
      { "to": "plugin:*", "profile": { "enabled": false } }
    ]
  }
}
```

| 字段 | 范围及语义 |
| --- | --- |
| `enabled` | 布尔值，false 关闭宿主页面过渡 |
| `effect` | `none`、`fade`、`slide`、`fade-slide` |
| `durationMs` | 0–1000 ms，0 立即完成 |
| `offsetX` / `offsetY` | -96–96，初始位置相对最终位置的逻辑像素偏移 |
| `startOpacity` | 0–1，相对原控件透明度的初始比例 |
| `easing` | `linear`、`cubic-out`、`parabolic` |

所有字段可省略。风格由底层至顶层叠加，每层先应用 `defaults`，再按数组顺序应用同时匹配 `from` 和 `to` 的规则；后定义的字段覆盖前面的对应字段，其他字段保留。每个风格最多 32 条规则。

选择器支持完整页面 ID 或单个结尾 `*`，默认 `from`、`to` 为 `*`。常用 ID 为 `library`、`songs`、`history`、`playlists`、`albums`、`artists`、`statistics`、`devices`、`settings`、`plugins`、`terminal`、`cloud-search`；分类或扩展页面可使用 `playlist:*`、`album:*`、`artist:*`、`plugin:*`、`interface:*`、`lan:*`。

歌词页继续使用专用的展开/收回过渡；由已有 `layout.motion.lyricsDurationMs`、`lyricsCornerRadius` 控制，普通页面规则不会与它叠加。已有 `layout.motion.pageDurationMs` 仍有效，在所在风格的 `pageTransitions` 之前参与时长叠加。

## 插件调用

清单声明：

```json
{ "permissions": ["ui-extend"], "requiredCapabilities": ["ui.styles.v1", "ui.page-transitions.v1"] }
```

使用 `styles` 服务的 `operation: "apply"` 和 `layers: StyleSheet[]` 提交风格。原生扩展通过 `ExtensionViewContext.RequestFromUserAsync` 请求；其他扩展通过 `ExtensionFrame.Requests` 请求，在 `CompleteAsync` 接收结果。完整调用方式见 [类型级风格与动画扩展](STYLE_EXTENSIONS.md)。

`operation: "catalog"` 返回的 `pageTransitions` 包含能力、效果、缓动、默认值和限制。

`operation: "resolve-page-transition"` 接受 `from` 和必填 `to`，返回 `profile`，包含叠加全部活动风格后的有效配置；省略 `from` 使用当前页。此查询不触发导航或播放动画。

`operation: "reset"` 只移除调用插件的层。插件停用或卸载由宿主撤销其覆盖，下层风格和默认配置自动恢复。

使用 SDK 的 `StyleSheetContract.Read/Validate` 验证文件，通过 `StyleSheetJson.Default.StyleSheet` 或 `ListStyleSheet` 源生成序列化，避免复制协议实现。
