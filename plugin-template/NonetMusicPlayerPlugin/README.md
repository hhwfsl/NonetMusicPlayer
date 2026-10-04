# NonetMusicPlayerPlugin

一个可直接修改并生成 `.impp` 的 NonetMusicPlayer 插件基础工程。默认插件是 **Hello World**，只显示可配置的问候语；不包含桌宠、游戏或其他 samples。

源码采用 [GNU GPL v3.0](LICENSE)（GPL-3.0-only）；SDK 和模板使用相同许可证。默认 `.impp` 附带 `plugin/LICENSE`；打包器自动保留插件目录中的 LICENSE / COPYING / NOTICE 等许可文件。

适配 Nonet 桌面版 **0.4.0-beta.1**，清单 Contract v1 / 页面 Schema v1。独立 SDK **3.0.0** 与播放器使用同一份验证源码，不引用播放器、Avalonia、音频库或外部 NuGet 包。项目与命名空间已经更名为 NonetMusicPlayerPlugin / NonetMusicPlayer.PluginSdk；旧的声明式 v1 `.impp` 包仍可使用。

## 五分钟开始

1. 安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，确认 `dotnet --version` 的主版本为 10。
2. 在此工程根目录打开 PowerShell、CMD、macOS 或 Linux 终端。
3. 执行：

```text
dotnet build NonetMusicPlayerPlugin.slnx -c Release
dotnet run --project tools/NonetMusicPlayerPlugin.Packager -c Release -- pack
```

得到 `publish/developer.hello-world.impp`。在播放器的插件中心导入它，启用后点击侧栏的 **Hello World**。在插件配置中修改 Greeting，保存后页面显示新问候语。

重复构建会保护已有输出。确认替换自己生成的包时：

```text
dotnet run --project tools/NonetMusicPlayerPlugin.Packager -c Release -- pack --force
```

## 开发自己的插件

编辑 `plugin/manifest.json` 中的 id、name、author、description 和 version；编辑 `plugin/page.json` 的页面；编辑 `plugin/plugin_config_schema.json` 定义用户填写的配置。

完整新手教程：[从 Hello World 到可发布插件](docs/GETTING_STARTED.md)。接口、权限、音源进程、配置和生命周期：[插件开发参考](docs/PLUGIN_DEVELOPMENT.md)。源码中的验证器是最终约束；用户配置不能修改开发者定义的 UI 规范。

```text
plugin/                         唯一正在开发的插件，不是 samples
  manifest.json                 插件身份、能力和入口
  page.json                     声明式原生页面
  plugin_config_schema.json     开发者定义的配置页面
sdk/NonetMusicPlayer.PluginSdk/ 主机同步的独立 Contract 和打包 API
tools/NonetMusicPlayerPlugin.Packager/  跨平台打包及验证命令
tests/NonetMusicPlayerPlugin.Checks/   初始工程回归检查
docs/                           入门教程与完整接口参考
publish/                        生成的 impp；不纳入 Git
```

## 验证与分发

```text
dotnet run --project tests/NonetMusicPlayerPlugin.Checks -c Release
dotnet run --project tools/NonetMusicPlayerPlugin.Packager -c Release -- validate publish/developer.hello-world.impp
```

只分发 `.impp`；包内只有运行时引用的清单、页面、配置规范和必要入口，不包含 SDK、测试、工程或用户数据。修改已安装插件时，应先关闭并卸载旧版本再导入新包，源 `.impp` 与工程不会被播放器修改。

页面插件可使用文档列出的受限原生部件、播放/搜索动作、菜单贡献和消息流程；不能运行任意 C#、Python、JavaScript、HTML 或 XAML。音源插件是独立进程，协议见参考文档；CLI 只能启用音源插件，不能显示图形页面。

## SDK 与版本管理

`sdk/SDK_VERSION.json` 记录 SDK、目标播放器和协议版本。此工程可独立构建，不要求另一个播放器源码目录存在。开发插件时不要修改 sdk：需要新接口时先升级官方基础工程，再修改 plugin。

工程采用本地 Git；提交插件源码与文档，不提交 `bin/obj`、生成包、凭据、真实服务器密钥或音乐文件。SDK 同步和每次验证记录见 [CHANGELOG](CHANGELOG.md)。
