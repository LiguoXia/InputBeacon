# InputBeacon 1.4.4 · 内存与流畅度优化

减少持续轮询和光标跟随的临时对象、重复画面上传，并改善辅助功能接口的资源释放。

- 状态诊断改为按需生成，减少每 100 ms 轮询时的字符串分配。
- 气泡移动复用已有透明画面，减少位图创建、像素复制及上传；原有显示效果、倒计时和切换提示保留。
- 旧版 UIA 光标查询使用原生 COM，每次查询后释放接口和文本范围；不再依赖托管 UIA / WPF 程序集。
- Java 定位线程按需启动，Windows 光标查询空闲 30 秒后释放 UIA 连接；开启跟随后继续保持约 100 ms 轮询。
- “复制状态诊断”增加工作集、私有内存、托管堆、GDI / USER 数量与运行时长，便于排查长时间使用的内存变化。

**下载：**[InputBeacon.exe](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.4/InputBeacon.exe)，或 [便携 ZIP](https://github.com/LiguoXia/InputBeacon/releases/download/v1.4.4/InputBeacon-v1.4.4-portable.zip)。ZIP 内含相同程序 `键盘状态.exe`、中文说明、版本记录和性能测试记录；校验值见 `SHA256SUMS.txt`。

**升级：**先退出旧版，再运行新版，已有设置保留。发布的 EXE 与本次已验证的本地 1.4.4 优化版完全相同，已运行该版本的用户无需再次替换 EXE。

**验证：**通过 182 项功能检查和 11,000 次有效 UIA 光标查询（含预热）。同机短测中，状态轮询的累计临时分配减少约 98%，气泡移动减少约 54%，静止气泡更新减少约 61%。这些是累计分配量，不是常驻内存降幅；压力测试末工作集约 52 MiB，不能保证多天、多应用使用时始终保持该占用。本次未重新执行真实 Java / IDEA 集成测试。详见 [性能优化记录](https://github.com/LiguoXia/InputBeacon/blob/v1.4.4/docs/performance-1.4.4.md)。

需要 Windows 10 / 11 和 .NET Framework 4.8。程序未签名。
