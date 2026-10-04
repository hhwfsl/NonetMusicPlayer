# 本地化资源规范

资源位于 `src/NonetMusicPlayer.Core/Localization/Resources`，桌面与 CLI 共用。采用跨平台 JSON 而非 Windows 专用 RESW；概念与资源 UID 一致。文件按功能命名，例如 Playback、Playlists、Lyrics、Settings、Plugins、Statistics、Home、Terminal。诊断文案可放同功能的 Diagnostics 文件，不以发行版本命名。

```json
{
  "Playback.Play": {
    "zh-CN": "播放",
    "en-US": "Play",
    "ja-JP": "再生"
  }
}
```

ID 使用 `功能.语义名称` 的英文稳定标识，不依赖中文或英文显示文案；更改文案不改 ID。三种语言必须完整，参数占位符必须一致。编号式临时 ID 不用于新资源。迁移中为区分语义重复使用的少量短哈希后缀属于稳定 ID，并非发行版本。

桌面 C# 使用 `L10n.T("Playback.Play")`，参数文案使用 `L10n.Format`；XAML 使用 `{ui:Loc Key='Playback.Play'}` 动态资源。核心及 CLI 使用 `LocalizationCatalog.Get/Format`。即时语言切换更新资源、可重建页面与动态状态；快捷键动作保存资源 ID，而非初始化时的翻译结果。

用户歌单名称、歌曲标签、文件路径、插件提供的自由文本不得按显示文案自动反查翻译。插件动作 label 可以显式使用宿主资源 ID（如 `Common.Next`）；自定义文本保持原样，插件作者自行组织其语言内容。

禁止新增按中文原文匹配的翻译字典、中文作为查询键，或以 beta 批次命名代码和资源。版本号仅保留在发布记录、验收证据与发行文件名中。

验证执行 UI 检查中的资源审计：检查英文 ID、三个语言值、占位符及 XAML 引用，同时实际切换页面验证长文字省略和完整提示。
