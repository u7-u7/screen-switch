# 屏幕切换 · ScreenSwitch

一个常驻桌面的 Windows 显示设置工具。看电视时只亮外接屏，写代码时再切回扩展。鼠标移到悬浮球上就能操作。

![屏幕切换图标](logo.svg)

## 下载与使用

到 [GitHub Releases](https://github.com/u7-u7/screen-switch/releases/latest) 下载 Windows 压缩包，解压后运行 `ScreenSwitch.exe`。适用于 Windows 10 和 Windows 11。

- **显示模式**　切换扩展、复制或仅选中屏，按显示器调整分辨率、刷新率和方向。
- **多屏组合**　勾选要点亮的显示器，让选中的屏幕扩展。拖动卡片调整排列，应用后可在 15 秒内确认或恢复。
- **投屏小窗**　在同一台电脑上实时预览另一块屏幕。窗口可跨屏拖动、置顶或全屏，切换来源时保留小窗位置。
- **悬浮球**　贴边后收起，鼠标触碰时展开。重启后恢复上次位置。右键菜单和系统托盘都能隐藏或显示悬浮球。
- **快捷键和外观**　默认 `Ctrl + Alt + E`，可自定义。配色面板支持五种配色和 0–100% 磨砂透明度。

压缩包内的 `屏幕切换图解.html` 可以离线查看。[50 秒中文介绍视频（MP4）](https://github.com/u7-u7/screen-switch/releases/download/v1.0.0/ScreenSwitch-Intro-zh.mp4) 演示了显示模式、多屏组合、投屏小窗和贴边收纳。

## 从源码构建

在 Windows PowerShell 中进入仓库目录，然后运行：

```powershell
.\build.ps1
```

脚本使用 Windows 的 .NET Framework C# 编译器，生成 `dist\ScreenSwitch.exe`。
