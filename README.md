# NonetMusicPlayer

本项目由 AI 完成，尚未经过人工代码 review。测试仅在 Windows 环境进行；macOS 和 Linux 只生成了交叉构建产物，未进行对应系统的实机测试。当前为预发布版本，使用或继续开发前请自行审查代码并备份数据。

软件名称：**Nonet**。当前桌面版与 CLI 源码版本：**0.4.0-beta.8**。公开仓库仅包含桌面端、独立 CLI、共享核心、插件 SDK、主项目打包器和必要测试；插件模板暂不提交，不包含移动端源码或构建产物。

## 技术栈

| 用途 | 技术 / 固定版本 |
| --- | --- |
| 构建与语言 | .NET SDK 10.0.401，C#，目标 net10.0 |
| 桌面界面 | Avalonia / Desktop / Fluent / Inter 12.1.1 |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| 音频 | SoundFlow 1.4.1，SoundFlow.Codecs.FFMpeg 1.4.0 |
| 媒体标签 | TagLibSharp 2.3.0 |
| 桌面持久化 | SQLite，Microsoft.Data.Sqlite 10.0.12 |
| CLI 与共用 JSON | System.Text.Json，持久化模型使用 Source Generation |
| 插件开发 SDK | NonetMusicPlayer.PluginSdk 4.0.0，Contract v1/v2、页面 Schema v1/v2 |

CLI 不引用 Avalonia，通过系统终端运行。插件 SDK 无图形、音频或外部 NuGet 依赖。实际依赖以各项目的 `.csproj` 为准；第三方许可见 [docs/licenses](docs/licenses)。

## 许可证

项目源码及插件基础工程采用 [GNU GPL v3.0](LICENSE)（SPDX：GPL-3.0-only）。第三方依赖保留其原有许可证，见 `docs/licenses`；发行包随附完整项目许可及第三方许可。

## 构建前提

- 安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。`global.json` 固定 10.0.401 并允许同一特性带的后续补丁；SDK 8/9 不能构建 net10.0。
- 安装 Git；首次还原需要访问 nuget.org。无需复制开发机器的 NuGet 缓存。
- 发布脚本使用 PowerShell；建议 PowerShell 7。普通构建和运行直接使用 dotnet，不需要 PowerShell。
- Windows 发行目标为 Windows 10 22H2 / Windows 11 x64，不支持 Windows 7。macOS / Linux 目标为 x64，需要对应系统的音频及桌面原生依赖；当前未经实机验证。不提供 ARM64 发行包。

```text
git clone https://github.com/hhwfsl/NonetMusicPlayer.git
cd NonetMusicPlayer
dotnet --version
dotnet restore NonetMusicPlayer.slnx --configfile NuGet.Config
dotnet build NonetMusicPlayer.slnx -c Release --no-restore
```

## 从源码启动

桌面端：

```text
dotnet run --project src/NonetMusicPlayer.Desktop -c Release
```

独立 CLI（无桌面依赖）：

```text
dotnet run --project src/NonetMusicPlayerCli -c Release -- help
dotnet run --project src/NonetMusicPlayerCli -c Release
```

两者的数据默认保存在各自程序目录的 `Data` 中；不要让桌面版与 CLI 共用数据目录。桌面隔离测试可使用 `NONET_DATA_DIR` 环境变量。开发运行目录需要可写权限。

## 自包含发布

在仓库根目录执行：

```text
pwsh -File scripts/publish-desktop.ps1 -CleanOnly -SkipInstaller
pwsh -File scripts/publish-cli.ps1 -CleanOnly
```

桌面版从新的 `artifacts/publish-stage-*` 构建，纯净 ZIP 输出到 `publish/desktop/archives`，不会读取或替换已有 `publish/desktop/windows`、`macos`、`linux`。CLI 的 `-CleanOnly` 输出新的 `artifacts/cli-stage-*`，不修改既有 CLI 发布目录。两者均自带 .NET 运行时，保留必要原生音频库、元数据库与许可文件，不附带个人数据、构建缓存或调试符号。

桌面更新资产的文件名固定为：

```text
NonetMusicPlayer.Desktop-0.4.0-beta.8-win-x64.zip
NonetMusicPlayer.Desktop-0.4.0-beta.8-osx-x64.zip
NonetMusicPlayer.Desktop-0.4.0-beta.8-linux-x64.zip
```

macOS ZIP 从 `Contents/` 开始组织，解压到 `Nonet.app/` 内；Linux/macOS 的 ZIP 保存可执行权限。未进行代码签名或 macOS 公证。更新清单与校验规则见 [UPDATES.md](docs/UPDATES.md)。

不带 `-CleanOnly` 的发布脚本会更新对应发布目录，并保留旧包和便携数据；已在使用的软件请优先采用纯净构建模式。Windows 安装器另外需要 Inno Setup 6，通过 `scripts/build-windows-installer.ps1` 构建；不属于上述纯净 ZIP 流程。

## 项目组织

本地开发与发布使用同一个 `NonetMusicPlayer` 目录。移动端源码可保留在本地，但当前被 Git 忽略，不在公开仓库范围内。`publish`、`artifacts` 和 NuGet 缓存均不提交；本地归档中的旧开发历史及使用数据不得合入公开 Git 历史。

```text
src/NonetMusicPlayer.Desktop/           Avalonia 桌面宿主
src/NonetMusicPlayerCli/                独立终端 / TUI 宿主
src/NonetMusicPlayer.Core/              播放、命令、插件、歌词等共享逻辑
src/NonetMusicPlayer.PluginSdk/         独立插件验证及打包 SDK
tests/                                Core、桌面、插件、更新回归夹具
tools/NonetMusicPlayer.PluginPackager/  主项目维护的插件打包与验证入口
scripts/                              发布、校验和插件依赖检查脚本
docs/                                 开发文档、使用手册、第三方许可
assets/icons/                         图标源文件及跨平台表示
```

插件模板保存在独立目录 `../NonetMusicPlayerPlugin`，本次不提交 GitHub。插件源码与播放器分开，但开发通过 `ProjectReference` 引用本仓库 `src/NonetMusicPlayer.PluginSdk`，打包使用 `tools/NonetMusicPlayer.PluginPackager`，调试运行本仓库桌面宿主。没有播放器源码不能构建新模板；不再复制 SDK。

具体插件是独立项目，不放在播放器源码仓库内；本地统一放在 `../MyNonetMusicPlayerPlugin/<插件项目>/`，由各项目自己的脚本生成包。主解决方案、默认测试及公开源码导出不依赖这些目录，`samples/` 不参与 Git 跟踪或 GitHub 提交。

## 验证

```text
dotnet run --project tests/NonetMusicPlayer.CoreChecks -c Release -- artifacts/core-checks
dotnet run --project tests/NonetMusicPlayer.UiChecks.Desktop -c Release -- artifacts/ui-checks --desktop-revision-only
dotnet run --project tests/NonetMusicPlayer.UiChecks.Desktop -c Release -- artifacts/nonet-checks --nonet-release-only
dotnet run --project tests/NonetMusicPlayer.PluginUiChecks.Desktop -c Release -- artifacts/plugin-checks
dotnet run --project tools/NonetMusicPlayer.PluginPackager -c Release -- help
```

UI 夹具使用 Avalonia.Headless 与隔离测试数据，不等同于跨平台实际交互、音频设备及安装验证。系统相关测试须在对应平台继续执行。

本版执行范围与平台限制见 [RELEASE_VALIDATION.md](docs/RELEASE_VALIDATION.md)。

构建、变更和接口资料：[CHANGELOG](CHANGELOG.md)、[CLI 构建与使用](src/NonetMusicPlayerCli/README.md)、[插件开发与依赖](docs/PLUGIN_DEVELOPMENT.md)、[插件 Contract](docs/PLUGIN_DEVELOPMENT.md)、[存储](docs/STORAGE.md)、[本地化](docs/LOCALIZATION.md)。

插件开发使用主项目的 SDK 4.0，并直接引用源码；通用扩展接口见 [EXTENSIONS](docs/EXTENSIONS.md)，旧插件兼容策略见 [PLUGIN_DEVELOPMENT](docs/PLUGIN_DEVELOPMENT.md)。
