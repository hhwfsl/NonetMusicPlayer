# 命令行播放器

NonetMusicPlayerCli 0.4.0-beta.5 是无图形框架依赖的播放器。发布程序名为 `nonet`，使用独立数据目录和音频会话，不连接桌面版实例。

## 启动与 PATH

保留对应平台整个发布目录。Windows 文件是 `publish/cli/windows/nonet.exe`，Linux/macOS 为 `publish/cli/<platform>/nonet`。

```powershell
# PowerShell：在程序目录启动
.\nonet.exe
.\nonet.exe player status
.\nonet.exe --stay player play "D:\Music\song.flac"
.\nonet.exe --json playlist list
```

CMD 在程序目录输入 `nonet`；PowerShell 使用 `.\nonet.exe`。将发布目录加入用户 PATH 后，两者都可以从任意目录直接输入 `nonet`。Linux/macOS 使用 `./nonet`，或将目录加入 PATH 后使用 `nonet`；从 Windows 复制后可能需 `chmod +x nonet`。程序不自动修改系统 PATH。

启动参数放在分类命令之前：`--data <目录>` 指定 CLI 数据根目录，`--json` 输出结构化结果，`--no-tui` 使用逐行模式，`--repl` 使用滚动提示符，`--tui` 显式选择 ANSI TUI，`--stay` 在命令执行后继续交互和播放。`--command <播放器命令>` 可重复用于批处理。它们仅是程序启动选项；业务分类不加横线，例如 `data backup <文件>`。

默认数据在程序旁的 Data，状态文件为 `cli-state.json`，含 `host: cli`。禁止指定桌面版 Data；同一 CLI 数据目录不能被两个进程同时写入。重启恢复选择和位置，不自动播放。

## 交互

默认启动字符 TUI，栏目依次是音乐、歌单、最近、统计、插件、**终端**、帮助、**设置**。Tab / Shift+Tab 切换，不使用 F1–F7。页面不再显示独立底部命令行，命令仅在终端栏目 `nonet $ ` 提示符中输入；↑↓ 直接回顾命令历史，PgUp/PgDn 回看日志，Ctrl+L 清屏。

所有 CLI 支持的操作均可用键盘完成：Ctrl+K 打开完整操作菜单，↑↓ 选择操作，Enter 打开参数表单；Tab / ↑↓ 移动字段，←→ 选择已有值，自由输入名称、文件路径、数字等，Enter 下一项或执行，Ctrl+Enter 直接提交，Esc 取消。多值参数可 Ctrl+N 追加。危险操作的确认默认取消，只有按 Y 才执行。

| 页面 | 快捷操作 |
| --- | --- |
| 全局 | Tab/Shift+Tab 切页；Ctrl+K 所有操作；Ctrl+D 退出 |
| 非终端页 | Space 暂停/继续；N/P 下/上一首；←→ 跳转 ±5 秒；+/- 音量；M 循环方式 |
| 音乐 / 最近 | Enter 播放；F 喜欢；I 信息；A 添加进歌单；Delete 按页面范围移除 |
| 歌单 | Enter 浏览；曲目 Enter 从该歌曲播放整个歌单；Q 播放歌单；C 新建；R 改名；A 添加；Esc 返回 |
| 插件 | Enter 启用/关闭；I 导入；C 开发者配置表单；Delete 卸载并选择保留或删除文件 |
| 设置 | Enter 编辑；设备/语言/日志/模式可选择；R 默认；B 备份；O 恢复 |
| 统计 | Enter 选择日期和日/月/年，再查看结果 |
| 终端 | Enter 执行；↑↓ 历史；←→/Home/End/Backspace/Delete 编辑；PgUp/PgDn 日志；Ctrl+L 清屏 |
| 结果 | ↑↓/PgUp/PgDn 回看；Enter/Esc 返回 |

操作菜单覆盖歌词导入、音源刷新、排序、封面等未分配专门快捷键的功能。表单生成的请求与命令执行的是相同业务，结果进入同一日志流；不会执行系统 Shell。路径由键盘输入，嵌套插件对象/列表通过 JSON 字段输入，其结构受开发者 Schema 验证。窄终端优先显示当前栏目，列表和帮助可翻页。退出恢复原屏幕和光标。

`--repl` 提供下面的连续滚动交互；上下键在此模式回顾命令。所有模式共用命令、确认规则和数据，不执行系统 Shell。

在终端栏目或 --repl 的短提示符 `nonet $ ` 后输入命令并回车；会话内可省略 `nonet`，例如 `help`、`player pause`，也接受 `nonet player pause`。外部 PowerShell 的 `.\nonet.exe help` 与先运行 `.\nonet.exe`、再输入 `help` 等效。--repl 没有固定底部输入框或图形弹窗：

```text
nonet help
nonet playlist create "日常"
nonet playlist list
nonet playlist add <歌单ID> "D:\Music"
nonet queue play <歌单ID>
nonet player seek 60
nonet player volume 50
nonet settings set language en-US
nonet playlist delete <歌单ID> -y
nonet clear
nonet exit
```

左右键、Home/End、Backspace/Delete 编辑；Esc 清除当前输入；Ctrl+L 清屏；空输入 Ctrl+D 退出；Ctrl+C 取消进程并保存状态。确认在当前终端输入 y + Enter，或者显式传入 `-y`，`-n` 取消。待确认时 Enter 不会播放列表歌曲。

TUI 终端使用 PgUp/PgDn 回看；--repl 的 PgUp 打开有界历史回看，PgUp/PgDn 和上下键翻页，Esc/End/Enter 返回原输入行。默认保留 1000 行，可通过设置页或 `terminalScrollbackLines` 调到最多 10000。外层 CMD、PowerShell 或终端模拟器的滚动缓存和字体由系统终端管理，应用不能跨平台限制外层缓存。

## 逐行与脚本

无 TTY、管道、TERM=dumb、--no-tui、--json 时使用逐行模式，不输出 ANSI 控制码，不依赖光标定位。清屏只清应用结果历史，不能擦除管道已经消费的输出。

```powershell
.\nonet.exe --json --command 'nonet playlist create "测试"' --command 'nonet playlist list'
```

JSON 模式仅输出命令结果，字段为 `operation`、`success`、`message`、`data`、`requiresConfirmation`；日志仍写磁盘但不混进 JSON 管道。内部 operation ID 可包含点号，公开命令语法不含点号。命令失败返回非零状态。执行命令后默认结束会话、保存并停止播放；持续播放须使用 --stay。

普通交互和逐行输出中的日志来自 Logs 文件夹的同一落盘记录，显示最低等级不影响磁盘记录。历史大小和日志显示等级参见 [命令参考](COMMANDS.md)。

## 无图形环境与插件

CLI 可用于纯命令行登录、SSH，不依赖 Avalonia、X11、Wayland 或桌面窗口。非播放事务不初始化音频设备。实际发声需要本机音频设备/服务；SSH 不会自动传输声音。Linux 可能需要发行版提供 FFmpeg 原生依赖。Windows 最低 Windows 10 22H2；当前包均为 x64，macOS/Linux 尚需实机验证。

音源插件使用相同 .impp 包验证、配置、进程握手和流媒体 Contract。UI、桌宠和主题插件不能在 CLI 启用。远程仅支持公开 GitHub Release 的 HTTPS .impp 附件。程序不执行系统 Shell，也不会拉取和执行任意仓库源码。

## 插件包与重新接入

原生插件按平台分别发布 `.impp`；CLI 只安装本机平台内容，同时兼容旧版 Contract v1 多平台包。更新保留插件 ID 和配置，拒绝降级。卸载时不删除文件会解除注册、保留原插件目录；再次导入同 ID、同版本的包即可接入，不会移动到 Retained。删除插件文件为不可撤销操作。
