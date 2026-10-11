# Linux 运行指南

发行目标为 Linux x86_64（`linux-x64`），不是 ARM64。桌面包自带 .NET 运行时，不需要另装 .NET SDK 或通过 Wine 启动。保留 ZIP 中全部文件；程序目录须可写，默认数据保存在该目录的 Data 中。不要以 root 运行。

Linux 版本只进行了交叉构建及归档校验，尚未经 Linux/niri 实机验证。

## Arch Linux 与 niri

当前 Nonet 使用 Avalonia 12.1.1 的默认 X11 后端，在 niri 下通过 XWayland 运行。它没有启用可选的 Avalonia.Wayland 后端。niri 25.08 及以上可按需启动 PATH 中的 xwayland-satellite（至少 0.7），无需手动写死 DISPLAY。

更新系统并补齐运行依赖；不要仅同步软件源而不升级系统：

```bash
sudo pacman -Syu --needed unzip xwayland-satellite libx11 libice libsm libxrandr libxi libxcursor libxrender fontconfig freetype2 icu openssl zlib krb5 alsa-lib libpulse xdg-utils noto-fonts-cjk
```

安装 xwayland-satellite 后退出并重新登录 niri。在 niri 会话的终端中检查 `niri --version`、`xwayland-satellite --version` 和 `printenv DISPLAY`。DISPLAY 应由当前会话设置；不要随意覆盖成 `:0`。旧 niri 请先升级并依照其对应版本的官方文档配置。

从 Release 下载 `NonetMusicPlayer.Desktop-<version>-linux-x64.zip`，以下以 0.4.0-beta.18 为例，在用户可写目录解压后运行：

```bash
mkdir -p "$HOME/Applications/Nonet"
unzip "$HOME/Downloads/NonetMusicPlayer.Desktop-0.4.0-beta.18-linux-x64.zip" -d "$HOME/Applications/Nonet"
cd "$HOME/Applications/Nonet"
chmod +x Nonet
./Nonet
```

升级既有安装前退出 Nonet、备份数据，再替换程序文件；不要将旧目录中的使用数据重新打包上传。应用已有的在线更新也会排除数据。

## 音频、文件选择与托盘

窗口按键位置可在设置 → 外观中切换为左上角或右上角，立即生效并保存。Linux 默认在右侧；左侧采用关闭、最小化、最大化/还原的顺序。

- 使用系统已有的 PipeWire 或 PulseAudio 会话。不因安装 Nonet 而替换音频系统。PipeWire 用户可用 `wpctl status` 检查输出；声音有问题时检查应用输出设备及 Data/Logs 中的错误。ALSA/Pulse 库用于运行时设备接入；音乐解码所需的 FFmpeg 原生库已随包提供，不需要额外安装视频背景工具。
- 文件选择对话框使用桌面 portal；精简 niri 环境中若对话框不出现，可安装 `xdg-desktop-portal` 与 `xdg-desktop-portal-gtk`，并按照现有桌面 portal 配置指定 FileChooser 后端，然后重新登录。
- niri 自身不提供托盘栏。没有安装支持 StatusNotifier 的面板时，建议将 Nonet 的关闭行为改为直接关闭，以免隐藏后找不到窗口。再次运行 `./Nonet` 可以通过单实例通信恢复已有实例。
- 桌面歌词、桌宠和自由定位浮层受 XWayland/合成器窗口规则约束，不能保证等同于 Windows 的置顶、点击穿透或绝对定位行为。
- 要在应用启动器显示 Nonet，可创建用户级 desktop entry，Exec 使用 Nonet 的绝对路径、Terminal=false。niri 自启动可以在已有配置中添加 `spawn-at-startup "/home/用户名/Applications/Nonet/Nonet"`；仅选一种启动方式，避免重复配置。通用 XDG autostart 开关是否被执行取决于会话的 autostart 集成。

## 排查与 CLI

所有共享库须与程序一起保留。用 `ldd ./Nonet` 和 `ldd ./libSkiaSharp.so` 查找缺失库，不要只复制可执行文件。检查程序目录写入权限及 DISPLAY 是否为空。若使用本地代理、特殊网络或不受信任插件，分别排查，避免误判为显示后端故障。

纯终端环境下载独立 `NonetMusicPlayerCli-<version>-linux-x64.zip`，放入另一目录并执行 `chmod +x nonet` 后运行 `./nonet`；`./nonet help` 显示命令。CLI 不需要 niri、XWayland 或图形库，其数据默认与桌面版分离。

## 参考

- [Avalonia Linux 后端与依赖](https://docs.avaloniaui.net/docs/platform-specific-guides/linux)
- [niri 官方 Xwayland 文档](https://github.com/niri-wm/niri/wiki/Xwayland)
- [Arch xwayland-satellite 软件包](https://archlinux.org/packages/extra/x86_64/xwayland-satellite/)
- [.NET 自包含 Linux 应用依赖](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual)
