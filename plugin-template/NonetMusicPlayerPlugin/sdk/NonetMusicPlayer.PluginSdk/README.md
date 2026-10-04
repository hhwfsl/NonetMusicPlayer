# NonetMusicPlayer.PluginSdk

SDK 3.0.0，清单 Contract v1 / 页面 Schema v1。仅依赖 .NET 10 BCL，无播放器、UI、音频和 NuGet 包依赖。源码采用 GPL-3.0-only。

`PluginManifest`、`PluginPageContract`、`PluginConfigSchema`、`PluginPathPolicy`、`PluginPackageInspector` 是宿主与开发工具使用的同一套校验。为兼容既有调用，公开类型命名空间仍是 `NonetMusicPlayer.Core.Plugins`，实际程序集已独立为 `NonetMusicPlayer.PluginSdk`。

```csharp
using NonetMusicPlayer.Core.Plugins;

// 只加入清单引用的文件，在临时包中验证，成功后替换输出。
var path = PluginPackageBuilder.Pack("plugin", "publish/hello.impp", overwrite: true);
var manifest = PluginPackageInspector.Inspect(path);
```

默认禁止覆盖；overwrite 只能作用于生成包。音源额外运行时依赖通过 additionalFiles 指定安全相对路径。声明式页面不得夹带脚本或可执行文件。JSON 类型信息使用 Source Generation。

`PluginMessages.Translate` 是可选的资源查询委托，播放器注入其本地化目录；独立工具使用英文后备提示。运行时网络、进程、页面创建及生命周期调度由宿主完成，SDK 只定义和验证 Contract，不执行插件。

基础工程携带本目录的源码快照。维护者修改 Contract 时必须同步 SDK_VERSION.json 的校验摘要，并在基础工程和目标播放器中验证生成包。

打包器自动保留根目录的 LICENSE / LICENSE.txt / COPYING / NOTICE / THIRD_PARTY_NOTICES.txt。声明式 UI 只允许这些固定名称、256 KiB 以内的 UTF-8 文本；不会运行它们，脚本及其他可执行资源依然拒绝。
