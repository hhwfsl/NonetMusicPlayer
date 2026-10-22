# 播放器命令参考

桌面终端和独立 CLI 使用同一套公开语法：

```text
nonet <分类> <命令> [命令选项] [参数...]
nonet help
nonet help playlist add
nonet player play
nonet playlist create "日常"
nonet playlist delete <id> -y
```

分类和命令用空格分隔，不加 `--`，也不用点号。桌面终端及已启动的 CLI 会话可省略 `nonet`，例如 `player pause`、`help`；外部系统终端必须先调用 `nonet` 程序。内部操作 ID 和设置资源 ID 不属于输入语法。参数支持单、双引号，Windows 路径保留反斜杠。`--yes` / `-y` 明确确认，`--no` / `-n` 明确取消；`--` 后按字面参数处理。帮助仅显示当前宿主能执行的命令。

## 共享命令

### 桌面设备互听

仅桌面宿主提供下列命令，CLI 帮助不列出。界面按钮和命令使用同一会话与结果流。

```text
network status
network sharing on
network invitation
network connect "nonet://connect?..."
network requests
network accept <request-id>
network reject <request-id>
network peers
network playlists <peer-id>
network tracks <peer-id>
network disconnect <peer-id|all>
network sharing off
network renew
settings set lanApprovalWaitSeconds 30
```

`invitation` 将本机邀请复制到系统剪贴板，不将密钥打印或保存到终端历史；`connect` 的邀请参数也不回显或保存。邀请应只交给同一可信局域网中的自己的设备。`accept/reject` 只处理当前未过期请求，不能重复使用。连接会主动开启本机共享以建立双向会话。通过 `network playlists` 返回的 page 使用 `navigation open <page>` 进入歌单；播放、收藏与添加到本地歌单沿用既有歌曲 ID 命令。`renew` 轮换尚未使用的邀请，不解除已建立的连接。

下表每项都以 `nonet` 开头：

`network peers` 保留已配对但暂不可达的设备，并提供 `reachable` 布尔值及 `connectionState`（`available` / `reconnecting`）；暂时停网不撤销配对或移除歌单。主动 `network disconnect`、关闭共享和正常退出仍结束会话。

| 分类 | 命令与参数 |
| --- | --- |
| 无 | `help [category] [command]`、`clear`、`exit` |
| `app` | `exit` |
| `player` | `status`、`play [track-id或file]`、`pause`、`resume`、`next`、`previous`、`seek <seconds>`、`volume <0-100>`、`mute`、`mode <repeat-all或repeat-one或shuffle>`、`devices`、`device <name>` |
| `queue` | `list`、`play <playlist-id> [track-id]` |
| `music` | `list [query]`、`info <track-id>`、`remove <track-id...>` |
| `playlist` | `list`、`create <name>`、`rename <id> <name>`、`describe <id> <description>`、`delete <id>`、`add <id> <file或folder或track-id...>`、`remove <id> <track-id...>`、`move <id> <track-id> <position>`、`reorder <id> <position>`、`cover <id> <file或default>` |
| `favorite` | `add <track-id>`、`remove <track-id>` |
| `history` | `list`、`remove <track-id...>`、`clear` |
| `statistics` | `show [yyyy-MM-dd] [day或month或year]` |
| `settings` | `list`、`get <name>`、`set <name> <json-value>`、`reset` |
| `data` | `backup <file>`、`restore <file>` |
| `lyrics` | `show`、`import <file>`、`path` |
| `plugins` | `list`、`install <impp-file或github-release-url>`、`enable <id>`、`disable <id>`、`uninstall <id> [keep-files或delete-files]`、`config <id> [json]`、`schema <id>`、`refresh <id>` |

无分类的命令操作终端本身。`nonet clear` 只清空显示和结果历史，保留磁盘日志、业务数据及方向键命令历史。`nonet exit` 在桌面版返回音乐主页，在 CLI 结束会话。`nonet app exit` 真正退出宿主进程。

位置从 1 开始。“我喜欢”ID 为 `liked`，不能删除或改名。歌曲、歌单 ID 从列表结果获取。文件 `nonet player play` 是临时播放；添加歌曲需指定歌单。系统默认输出设备标识为 `Playback.SystemDefault`。

`history remove` 只移除最近播放记录，不改变歌单。桌面歌曲浏览的 `music remove` 需要确认，会移除所有歌单引用及应用生成的数据，但不删除导入的源音乐或源歌词。专辑与艺术家只是分类，不提供删除歌曲功能。`plugins uninstall` 默认使用 `keep-files`，停用并解除安装记录，插件文件保留原位；`delete-files` 不可撤销地删除插件本体，仍需确认或 `-y`。

## 桌面命令

| 分类 | 命令与参数 |
| --- | --- |
| `window` | `show`、`hide`、`minimize`、`maximize`、`restore`、`close` |
| `navigation` | `open <page>` |
| `documentation` | `open` |
| `music` | `search <query>`、`undo` |
| `layout` | `reset` |
| `desktop-lyrics` | `toggle`、`lock <true或false>` |
| `album` / `artist` | `cover <name> <file或auto或default>` |
| `import` | `cancel` |
| `playlist` | `import <m3u-file>`、`export <m3u-file>` |
| `data` | `directory <folder>` |
| `selection` | `begin`、`end`、`all`、`clear`、`select <track-id...>`、`list` |

页面 ID：`library`（音乐主页）、`songs`、`albums`、`artists`、`history`、`statistics`、`network`（设备互听）、`plugins`、`terminal`、`settings`、`lyrics`。歌单使用 `playlist:<id>`，分类使用 `album:<name>` 和 `artist:<name>`。

`auto` 恢复解析封面，`default` 使用软件默认封面。`window close` 遵循托盘设置。裁剪等视觉编辑不模拟系统键鼠，命令操作其保存结果。

## 终端设置与日志

设置 → 终端包含独立不透明度、字体大小、历史行数和最低显示等级。历史默认 1000 行，范围 1–10000；字体 10–32 px；最低显示等级可选 INFO、WARN、ERROR。CLI 可使用：

```text
nonet settings set terminalScrollbackLines 2000
nonet settings set terminalMinimumLogLevel WARN
```

终端日志与 Logs 文件夹来自同一落盘记录流；命令输入只作终端回显，不记录为日志。等级筛选只影响显示，磁盘固定记录 INFO、WARN、ERROR。文件命名为 `yyyy-MM-dd_版本号.log`，同日同版本追加到同一文件；最多保留 10 个受管日期版本文件，单文件达 32 MiB 时保留约后 16 MiB 的完整行。原版本日志不被改名。

界面动作和终端操作共享业务状态和事务摘要。查询数据另行显示，Unicode 歌名直接输出文字。配置输入不回显、不加入输入历史，日志认证信息和路径脱敏。移除歌曲不会删除原音频；恢复和卸载等操作默认确认。仅安装可信插件，本终端不执行系统 Shell。
