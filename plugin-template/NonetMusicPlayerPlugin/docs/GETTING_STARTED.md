# 从 Hello World 到可发布插件

本文假定没有插件开发经验。无需学习 Avalonia，也不必编译播放器；先通过三个 JSON 文件开发原生页面插件。

基础工程采用 GPL-3.0-only。保留 `plugin/LICENSE`，它会随 `.impp` 自动分发；许可文件不计作页面代码，不会执行。独立工具与 SDK 的许可见工程根目录 `LICENSE`。

## 1. 理解三个文件

`manifest.json` 告诉播放器“这是什么插件”。id 是唯一标识，应采用自己的英文作者前缀，例如 `myname.music-tool`，仅限小写字母、数字、点和连字符；不能与别人的插件重复。name 是显示名称，version 用 `1.0.0` 这种数字版本；contractVersion 保持 1，因为这是协议版本，不是 SDK 版本。

`page.json` 描述页面。schemaVersion 保持 1；title 是页面标题；widgets 是原生控件数组。初始 text 部件把 `${config.greeting}` 替换为用户配置，不执行代码。

`plugin_config_schema.json` **由开发者**定义配置字段。用户只能填写字段值，不能编辑配置 UI 或运行代码。如果不需要配置，改成 `{}`，并把页面中的配置占位符替换为固定文字；此时播放器显示“无配置”。

JSON 不允许注释、尾部多余逗号或单引号字符串。VS Code 等编辑器可提示语法错误；打包器会再次检查字段、权限、大小和引用。

## 2. 修改 Hello World

1. 把 manifest 的 id 改为自己的唯一标识，填写名称与作者。
2. 把 page 的 title 改为相同名称，把 text 改为自己的文字，或者保留配置占位符。
3. 修改配置的 description（字段标题）、hint（说明）和 default（默认内容）；required=true 表示不能留空。
4. 在根目录执行 README 的 pack 命令。默认包名自动使用新的 id。
5. 在播放器插件中心导入 impp，确认其信息和权限，启用，再打开侧栏插件页面。

## 3. 添加一个播放按钮

在 manifest 的 permissions 数组中添加 `player-control`：

```json
"permissions": ["player-control"]
```

在 widgets 数组中再添加一个部件，注意与上一个部件之间用逗号分隔：

```json
{
  "type": "actions",
  "title": "播放控制",
  "actions": [
    { "label": "Playback.PlayPause", "action": { "kind": "play-pause" } },
    { "label": "Common.Next", "action": { "kind": "next" } }
  ]
}
```

这两个 label 是宿主资源 ID，会跟随中、英、日语言；自己的自由文本不会自动翻译。动作必须有对应权限，否则打包失败。page 最多 12 个部件、32 KiB，动作部件最多 8 个按钮。

## 4. 配置页面怎么生成

播放器读取 schema，按 type 创建开关、输入框、选择框或对象/列表。字段不能任意添加控件或代码：

```json
{
  "greeting": { "type": "string", "description": "问候语", "default": "你好！", "required": true, "max_length": 200 },
  "enabled": { "type": "bool", "description": "启用消息", "default": true },
  "mode": { "type": "string", "description": "模式", "default": "quiet", "enum": [{"label":"安静","value":"quiet"},{"label":"活跃","value":"active"}] }
}
```

只有被页面引用或被音源进程读取的设置才会改变实际行为；声明一个 enabled 字段并不会自动开关整个插件。字符串中可以写 `${config.greeting}`；完整字符串占位符可替换成数字或布尔值，例如 pet 的 floating 字段可以引用 bool。min/max 控制数字范围；max_length 控制字符串长度；敏感字段使用 sensitive=true 或 ui:widget=password，不能作为公开文字输出。

保存配置后页面重新读取配置。API 凭据按宿主安全策略可能只保留在当前会话，重启后需重新填写。

## 5. 菜单、桌宠、流程与生命周期

更多能力和权限见参考文档。当前菜单贡献点为 `lyrics.more`，动作只能打开本插件页面；歌词标注部件为 `lyrics-timing`，要求 lyrics-editor 和 player-control 权限。

桌宠是宿主提供的 pet 原生部件，需要 desktop-widget 权限；它可以通过受限动作控制播放、搜索和导航。流程只支持 track-changed / interval 触发消息，不能自动执行任意播放器命令。禁用 UI 插件时宿主释放页面、计时器、桌宠和事件订阅。

音源进程可以声明 lifecycle.disable / lifecycle.uninstall / lifecycle.shutdown。关闭时释放网络、解码或其他资源；卸载时仅清理自己拥有的数据。生命周期方法是 JSON-RPC 回调，不是把 C# 析构器放进 JSON。详情及握手方法见参考文档。

## 6. 自定义构建和音源入口

```text
dotnet run --project tools/NonetMusicPlayerPlugin.Packager -c Release -- pack --source plugin --output publish/my-plugin.impp --force
```

打包器默认只加入清单引用的入口和配置。音源程序依赖的 DLL 或资源必须逐个用 `--include <安全相对路径>` 加入；可重复使用该选项。声明式 UI 不能夹带脚本、DLL 或资源文件；额外文件会被拒绝。

默认包名稳定，但清单版本应在发布变更时递增。--force 只允许替换生成的 impp，先校验新包，失败时旧包保持不变。

## 7. 常见问题

| 现象 | 检查 |
| --- | --- |
| 找不到 dotnet | 安装 .NET 10 **SDK**，重新打开终端；不是只装 Runtime。 |
| 找不到 plugin 或 manifest | 当前终端必须位于本工程根目录，或显式提供 --source。 |
| 权限不足 | 清单需要声明当前原生动作要求的权限，参考权限表。 |
| 页面存在未知字段 | 当前 Schema 是封闭规范，只支持参考文档列出的字段。 |
| 输出已存在 | 改输出路径，或确认后使用 --force。 |
| 导入提示同名插件 | 先关闭、卸载旧版本，再导入新包；不同开发项目使用不同 id。 |
| 配置未生效 | 检查页面占位符、schema 字段名、默认值及类型。 |
| 在 CLI 无法开启页面 | CLI 无图形宿主，只能开启音源进程插件。 |

完成修改后执行测试和 validate，再实际导入目标播放器验证。不要在发布包中包含密码、音乐、工程缓存或其他人的文件。
