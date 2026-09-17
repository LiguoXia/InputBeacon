# InputBeacon 1.4.2 · 切换输入框时显示当前状态

开启光标跟随并设置“若干秒后消失”后，从浏览器搜索框切换到微信输入框，即使没有切换中英文或大小写，也会显示一次当前状态，并按设置的秒数消失。切回之前的输入框也会重新提示。

- 新输入框的有效光标稳定约 200 ms 后显示，倒计时从确认显示时开始。
- 同一输入框内继续打字、移动光标不会重复触发；临时丢失光标、输入法未知状态恢复、全角标记波动也不会重新计时。
- 保留中英文 / A/a 变化提示，以及“一直显示”的持续跟随行为。
- 支持用 UI Automation 控件标识区分共享同一窗口的输入框；后台读取，不读取输入文本。未提供独立标识的自绘输入框，在同一窗口内切换时可能无法区分。

下载 **[InputBeacon.exe](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.2/InputBeacon.exe)** 双击运行，或下载 **[便携 ZIP](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.2/InputBeacon-v1.4.2-portable.zip)**。ZIP 内含相同程序 `键盘状态.exe`、中文使用说明和版本记录。

**升级：**先退出旧版再运行新版，已有颜色、显示方式和时长设置保留。测试过本地 1.4.2 验证版的用户无需重新替换程序，本次发布的 EXE 与验证版完全相同。

**验证：**通过 173 项自动检查，包括跨应用与虚拟控件切换、返回原输入框、慢定位、倒计时、防误触发，以及真实 Windows 文本框的 UIA 标识。不同浏览器、微信版本及自绘控件的兼容性仍取决于它们提供的光标接口。

IDEA 编辑区需要 Java Access Bridge：右键图标 → **IDEA / Java 光标支持…**，启用后保存工作并完全退出、重开 IDEA，必要时开启 IDEA 外观设置中的 **Support screen readers**。

需要 Windows 10 / 11 和 .NET Framework 4.8。程序未签名，文件校验值见 `SHA256SUMS.txt`。详细说明见仓库 README。
