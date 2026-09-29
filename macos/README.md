# 键盘状态 · InputBeacon for macOS

基于 Windows 1.4.4 的交互与跟随计时逻辑实现的原生 Swift / AppKit 版本。支持 **macOS 13 Ventura 或更高版本**，Universal 2 程序包含 **Intel x86_64 和 Apple Silicon arm64**，无需 Rosetta、.NET 或其他运行时。

## 安装

1. 从 [macOS 1.0.0 发布页](https://github.com/LiguoXia/InputBeacon/releases/tag/macos-v1.0.0) 下载 `InputBeacon-macOS-1.0.0-universal.dmg`。
2. 打开 DMG，将 InputBeacon 拖入 Applications（应用程序），弹出 DMG 后从应用程序启动。ZIP 版解压后同样移动到应用程序。
3. 首次运行显示透明悬浮窗，菜单栏保留 `⌨` 设置入口。
4. 开启“光标跟随显示”时，按提示在“系统设置 → 隐私与安全性 → 辅助功能”中允许 InputBeacon。固定悬浮窗与菜单栏状态无需此权限。

此版本使用临时（ad-hoc）签名，**未使用 Apple Developer ID 签名、未公证**。Gatekeeper 可能阻止首次打开。核对仓库来源及 `SHA256SUMS.txt` 后，可通过“系统设置 → 隐私与安全性 → 仍要打开”允许此应用。受组织管理的 Mac 可能不允许使用未公证应用。不要关闭系统整体安全保护。参考 [Apple：打开未知开发者的 Mac App](https://support.apple.com/guide/mac-help/mh40616/mac)。

## 功能与操作

- **透明悬浮窗**：拖动调整位置，右键设置；支持鼠标穿透、重置位置、80%～150% 大小及 45%～100% 不透明度。
- **菜单栏状态**：可显示“中 / 英 / 语 / ?”及“A / a”，关闭后仍保留设置入口。
- **输入光标跟随**：始终穿透，不抢焦点。进入新的输入框或实际大小写变化时显示；输入源变化需稳定 200 ms。支持一直显示，或 1～60 秒后隐藏，默认 3 秒。同一框内打字、移动光标不会续时。
- **颜色**：支持 3 位 / 6 位 HEX，自定义颜色同步应用到悬浮窗、菜单栏状态和跟随提示，可恢复默认配色。
- **登录启动**：通过 macOS 的登录项管理启用；建议先将应用放入 Applications。
- **诊断**：菜单“复制状态诊断”只包含版本、输入源 ID、修饰键状态、辅助功能权限与跟随触发原因。

大小写按 Caps Lock 与 Shift 异或计算。小圆点表示 Caps Lock 开启，↑ 表示按住 Shift。应用、输入法或键盘映射可能改变实际输出。

## 与 Windows 版的差异

macOS 版的“中 / 英”表示**系统当前输入源的语言**：中文为“中”、英文为“英”、其他语言为“语”、无法读取为“?”。第三方输入法在同一个输入源内部切换中英文时，可能不更新系统公开状态；本工具无法保证识别这些内部模式。全角模式也不显示。不要将“中”视为对实际将输出中文的保证。

跟随定位使用 macOS Accessibility 的选区索引与零长度范围几何信息，不读取输入文本。选中文本、安全输入框、自绘终端、未公开插入光标几何信息的网页 / 应用不显示跟随提示，也不使用鼠标位置或整个输入框猜测光标。Java / IDEA 是否支持取决于其 macOS 辅助功能实现；Windows Java Access Bridge 不适用于此平台。

辅助功能调用在串行后台队列执行，带超时且不会堆积请求。旧样本超过 350 ms 或前台应用变化即失效；约每 100 ms 刷新。界面采用原生 Retina 绘制，无键盘事件监听器、无网络请求、不记录输入内容。

## 偏好与卸载

偏好使用 `UserDefaults`，域名 `com.liguoxia.InputBeacon`，独立于 Windows 偏好。卸载前关闭“登录时启动”，退出应用，再删除应用。需重置偏好时，退出应用后运行：

```sh
defaults delete com.liguoxia.InputBeacon
```

## 从源码构建

需要 Mac 及支持 Swift 5.9+ 的 Xcode / Command Line Tools。无第三方依赖。

```sh
cd macos
swift test
bash build.sh
```

`dist/` 包含 Universal `.app`、DMG、ZIP 与 SHA-256 校验文件。构建脚本分别编译 arm64 / x86_64，再使用 `lipo` 合并并验证两个切片，临时签名后生成安装包。

GitHub Actions 在 Apple Silicon 和 Intel macOS runner 上分别执行核心测试、双架构编译、签名校验和本机 `--self-check` 启动检查。推送 `macos-v*` 标签时，在两组检查通过后发布安装包，不改变 Windows 最新版标记。

自动化检查覆盖大小写、输入源语言、输入框切换、未知状态、200 ms 防抖、轮询暂停、定时隐藏和多显示器坐标 / 边缘避让。CI 的 `--self-check` 只确认二进制可执行，不等于实际用户会话中已验证辅助功能、输入法或登录启动。发布前后可使用 [人工验收清单](QA.md) 进行真实 Mac 验证。

接口依据：[Apple Accessibility](https://developer.apple.com/documentation/applicationservices/axuielement)、[AX 调用超时](https://developer.apple.com/documentation/applicationservices/1459345-axuielementsetmessagingtimeout)、[SMAppService](https://developer.apple.com/documentation/servicemanagement/smappservice)。
