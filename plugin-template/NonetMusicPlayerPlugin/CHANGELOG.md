# Changelog

## 3.0.0 — 2026-10-04

- 工程更名为 NonetMusicPlayerPlugin，适配 Nonet 0.4.0-beta.1。
- SDK C# 命名空间与程序集更名为 NonetMusicPlayer.PluginSdk，SDK 版本升为 3.0.0。
- 保留 Contract v1 / 页面 Schema v1，声明式 impp 格式不变。
- 项目及模板采用 GPL-3.0-only；打包器自动保留插件中的许可文件。
- 同步接口参考文档与版本摘要；基础工程仍可独立、无外部依赖构建。

## 2.0.0 — 2026-10-04

- 重建为可独立构建的 Hello World 插件基础工程，不再附带 samples。
- 内置与 NonetMusicPlayer 0.3.0-beta.6 共用源码的 Plugin SDK 2.0.0；Contract / 页面 Schema 仍为 v1。
- 打包前完整验证配置、权限、原生部件和安全路径；只加入运行时所需文件。
- 提供 pack、validate、--force、--source、--output 和音源依赖 --include。
- 默认构建无外部 NuGet 包、无播放器/UI/音频依赖。
- 增加入门教程、配置及生命周期文档、自动检查和 SDK 同步信息。

## 1.0.0 — 2026-10-01

- 初始声明式插件示例工程，包含桌宠和贪吃蛇打包器。
