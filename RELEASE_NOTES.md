# InputBeacon 1.4.5 · 微信空白输入框兼容更新

修复微信 4.1.15.13 首次进入空白聊天输入框时，必须打字或再次点击才能显示输入状态提示的问题。

- 已聚焦的空白聊天编辑框没有发布光标坐标时，先在输入框上边缘显示提示；中英文切换也可触发提示。
- 取得真实光标坐标后自动恢复跟随，同一输入框内不重新延长倒计时。
- 回退只匹配微信 Qt 聊天编辑框，并检查空文档、折叠插入范围、可见性和焦点；语音按钮、密码框、隐藏控件不启用此回退。
- 不读取聊天内容，不模拟点击、按键或移动选择，保留已有偏好。

**下载：**[InputBeacon.exe](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.5/InputBeacon.exe)，或 [便携 ZIP](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.5/InputBeacon-v1.4.5-portable.zip)。ZIP 内程序名为 `键盘状态.exe`；校验值见 `SHA256SUMS.txt`。

**验证：**197 项功能检查通过。本机微信 4.1.15.13 捕获到旧版缺失光标坐标、新版取得空白输入框定位的差异；用户复测确认首次点击和中英文切换均能显示。其他微信版本及布局未全部验证，本次未重测 Java / IDEA 或长时间内存压力。详见 [验证记录](https://github.com/LiguoXia/InputBeacon/blob/v1.4.5/docs/wechat-1.4.5.md)。

**升级：**先退出旧版，再运行新版，已有设置保留。需要 Windows 10 / 11 和 .NET Framework 4.8。程序未签名；macOS 版本保持独立。
