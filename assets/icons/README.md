# 应用图标

`app-icon.svg` 是从 `assets/design/forest-character-draft-v4.svg` 提取的纯路径头部图标，保留该版本的角色与花饰，不含嵌入位图。头部轮廓范围为原图坐标 `(870, 0)` 至 `(1633, 732)`，画布中心与轮廓中心均为 `(1251.5, 366)`；透明正方形画布边长 860，默认显示尺寸 1024 × 1024，可无损缩放。

Windows EXE、窗口及托盘要求 ICO；macOS 应用包要求 ICNS。这些平台文件由同一个 SVG 生成，不能将 SVG 直接写入系统原生图标字段。`native` 中的 PNG 仅用于构建 macOS 图标，不作为未使用资源装入播放器。Windows ICO 的 16–256 px 多尺寸版本位于桌面项目 `Assets/icon.ico`。

构建工具为 `scripts/build-vector-icon.py` 和 `scripts/render-app-icon.cjs`。前者使用参考预览识别裁切轮廓，但图形内容全部来自原有 SVG 路径；后者生成平台必要的位图表示。工具和完整绘画不随最小运行包发布。
