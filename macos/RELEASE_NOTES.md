# InputBeacon macOS 1.0.0

首个原生 macOS 版本，沿用 Windows 1.4.4 的显示与跟随计时逻辑。

- 支持 macOS 13+，同一安装包原生支持 Intel 和 Apple Silicon。
- 透明可拖动悬浮窗、菜单栏状态、输入光标旁的穿透提示。
- Caps Lock / Shift 大小写提示；输入框进入提示、200 ms 输入源防抖及 1～60 秒 / 持续显示。
- 显示大小、不透明度、HEX 文字颜色、登录启动、状态诊断。
- 通过 GitHub Actions 在 Intel 与 Apple Silicon 环境编译、测试、验证通用程序两个架构与本机命令启动。

## 下载

一般用户下载 `InputBeacon-macOS-1.0.0-universal.dmg`，拖入 Applications 后运行；ZIP 为相同程序的压缩版。`SHA256SUMS.txt` 用于校验。

## 首次使用及已知边界

- 临时签名，**没有 Apple Developer ID 签名或公证**。首次打开可能需要在“系统设置 → 隐私与安全性”中选择“仍要打开”。
- 光标跟随需“辅助功能”权限；固定悬浮窗 / 菜单栏可独立使用。
- “中 / 英”表示系统输入源语言，不能保证读取第三方输入法内部的中英切换，不显示全角状态。
- 仅在应用提供可靠插入光标位置时跟随；不读取输入文本，不使用鼠标位置猜测。不保证兼容所有终端、浏览器或 IDEA 版本。
- CI 测试不包含真实桌面辅助功能授权、第三方输入法、拖动操作和登录启动的人工验收。

详细说明：[macOS 使用说明](https://github.com/LiguoXia/InputBeacon/blob/macos-v1.0.0/macos/README.md)。Windows 版本及其最新下载入口保持独立。
