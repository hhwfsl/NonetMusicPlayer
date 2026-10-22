# Windows 安装包

安装包位于 `publish/desktop/windows_installer`，x64、x86、ARM64 每个架构只保留最新成功构建的安装器；旧安装器可在 `artifacts/releases/installers` 开发归档中找回。每个架构另有纯净 ZIP，位于 `publish/desktop/archives`。支持 Windows 10 22H2 / Windows 11 的对应架构，不提供 Windows ARM32。

默认选择与设备匹配的原生架构：常规 64 位 PC 选 x64，32 位 Windows 选 x86，Windows on ARM 选 ARM64。安装器文件名分别以 `windows-x64-setup.exe`、`windows-x86-setup.exe`、`windows-arm64-setup.exe` 结尾；ARM64 安装器拒绝在非 ARM64 系统安装。

x64 沿用原安装标识及 `NonetMusicPlayer` 默认目录，x86/ARM64 使用带架构后缀的独立标识和目录，避免不同架构 DLL 混装。不要手动将不同架构的 ZIP 叠加解压到同一目录。软件内更新只更新当前进程 RID，不自动转换架构；切换架构前先备份数据，插件也需相应架构支持。

## 安装、升级与卸载

默认按当前用户安装，无需管理员权限。安装目录必须可写，因为应用数据默认保存在软件目录的 `Data` 文件夹。可在应用设置中另选数据和备份目录。

安装包只包含程序、依赖、使用手册和第三方许可，不包含打包机器的用户数据。升级保留已有数据。卸载移除程序、快捷方式及其卸载登记，保留 `Data` 和 `Nonet.bootstrap.json`，不删除原始音乐和外部数据目录。重新安装到相同目录可以继续使用保留的数据；重要数据仍应另行备份。

安装程序不会自动修改默认音频打开方式。安装后可在应用设置中注册音频支持，再在系统默认应用中选择 Nonet。

当前预发布安装包未配置商业代码签名。Windows 可能显示来源或信誉警告，请核对来源，不要绕过对未知安装包的安全检查。

## 构建

需要 .NET SDK 和 Inno Setup 6 编译器。正常发布 Windows 时会同步生成安装器：

```powershell
./scripts/publish-desktop.ps1 -Platforms windows
./scripts/publish-desktop.ps1 -CleanOnly -Rids win-x86,win-arm64
```

已发布当前版本的便携程序时，可以仅重建安装器，不替换正在运行的播放器：

```powershell
./scripts/build-windows-installer.ps1 -CompilerPath 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
./scripts/build-windows-installer.ps1 -Architecture arm64 -PackageDirectory 'artifacts/纯净暂存/package'
```

普通 Windows 发布不允许 `-SkipInstaller`，防止便携版更新而安装器落后。`-CleanOnly` 默认也从纯净暂存包生成安装器；若仅需 ZIP 且不更新任何便携发布目录，可显式使用 `-CleanOnly -SkipInstaller`。安装器脚本支持 `-PackageDirectory` 指向纯净 Windows 包目录。编译器也可以放在项目的 `artifacts/toolchain/inno-6.7.3/package/tools` 下。编译脚本只使用编译器，不自动安装工具。

构建先检查项目、实际程序版本及全部必需原生库架构，再输出至独立暂存目录。编译及版本检查通过后才归档同架构旧安装器并替换当前文件，不删除其他架构最新文件；编译失败保留原安装器。安装器使用明确的程序文件白名单，不打包便携目录中的 Data、引导配置或插件。

安装配置为 `scripts/windows-installer/NonetMusicPlayer.iss`。Inno Setup 支持语言消息文件的继承，简体中文消息使用项目内的翻译覆盖英文默认消息，另提供英文和日文。参见 [Inno Setup 官方文档](https://jrsoftware.org/ishelp/topic_languagessection.htm)。

`/LMPQA=1` 是隔离安装验证参数，使用不同的卸载登记标识；仅用于项目测试目录，不应用于正常安装。
