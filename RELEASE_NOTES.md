# InputBeacon 1.4.0 · 兼容接口与贴近光标的气泡

- 针对资源管理器现代输入框补充 TextPattern2 光标读取、跨进程控件识别和 MSAA 回退。
- 增加 IDEA / Java 光标桥接。右键通知区域图标 → **IDEA / Java 光标支持…** → **启用 Java 光标支持**；保存工作后完全退出并重新打开 IDEA。如果仍无提示，在 IDEA 外观设置中开启 **Support screen readers**。
- 跟随提示缩小到 **72 × 34** 逻辑像素，贴近光标，加入浅色半透明圆角气泡、平滑尖角与柔和阴影。
- 保留切换后 1～60 秒消失、一直跟随、颜色设置及三种显示方式独立开关。升级保留已有设置。

下载 **[InputBeacon.exe](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.0/InputBeacon.exe)** 双击运行，或下载 **[便携 ZIP](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.0/InputBeacon-v1.4.0-portable.zip)**。ZIP 内为 `键盘状态.exe` 和中文使用说明；两个 EXE 内容相同。先退出旧版，再替换文件。

需要 Windows 10 / 11 和 .NET Framework 4.8。程序未签名，校验值见 `SHA256SUMS.txt`。

已通过本地回归和真实 Java 输入框的 6 阶段桥接检查（水平移动、换行、文末、空文本及恢复输入），验证运行环境为 IDEA 2024.1.7 自带的 64 位 JetBrains Runtime。IDEA 编辑区与资源管理器的完整气泡显示效果尚未完成端到端验收；Java 桥接必须先启用，部分应用或终端未提供光标接口时仍无法跟随。详细使用方法与兼容范围见 README。