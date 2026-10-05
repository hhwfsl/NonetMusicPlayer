# NonetMusicPlayer.PluginSdk

SDK 3.3.0，清单 Contract v1 / 页面 Schema v1。该 SDK 由 Nonet 主项目维护，只依赖 .NET 10 BCL，无桌面 UI、音频和外部 NuGet 包依赖。源码采用 GPL-3.0-only。

`PluginManifest`、`PluginPageContract`、`PluginConfigSchema`、`PluginPathPolicy`、`PluginPackageInspector` 是宿主与开发工具使用的同一套校验。为兼容既有调用，公开类型命名空间仍是 `NonetMusicPlayer.Core.Plugins`，实际程序集已独立为 `NonetMusicPlayer.PluginSdk`。

```csharp
using NonetMusicPlayer.Core.Plugins;

// 只加入清单引用的文件，在临时包中验证，成功后替换输出。
var path = PluginPackageBuilder.Pack("plugin", "publish/hello.impp", overwrite: true);
var manifest = PluginPackageInspector.Inspect(path);
```

默认禁止覆盖；overwrite 只能作用于生成包。音源额外运行时依赖通过 additionalFiles 指定安全相对路径。声明式页面不得夹带脚本或可执行文件。JSON 类型信息使用 Source Generation。

`PluginMessages.Translate` 是可选的资源查询委托，播放器注入其本地化目录；独立工具使用英文后备提示。运行时网络、进程、页面创建及生命周期调度由宿主完成，SDK 只定义和验证 Contract，不执行插件。

独立模板通过 ProjectReference 依赖主项目本目录，不复制 SDK 或维护摘要快照。打包使用主项目 tools/NonetMusicPlayer.PluginPackager；开发时先取得主项目源码，并在真实宿主中验证插件。模板本次不提交 GitHub。

SDK 3.1 新增 `lyrics` 独立进程插件、`lyrics-search` 页面部件与 `lyrics.more / match-lyrics` 菜单动作。歌词进程只接收歌曲元数据，不接收源音频路径。`audio-tags` 是宿主写标签能力声明，不是进程沙箱；安装包无法预先授予 `AudioTagWriteConsent`。详见插件参考文档。

宿主发行版升级不改变已经支持的 Contract v1 / Schema v1，不要求旧插件重新打包。SDK 3.2 新增可选仓库元数据及共用 PluginUpdatePolicy；旧包缺失字段仍兼容。新插件源码清单声明 repositoryOwner / repositoryName，来源由宿主记录；更新保持 id、递增 version，完整规则见开发参考文档。

打包器自动保留根目录的 LICENSE / LICENSE.txt / COPYING / NOTICE / THIRD_PARTY_NOTICES.txt。声明式 UI 只允许这些固定名称、256 KiB 以内的 UTF-8 文本；不会运行它们，脚本及其他可执行资源依然拒绝。

SDK 3.3 新增可选 `platform` 清单字段与 `PluginPlatformPolicy`。打包器使用 `--rid win-x64 / osx-x64 / linux-x64` 分别生成本机平台包；安装与更新只保留当前平台和共享资源。SDK 保留原四参数 `PluginPackageBuilder.Pack` API，新增含 `rid` 的五参数重载；旧多平台包和旧 Contract v1 继续兼容。

独立进程入口可使用 Native AOT 发布，宿主仍通过相同 JSON-RPC 调用，不增加 Contract、SDK 版本或新的运行时权限。协议序列化采用显式源生成类型，构建和链接在目标系统进行；仅打包本机必要原生文件和许可。SDK 不承诺所有验证/开发工具方法均适用于 AOT，入口只引用所需 DTO/API，并处理裁剪分析警告。详细构建、兼容和验证要求见插件开发文档第 16 节。
