# Nonet Crash Handler

C++17 编写的独立崩溃观察器。正常运行时只有一个阻塞在本地管道读取上的线程，不轮询、不发送定时心跳，不加载 .NET、音频引擎或 GUI 框架。Windows 的对话框与文件管理器 DLL 仅在崩溃时加载。

## 行为

- 宿主启动后建立仅限父子进程的标准输入管道。`C` 表示正常退出，`F` 表示宿主报告了致命异常；写端意外消失表示异常退出。`E`、`J`、`Z` 只用于更新对话框语言。
- 正常退出不生成崩溃记录，观察器随即结束。
- 托管致命异常先通过现有 AppLog 脱敏记录并刷新；观察器独立保存时间、版本、进程号、退出类别及 Windows 退出码，并提示用户。
- 报告位于当前数据目录的 `Logs/Crashes/yyyy-MM-dd_版本.log`，不会发送到网络。默认不生成进程内存转储。
- Windows 使用系统 MessageBox；macOS 仅崩溃时调用 osascript；Linux 优先调用 zenity，其次 kdialog，无可用对话框工具时保留报告并输出到标准错误。

观察器用于报告崩溃，不尝试让已经损坏的进程继续运行。普通插件业务错误仍由插件故障管理处理。系统断电、观察器本身被终止和严重文件系统故障可能无法留下报告。

## 构建

使用 [Zig 0.15.2](https://ziglang.org/download/0.15.2/) 的 C/C++ 交叉编译器。实现语言为 C++，不依赖 Zig 运行时。也可使用目标系统的 C++17 编译器编译同一源文件。

```powershell
$env:NONET_ZIG = 'C:/tools/zig/zig.exe'
./scripts/build-crash-handler.ps1 -Rids win-x64,win-x86,win-arm64,linux-x64,linux-arm64,osx-x64
./scripts/publish-desktop.ps1 -Platforms windows -Rids win-x64
```

输出在 `artifacts/native/NonetCrashHandler/<rid>/`。Linux 静态链接 musl；Windows 使用系统 CRT/API；macOS 使用系统 libSystem。发布脚本只复制对应架构的处理器，不包含编译器、缓存和源码。

固定 Windows 工具链归档：`zig-x86_64-windows-0.15.2.zip`，SHA-256：`3a0ed1e8799a2f8ce2a6e6290a9ff22e6906f8227865911fb7ddedc3cc14cb0c`。

```powershell
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/crash-check --crash-handler artifacts/native/NonetCrashHandler/win-x64/NonetCrashHandler.exe
```

自动检查在隔离夹具中验证正常关闭、致命错误、意外退出、日志脱敏和待机资源；不终止用户的播放器，也不展示系统对话框。跨架构编译与目标系统运行验证分别记录。
