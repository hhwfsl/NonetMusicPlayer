# 桌面发行验证

版本：Nonet 0.4.0-beta.37。插件开发 SDK：4.16.0；程序集兼容基线：4.0.0.0。

## 构建与平台

使用 .NET SDK 10.0.401、Release 配置和独立暂存目录构建。发行包核对原生依赖架构、完整 SHA-256、许可证与 Unix 执行权限。

| 目标 | 发行形式 | 验证范围 |
| --- | --- | --- |
| Windows x64 | ZIP、installer | 程序运行、插件加载、安装和卸载 |
| Windows x86 | ZIP、installer | Windows x64 的 WOW64 环境运行、安装和卸载 |
| Windows ARM64 | ZIP、installer | 交叉构建与二进制检查 |
| Linux x64、ARM64 | ZIP | 交叉构建与二进制、权限检查 |
| macOS x64 | ZIP | 交叉构建与二进制、权限检查 |

交叉构建不能代替目标设备上的音频、图形、窗口管理器及高 DPI 验收。

## 回归范围

- SQLite 迁移、恢复与实际聆听计数；七天/二十四小时窗口、边界补零及旧数据兼容。
- 原版与完整界面方案的分类页标题操作、回顶、明暗主题、动态主题色及窄窗口布局。
- 歌词首帧定位、双向转场、取消和连续切页；动画结束不闪回起点。
- 冻结 Contract v1/v2、同 ID 更新、配置保留、权限拒绝与兼容适配。
- 完整界面重组、设置临时接管及撤销恢复；插件原生页面重挂接后保留实例。
- 模板直接引用宿主 SDK、六种单平台插件包、可选选择器和统计图表能力。

## 发行包验收

Windows 安装器载荷与对应纯净 ZIP 逐文件核对；隔离卸载保留非程序数据。应用更新包内含版本、RID 与逐文件摘要，不能混用其他架构的 ZIP 或安装器。

开发者可使用 scripts/verify-native-architecture.ps1 和 scripts/verify-clean-desktop.ps1 重复检查发行包。插件接口和测试入口见 [PLUGIN_DEVELOPMENT](PLUGIN_DEVELOPMENT.md) 与 [UNIVERSAL_EXTENSIONS](UNIVERSAL_EXTENSIONS.md)。
