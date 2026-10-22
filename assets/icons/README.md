# 应用图标

唯一源图为 `app-icon-source.png`，完整保留人物和音乐符号，不裁切、不重绘。图标使用 3.5% 透明边距和 20% 相对圆角，在不同系统的图标容器内留出适度空间。

运行以下命令重新生成原生表示：

```powershell
dotnet run --project tools/NonetMusicPlayer.IconBuilder -c Release -- .
```

- Windows：`src/NonetMusicPlayer.Desktop/Assets/icon.ico`，内含 16/24/32/48/64/128/256 px PNG 载荷；EXE、窗口、托盘和文件关联共用。
- macOS：发布脚本用 `native/app-icon-256.png`、512 和 1024 生成 ICNS。
- Linux：应用与文件关联使用同一原生图标。

源图与生成工具用于源码构建，运行包仅包含各平台所需的原生图标。
