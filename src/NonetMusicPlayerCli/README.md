# NonetMusicPlayerCli

本项目由 AI 完成，尚未经过人工代码 review；测试仅在 Windows 进行，macOS/Linux 未经实机验证。

独立的纯命令行音乐播放器，当前版本 **0.4.0-beta.12**。与 Nonet 桌面播放器共享核心逻辑，但不依赖窗口，使用独立数据和播放会话。

## 启动

发布程序为 `nonet.exe`（Windows）或 `nonet`（Linux/macOS）。PowerShell 在所在目录输入 `.\nonet.exe`，CMD 输入 `nonet`，Linux/macOS 输入 `./nonet`。保留发布文件夹内所有运行文件；将整个目录加入用户 PATH 后可在任意终端直接输入 `nonet`，无需系统 Shell 扩展或脚本包装。

```text
nonet help
nonet playlist list
nonet player play "song.flac"
nonet settings set terminalScrollbackLines 1000
```

不带参数启动交互会话。在 `nonet $ ` 提示符后可直接输入 `help`、`player pause` 等命令并回车，也可保留 `nonet` 前缀。危险操作在终端确认，或用 `--yes` / `-y` 确认、`--no` / `-n` 取消。命令分类不加 `--`，旧点号语法不支持。

ANSI 终端默认启动 TUI：Tab / Shift+Tab 切换音乐、歌单、最近、统计、插件、终端、帮助和设置。↑↓ 选择，Enter 浏览或播放，空格暂停/继续；Ctrl+K 打开所有 CLI 操作的键盘菜单，填写参数即可执行，不必输入命令。设置直接选项编辑，破坏性操作用 Y 确认、Enter / N / Esc 取消。

只在“终端”栏目输入共享命令；↑↓ 调取命令历史，PgUp/PgDn 回看日志，Ctrl+L 清屏。Ctrl+D 或终端中的 `exit` 退出。没有 F1–F7 快捷分页；窄终端会优先显示当前导航栏目。详细键盘表见 CLI.md。

`--tui` 显式启用 ANSI TUI，`--repl` 使用普通滚动式 REPL，`--no-tui` / 管道 / `TERM=dumb` 使用逐行模式。TUI 退出恢复原终端屏幕；REPL 保留可复制的滚动输出。历史默认 1000 行、最高 10000；外层终端字体及缓存由终端自身控制。日志显示筛选不改变磁盘日志。

默认数据位于程序旁 Data；禁止指定桌面版 Data。无图形环境中，非音频操作不要求音频设备；播放仍需本机音频服务。macOS/Linux x64 包待实机验证。

持续播放使用 `nonet --stay player play "song.flac"`。脚本使用 `nonet --json playlist list`；输出为 UTF-8 JSON，不混入日志控制码。完整语法、PATH 和纯终端运行说明见发布目录中的 CLI.md、COMMANDS.md。

开发构建：`dotnet build src/NonetMusicPlayerCli -c Release`。发布：`pwsh -File scripts/publish-cli.ps1`。
