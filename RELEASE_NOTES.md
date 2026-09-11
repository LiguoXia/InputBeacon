# InputBeacon 1.4.1 · 跟随气泡误触发修复

- 针对微信使用微软拼音时的误弹路径：忽略输入状态读取失败及恢复、全角标记波动、窗口或控件焦点变化；中英文变化稳定至少 200 ms 后才提示。
- 实际 A / a 变化立即提示；输入、移动光标、启动或重新应用设置不会额外延长定时气泡。“一直显示”仍持续跟随。
- 补充资源管理器等现代 UIA 输入框在空光标范围时的相邻字符定位回退，避免该回退将空行定位到上一行；保持后台定位连接，改善 1 秒短倒计时的首次定位。
- 保留 72 × 34 逻辑像素的贴近光标气泡、浅色半透明圆角底、平滑尖角及阴影；保留三种显示方式独立开关与所有外观偏好。
- 复制状态诊断增加提示时长、触发次数和原因，不包含输入文本。

下载 **[InputBeacon.exe](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.1/InputBeacon.exe)** 双击运行，或下载 **[便携 ZIP](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.1/InputBeacon-v1.4.1-portable.zip)**。ZIP 内含相同程序 `键盘状态.exe`、中文使用说明和版本记录。先退出旧版再替换 EXE，已有设置保留。

**IDEA 首次使用：**右键通知区域图标 → **IDEA / Java 光标支持…** → **启用 Java 光标支持**，保存工作后完全退出并重开 IDEA；必要时在 IDEA 外观设置中开启 **Support screen readers**。桥接代码已通过独立 Java 输入框 6 阶段检查，使用 IDEA 2024.1.7 自带的 64 位 JetBrains Runtime。

**验证范围：**本版通过输入状态、误触发过滤、倒计时、空 UIA 范围、真实 WinForms 光标、透明合成和设置等自动回归。尚未完成新版在微信 + 微软拼音、IDEA 编辑区及资源管理器地址栏 / 搜索框的端到端验收；不将自动回归替代实机结论。

需要 Windows 10 / 11 和 .NET Framework 4.8。程序未签名，发布文件校验值见 `SHA256SUMS.txt`。详细使用方法见仓库 README。
