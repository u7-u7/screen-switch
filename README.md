# 屏幕切换 · ScreenSwitch

一个常驻桌面的显示设置工具，支持 Windows 和 macOS。看电视时只亮外接屏，写代码时再切回扩展。鼠标移到悬浮球上就能操作。

![屏幕切换图标](logo.svg)

## Windows 下载与使用

到 [GitHub Releases](https://github.com/u7-u7/screen-switch/releases/latest) 下载 Windows 压缩包，解压后运行 `ScreenSwitch.exe`。适用于 Windows 10 和 Windows 11。

- **显示模式**　切换扩展、复制或仅选中屏，按显示器调整分辨率、刷新率和方向。
- **多屏组合**　勾选要点亮的显示器，让选中的屏幕扩展。拖动卡片调整排列，应用后可在 15 秒内确认或恢复。
- **投屏小窗**　在同一台电脑上实时预览另一块屏幕。窗口可跨屏拖动、置顶或全屏，切换来源时保留小窗位置。
- **悬浮球**　贴边后收起，鼠标触碰时展开。重启后恢复上次位置。右键菜单和系统托盘都能隐藏或显示悬浮球。
- **快捷键和外观**　默认 `Ctrl + Alt + E`，可自定义。配色面板支持五种配色和 0–100% 磨砂透明度。

压缩包内的 `屏幕切换图解.html` 可以离线查看。[50 秒中文介绍视频（MP4）](https://github.com/u7-u7/screen-switch/releases/download/v1.0.0/ScreenSwitch-Intro-zh.mp4) 演示了显示模式、多屏组合、投屏小窗和贴边收纳。

## macOS 使用

支持 macOS 13 及以上，安装包包含 Apple 芯片和 Intel 两种版本。从源码构建后，双击打开 `dist/ScreenSwitch.app`，从菜单栏的双屏图标进入。也可以点悬浮球或按 `Control + Option + E`。在偏好设置里可更换快捷键、隐藏悬浮球。

- **显示模式**　扩展全部，或只点亮勾选的屏幕。修改后可在 15 秒内保留或恢复，超时自动恢复。
- **排列与画质**　拖动屏幕卡片调整排列，从每块屏幕的菜单切换分辨率和刷新率。屏幕旋转可在 macOS 系统设置中调整。
- **投屏小窗**　在当前屏幕打开面板，点「投屏」并选择另一块屏幕。来源画面会铺满当前屏幕上的圆角小窗，小窗随来源保持横屏或竖屏比例。小窗可跨屏拖动，鼠标移上去时可切换来源、置顶、全屏或关闭。第一次使用需要在「系统设置 → 隐私与安全性」授予屏幕录制权限，然后重新打开应用。
- **悬浮球**　贴边时露出蓝色半球，鼠标移上去时展开。可拖到其他屏幕，重开应用时恢复位置。

单独开关显示器依赖 macOS 的内部接口，能否使用取决于系统版本和硬件。应用会在接口不可用时隐藏单独开关操作。显示配置在当前登录会话内生效。

## 从源码构建

### Windows

在 Windows PowerShell 中进入仓库目录，然后运行：

```powershell
.\build.ps1
```

脚本使用 Windows 的 .NET Framework C# 编译器，生成 `dist\ScreenSwitch.exe`。

### macOS

安装 Xcode 或 Xcode 命令行工具后，在仓库目录运行以下命令。

```bash
./build-macos.sh
```

脚本生成 `dist/ScreenSwitch.app` 和 `dist/ScreenSwitch-macOS.zip`。默认使用临时签名，重新构建后 macOS 可能要求再次授予屏幕录制权限。本机开发时可通过 `SCREENSWITCH_SIGNING_IDENTITY` 指定钥匙串中的同一份 Apple Development 签名证书，以便后续构建沿用授权。正式发布可使用 Developer ID 签名并公证。
