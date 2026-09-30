# 微信 4.1.15.13 空白聊天输入框验证

## 复现与原因

本机运行微信 4.1.15.13 和 InputBeacon 1.4.4。单击空白聊天框并切换中英文时没有跟随提示；再次点击后才出现。

只读诊断确认 UI Automation 焦点已经是 Qt 的 `mmui::ChatInputField`（Edit 类型），但没有 Win32 caret HWND，UIA 折叠范围及扩展范围也没有可用几何信息。后续点击才出现 Win32 光标。诊断未读取控件 Name、Value、聊天文字或文本范围内容，也未模拟输入。

## 处理方式

优先保留真实 Win32 / MSAA / UIA 光标路径。只有精确匹配已聚焦、启用、可见且非密码的微信聊天编辑框，并通过文本范围端点比较确认文档为空、插入范围折叠在空文档中时，允许使用编辑框上边缘作为提示锚点。查询完成再次检查前台窗口和焦点标识。该锚点不是实际插入光标坐标；诊断明确标记 `WeChat empty input field anchor (caret unavailable)`。

语音按钮、其他 Qt 控件、非空文档、非折叠选择或未知属性均不使用此回退。只比较范围端点，不调用 GetText / Value，不移动真实选择。真实光标恢复后自动优先采用；控件标识保持一致，不延长倒计时。

Microsoft 说明空文本范围可能返回空几何数组：[GetBoundingRectangles](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-getboundingrectangles)。这不能单独证明具体应用的故障；上述原因依据本机实测。

## 验证结果

- 197 项功能检查全部通过，新增微信控件筛选、空文档 / 插入范围、错误几何、语音 / 密码 / 隐藏控件排除和回退转真实光标不续时检查。
- 原有真实 WinForms / RichEdit、UIA COM、非激活气泡、资源释放和状态计时检查通过。一轮真实窗口测试因前台焦点检查失败中断，完整重跑通过；该检查依赖交互桌面的焦点。
- 本机真实微信：尚无 caret HWND 且旧读取器无几何时，新版返回已聚焦空白编辑框锚点；后台跟随采样随后正常取得该锚点。
- 用户复测确认“首次点击和切换都能显示”。
- 没有验证所有微信版本 / 布局，没有重测 Java / IDEA 或多天内存占用。原始本机诊断仅保存在忽略的 build 目录，不作为发布附件。

功能检查全文见 [1.4.5 测试结果](test-results/1.4.5.txt)。
