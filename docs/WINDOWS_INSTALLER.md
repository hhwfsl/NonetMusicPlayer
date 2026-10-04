# Windows 安装包

安装包位于 `publish/desktop/windows_installer`，便携版本仍位于 `publish/desktop/windows`。支持 Windows 10 22H2 / Windows 11 x64。

## 安装、升级与卸载

默认按当前用户安装，无需管理员权限。安装目录必须可写，因为应用数据默认保存在软件目录的 `Data` 文件夹。可在应用设置中另选数据和备份目录。

安装包只包含程序、依赖、使用手册和第三方许可，不包含打包机器的用户数据。升级保留已有数据。卸载移除程序、快捷方式及其卸载登记，保留 `Data` 和 `Nonet.bootstrap.json`，不删除原始音乐和外部数据目录。重新安装到相同目录可以继续使用保留的数据；重要数据仍应另行备份。

安装程序不会自动修改默认音频打开方式。安装后可在应用设置中注册音频支持，再在系统默认应用中选择 Nonet。

当前预发布安装包未配置商业代码签名。Windows 可能显示来源或信誉警告，请核对来源，不要绕过对未知安装包的安全检查。

## 构建

需要 .NET SDK 和 Inno Setup 6 编译器。先发布 Windows 便携程序，再编译安装包：

```powershell
./scripts/publish-desktop.ps1 -Platforms windows -SkipInstaller
./scripts/build-windows-installer.ps1 -CompilerPath 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

默认发布脚本会在发布 Windows 后构建安装包；没有编译器时可使用 `-SkipInstaller`。编译器也可以放在项目的 `artifacts/toolchain/inno-6.7.3/package/tools` 下。编译脚本只使用编译器，不自动安装工具。

安装配置为 `scripts/windows-installer/NonetMusicPlayer.iss`。Inno Setup 支持语言消息文件的继承，简体中文消息使用项目内的翻译覆盖英文默认消息，另提供英文和日文。参见 [Inno Setup 官方文档](https://jrsoftware.org/ishelp/topic_languagessection.htm)。

`/LMPQA=1` 是隔离安装验证参数，使用不同的卸载登记标识；仅用于项目测试目录，不应用于正常安装。
