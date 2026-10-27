# Android 第三方组件

本 APK 使用以下开源组件；其版权归相应作者。测试工具与 Gradle / SDK 不进入发布包。请勿将第三方版权解释为对本产品的背书。

| 组件 | 许可证 | 来源 |
| --- | --- | --- |
| AndroidX Core、Activity、Lifecycle、DocumentFile | Apache-2.0 | https://developer.android.com/jetpack/androidx |
| Jetpack Compose UI / Foundation、Material Icons（仅矢量图标资源） | Apache-2.0 | https://android.googlesource.com/platform/frameworks/support/ |
| AndroidX Media3 | Apache-2.0 | https://github.com/androidx/media |
| Kotlin 标准库、kotlinx.coroutines | Apache-2.0 | https://github.com/JetBrains/kotlin / https://github.com/Kotlin/kotlinx.coroutines |
| OkHttp、Okio | Apache-2.0 | https://github.com/square/okhttp / https://github.com/square/okio |
| Coil | Apache-2.0 | https://github.com/coil-kt/coil |
| Guava、ListenableFuture | Apache-2.0 | https://github.com/google/guava |
| Checker Framework 注解 | MIT | https://github.com/typetools/checker-framework |
| GitHub Octicons mark-github-16 | MIT | https://github.com/primer/octicons |
| Bouncy Castle X25519 | MIT | https://github.com/bcgit/bc-java |
| ZXing Core（二维码生成） | Apache-2.0 | https://github.com/zxing/zxing |

完整 Apache-2.0 文本：https://www.apache.org/licenses/LICENSE-2.0 。各项目原始版权 / NOTICE 见上述源代码仓库。SQLite 使用 Android 系统提供的实现；平台组件不复制进 APK。应用角色图标为独立美术资源，并非上述组件的一部分。

GitHub 图标复用官方原始矢量路径，仅适配尺寸和前景色；完整许可见 [Octicons-MIT](docs/licenses/Octicons-MIT.txt)。

设备互听使用 Bouncy Castle 和 ZXing；对应许可随 APK 提供。
