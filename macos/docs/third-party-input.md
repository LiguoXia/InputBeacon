# 第三方输入法状态适配

## 为什么不能只看系统输入源

`TISCopyCurrentKeyboardInputSource` / `kTISPropertyInputSourceLanguages` 反映输入源身份及语言。搜狗内部 Shift 中英切换通常不改变 `com.sogou.inputmethod.sogou.pinyin`，因此 1.0.0 的语言分类不足以判断实际模式。

1.1.0 增加按输入法选择的本地只读查询。生产程序只发送下面列出的查询通知，不发送切换指令，不修改输入法配置，不读取输入文本、词库、候选词、历史记录或第三方日志。

## 搜狗

通过 `DistributedNotificationCenter` 发送 `SGFetchSelectPreviousIMEHotKeyAndSogouInfo`，接收 `SGSystemPreviousIMEHotKeyAndSogouInfo`，只消费 `userInfo.inputStatus`。状态值来自 `currentShowStatus`，不能与搜狗另一个 `currentInputStatus` 枚举混用。

| 值 | 含义 | 显示 |
| --- | --- | --- |
| 0 | 中文 | 中 |
| 1 | 英文 | 英 |
| 2 | 自动英文 / 地址栏英文 / 键盘英文 | 英 |
| 3 | Caps 英文 | 英 |
| 缺失或其他值 | 不支持 / 未确认 | ? |

协议依据是官方 [搜狗 6.25.1.11973 安装包](https://ime.gtimg.com/pc/sogou_mac_625a.zip) 中相关状态查询方法；SHA-256 为 `62b265988e0e3cae590ebf8c4588945011987f606687335ef29edf6c622522c6`，可与 [Homebrew 配方](https://github.com/Homebrew/homebrew-cask/blob/master/Casks/s/sogouinput.rb) 核对。该接口并非搜狗承诺长期兼容的公共 SDK。

查询处理器独立于切换助手的安装检测。状态变化推送 `SGSwitchNotificationChangeInputStatus` 则有助手安装条件，因此程序主动查询，不依赖这条推送，不创建伪装助手或安装任何额外插件。状态 payload 中其他字段一律忽略。

## 鼠须管

发送 `SquirrelGetASCIIModeNotification`，接收 `SquirrelASCIIModeResponse` 的字符串 object：`ascii` 为英文，`nascii` 为中文。依据鼠须管[应用代理源码](https://github.com/rime/squirrel/blob/8418c95c3245e1e0bda578ac054025c5bfaccb7f/sources/SquirrelApplicationDelegate.swift)与[输入控制器](https://github.com/rime/squirrel/blob/8418c95c3245e1e0bda578ac054025c5bfaccb7f/sources/SquirrelInputController.swift)。旧版本没有查询接口时显示未知，不按 Caps 或 Shift 推断。

## 过期与切换保护

约每 200 ms 查询，只允许一个请求在途。请求 500 ms 未回复即超时，并留出 250 ms 等待区间；已确认样本最多使用 750 ms。切换输入源、前台应用时清空显示样本，先消费并丢弃旧上下文回复，再查询新的上下文。接收时再次核对系统当前输入源和前台应用。

这两种协议没有请求序号和目标应用 PID，也不提供对发送者的身份验证，因此无法完全消除极端延迟回复或同用户进程伪造通知造成的歧义。它们只用于显示提示，不用于授权或安全决策。

## 测试

核心测试覆盖真实协议的字符串 / 数字值、非法 payload、缺失语言元数据、同 ID 下中英切换、跨应用过期回复、超时、缓存失效与跟随触发。生产程序不做输入模式切换。

`BeaconMacTests` 使用真实生产接收器及系统分布式通知，注入输入源 / 前台应用快照和模拟回复，验证同 ID 下中文 / 英文 / 中文、应用切换、输入源切换和超时。

`scripts/test-sogou.sh` 只允许在 `CI=true` 的临时 macOS 账户执行：校验并解包官方搜狗安装包、启动真实输入法进程，通过生产解码器验证只读查询的实际回复。下载的搜狗二进制不进入 Git 仓库或 InputBeacon 发布附件。

GitHub 托管桌面注册输入源成功，但激活新输入源返回 `TISSelectInputSource=-50`，因此默认 CI 不宣称完成真实 Shift 切换或跨应用验收。另保留 `INPUTBEACON_FULL_IME_TEST=true` 的完整会话测试，供能激活输入源的临时 macOS runner 使用：在独立文本框中用搜狗切换控制协议设置中文 / 英文 / 中文，同时检查生产探针。该可选测试尚未通过托管 runner 验证，也不代替微信、IDEA 等应用中按 Shift 的真机验收。
