import AppKit
import ApplicationServices
import ServiceManagement
import BeaconCore

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let preferences = Preferences()
    private let probe = InputProbe()
    private let tracker = CaretTracker()
    private let floating = Overlay(bubble: false)
    private let bubble = Overlay(bubble: true)
    private var status: NSStatusItem!
    private var timer: Timer?
    private var lifetime = FollowLifetime()
    private var menuOpen = false
    private var dialogOpen = false
    private var state = InputState()

    func applicationDidFinishLaunching(_ notification: Notification) {
        let sameApps = NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "com.liguoxia.InputBeacon")
        if let existing = sameApps.first(where: { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }) {
            existing.activate(options: [.activateIgnoringOtherApps])
            NSApp.terminate(nil); return
        }
        NSApp.setActivationPolicy(.accessory)
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        status.menu = makeMenu()
        floating.view.contextMenu = { [weak self] in self?.makeMenu() ?? NSMenu() }
        floating.view.onMoved = { [weak self] origin in self?.preferences.origin = origin }
        NotificationCenter.default.addObserver(self, selector: #selector(screenChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        applyPreferences()
        timer = Timer(timeInterval: 0.1, repeats: true) { [weak self] _ in self?.tick() }
        timer?.tolerance = 0.025
        RunLoop.main.add(timer!, forMode: .common)
        tick()
        if CommandLine.arguments.contains("--ui-smoke") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [self] in
                precondition(status.menu != nil && status.button != nil)
                precondition(!floating.panel.canBecomeKey && !bubble.panel.canBecomeKey)
                precondition(bubble.panel.ignoresMouseEvents)
                precondition(floating.panel.contentView === floating.view)
                precondition(floating.panel.frame.width > 0)
                print("AppKit menu and nonactivating overlays OK")
                NSApp.terminate(nil)
            }
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        preferences.showFloating = true; preferences.clickThrough = false
        applyPreferences(); return false
    }

    func applicationWillTerminate(_ notification: Notification) { timer?.invalidate() }

    @objc private func screenChanged() { placeFloating() }

    private func placeFloating() {
        let size = CGSize(width: 84 * preferences.scale, height: 36 * preferences.scale)
        let screen = NSScreen.screens.first(where: { $0.frame.contains(preferences.origin ?? NSEvent.mouseLocation) }) ?? NSScreen.main
        guard let screen else { return }
        let origin = preferences.origin ?? CGPoint(x: screen.visibleFrame.maxX - size.width - 24, y: screen.visibleFrame.maxY - size.height - 24)
        floating.panel.setFrame(BeaconGeometry.clamp(CGRect(origin: origin, size: size), to: screen.visibleFrame), display: true)
    }

    private func applyPreferences() {
        lifetime.reset(); bubble.panel.orderOut(nil)
        floating.panel.ignoresMouseEvents = preferences.clickThrough
        placeFloating()
        if preferences.showFloating { floating.panel.orderFrontRegardless() } else { floating.panel.orderOut(nil) }
        tick()
    }

    private func tick() {
        state = probe.read()
        if preferences.showMenuStatus {
            let color = preferences.colorHex.flatMap(Preferences.color) ?? .labelColor
            status.button?.attributedTitle = NSAttributedString(string: state.label, attributes: [.foregroundColor: color, .font: NSFont.monospacedSystemFont(ofSize: 13, weight: .medium)])
        } else { status.button?.title = "⌨" }
        status.button?.toolTip = "键盘状态 · \(state.sourceName)\n\(state.modeDetail)；点击打开设置"
        floating.update(state: state, preferences: preferences)
        guard preferences.followCaret, !menuOpen, !dialogOpen,
              let pid = NSWorkspace.shared.frontmostApplication?.processIdentifier,
              pid != ProcessInfo.processInfo.processIdentifier else { bubble.panel.orderOut(nil); return }
        tracker.refresh(pid: pid)
        let now = ProcessInfo.processInfo.systemUptime
        let sample = tracker.sample.flatMap { $0.pid == pid && now - $0.timestamp < 0.35 ? $0 : nil }
        lifetime.observe(state: state, field: sample?.field, seconds: preferences.followSeconds, now: now)
        guard lifetime.shouldShow(enabled: true, seconds: preferences.followSeconds, hasCaret: sample != nil,
                                  knownState: state.mode != .unknown, now: now), let sample,
              let mainScreen = NSScreen.screens.first else { bubble.panel.orderOut(nil); return }
        let caret = BeaconGeometry.appKitRect(fromAX: sample.rect, mainDisplayHeight: mainScreen.frame.height)
        guard let screen = NSScreen.screens.first(where: { $0.frame.intersects(caret.insetBy(dx: -1, dy: 0)) }) else { bubble.panel.orderOut(nil); return }
        bubble.update(state: state, preferences: preferences)
        let size = CGSize(width: 84 * preferences.scale, height: 36 * preferences.scale)
        let frame = BeaconGeometry.bubble(caret: caret, size: size, visibleFrame: screen.visibleFrame)
        if bubble.panel.frame != frame { bubble.panel.setFrame(frame, display: true) }
        if !bubble.panel.isVisible { bubble.panel.orderFrontRegardless() }
    }

    func menuWillOpen(_ menu: NSMenu) {
        menuOpen = true; bubble.panel.orderOut(nil)
        refreshChecks(menu)
    }
    func menuDidClose(_ menu: NSMenu) { menuOpen = false }

    private func item(_ title: String, _ action: Selector, tag: Int = 0) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self; item.tag = tag; return item
    }
    private func makeMenu() -> NSMenu {
        let menu = NSMenu(); menu.delegate = self
        let title = NSMenuItem(title: "键盘状态 · InputBeacon", action: nil, keyEquivalent: "")
        menu.addItem(title)
        let modeInfo = NSMenuItem(title: state.modeDetail, action: nil, keyEquivalent: "")
        modeInfo.tag = 901; menu.addItem(modeInfo)
        menu.addItem(.separator())
        menu.addItem(item("显示悬浮窗", #selector(toggleFloating)))
        menu.addItem(item("菜单栏显示状态", #selector(toggleMenuStatus)))
        menu.addItem(item("光标跟随显示", #selector(toggleFollow)))
        menu.addItem(item("悬浮窗鼠标穿透", #selector(toggleClickThrough)))
        let durations = NSMenu()
        for seconds in [0, 1, 3, 5, 10, 30, 60] { durations.addItem(item(seconds == 0 ? "一直显示" : "\(seconds) 秒", #selector(setDuration(_:)), tag: seconds)) }
        durations.addItem(item("自定义时长…", #selector(customDuration)))
        let duration = NSMenuItem(title: "跟随显示时长", action: nil, keyEquivalent: ""); duration.submenu = durations; menu.addItem(duration)
        let sizes = NSMenu()
        for value in [80, 100, 125, 150] { sizes.addItem(item("\(value)%", #selector(setScale(_:)), tag: value)) }
        let size = NSMenuItem(title: "显示大小", action: nil, keyEquivalent: ""); size.submenu = sizes; menu.addItem(size)
        let opacities = NSMenu()
        for value in [45, 60, 80, 94, 100] { opacities.addItem(item("\(value)%", #selector(setOpacity(_:)), tag: value)) }
        let opacity = NSMenuItem(title: "文字不透明度", action: nil, keyEquivalent: ""); opacity.submenu = opacities; menu.addItem(opacity)
        menu.addItem(item("文字颜色（HEX）…", #selector(customColor)))
        menu.addItem(item("恢复默认配色", #selector(resetColor)))
        menu.addItem(item("重置悬浮窗位置", #selector(resetPosition)))
        menu.addItem(.separator())
        menu.addItem(item("登录时启动", #selector(toggleLogin)))
        menu.addItem(item("授权光标跟随（辅助功能）…", #selector(requestAccessibility)))
        menu.addItem(item("复制状态诊断", #selector(copyDiagnostics)))
        menu.addItem(item("关于与使用说明…", #selector(about)))
        menu.addItem(.separator())
        menu.addItem(item("退出", #selector(quit)))
        refreshChecks(menu); return menu
    }

    private func refreshChecks(_ menu: NSMenu) {
        for item in menu.items {
            if item.tag == 901 { item.title = state.modeDetail }
            switch item.action {
            case #selector(toggleFloating): item.state = preferences.showFloating ? .on : .off
            case #selector(toggleMenuStatus): item.state = preferences.showMenuStatus ? .on : .off
            case #selector(toggleFollow): item.state = preferences.followCaret ? .on : .off
            case #selector(toggleClickThrough): item.state = preferences.clickThrough ? .on : .off
            case #selector(toggleLogin): item.state = SMAppService.mainApp.status == .enabled ? .on : .off
            case #selector(setDuration(_:)): item.state = item.tag == preferences.followSeconds ? .on : .off
            case #selector(setScale(_:)): item.state = item.tag == Int((preferences.scale * 100).rounded()) ? .on : .off
            case #selector(setOpacity(_:)): item.state = item.tag == Int((preferences.opacity * 100).rounded()) ? .on : .off
            default: break
            }
            if let submenu = item.submenu { refreshChecks(submenu) }
        }
    }

    @objc private func toggleFloating() { preferences.showFloating.toggle(); applyPreferences() }
    @objc private func toggleMenuStatus() { preferences.showMenuStatus.toggle(); applyPreferences() }
    @objc private func toggleClickThrough() { preferences.clickThrough.toggle(); applyPreferences() }
    @objc private func toggleFollow() {
        preferences.followCaret.toggle()
        if preferences.followCaret && !AXIsProcessTrusted() { requestAccessibility() }
        applyPreferences()
    }
    @objc private func setDuration(_ item: NSMenuItem) { preferences.followSeconds = item.tag; applyPreferences() }
    @objc private func setScale(_ item: NSMenuItem) { preferences.scale = CGFloat(item.tag) / 100; applyPreferences() }
    @objc private func setOpacity(_ item: NSMenuItem) { preferences.opacity = CGFloat(item.tag) / 100; applyPreferences() }
    @objc private func resetColor() { preferences.colorHex = nil; applyPreferences() }
    @objc private func resetPosition() { preferences.origin = nil; preferences.showFloating = true; applyPreferences() }
    @objc private func quit() { NSApp.terminate(nil) }

    private func prompt(title: String, detail: String, value: String) -> String? {
        dialogOpen = true; bubble.panel.orderOut(nil); defer { dialogOpen = false }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = detail
        let field = NSTextField(string: value); field.frame = CGRect(x: 0, y: 0, width: 260, height: 24)
        alert.accessoryView = field; alert.addButton(withTitle: "应用"); alert.addButton(withTitle: "取消")
        alert.window.initialFirstResponder = field
        return alert.runModal() == .alertFirstButtonReturn ? field.stringValue : nil
    }
    private func message(_ title: String, _ detail: String) {
        dialogOpen = true; bubble.panel.orderOut(nil); defer { dialogOpen = false }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = detail; alert.runModal()
    }
    @objc private func customDuration() {
        guard let value = prompt(title: "跟随显示时长", detail: "输入 1～60 秒；0 表示一直显示。", value: String(preferences.followSeconds)) else { return }
        guard let seconds = Int(value.trimmingCharacters(in: .whitespacesAndNewlines)), (0...60).contains(seconds) else { message("时长无效", "请输入 0～60 的整数。"); return }
        preferences.followSeconds = seconds; applyPreferences()
    }
    @objc private func customColor() {
        guard let hex = prompt(title: "文字颜色", detail: "支持 #0071E3、0071E3 或 #FFF。", value: preferences.colorHex ?? "#0071E3") else { return }
        guard Preferences.color(hex) != nil else { message("颜色无效", "请输入 3 位或 6 位 HEX 颜色。"); return }
        preferences.colorHex = hex; applyPreferences()
    }
    @objc private func toggleLogin() {
        do {
            if SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
            else { try SMAppService.mainApp.register() }
            if SMAppService.mainApp.status == .requiresApproval { SMAppService.openSystemSettingsLoginItems() }
        } catch { message("无法更改登录启动", "请将 InputBeacon.app 移入“应用程序”后重试。\n\(error.localizedDescription)") }
    }
    @objc private func requestAccessibility() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        if !AXIsProcessTrustedWithOptions(options), let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }
    @objc private func copyDiagnostics() {
        let text = "InputBeacon macOS \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev")\nOS=\(ProcessInfo.processInfo.operatingSystemVersionString)\n\(probe.diagnostic)\nModeOrigin=\(state.modeOrigin)\nMode=\(state.mode.rawValue)\nCaps=\(state.caps) Shift=\(state.shift)\nAccessibility=\(AXIsProcessTrusted())\nFollow=\(preferences.followCaret) Seconds=\(preferences.followSeconds)\nTrigger=\(lifetime.lastTrigger) Count=\(lifetime.triggerCount)\n"
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(text, forType: .string)
    }
    @objc private func about() {
        message("键盘状态 · InputBeacon \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev")", "支持 Intel 与 Apple Silicon，macOS 13 或更高版本。\n\n搜狗与支持状态查询的鼠须管可显示输入法内部中英文状态；其他输入法显示系统输入源语言。等待或无法取得第三方状态时显示 ?，可复制状态诊断排查。全角模式不显示。A / a 根据 Caps Lock 与 Shift 组合计算。\n\n光标跟随需辅助功能权限，只查询光标位置及控件标识，不读取输入文本。自绘终端、安全输入框或未提供光标接口的应用可能不支持跟随。\n\n拖动悬浮文字调整位置，右键打开设置。圆点表示 Caps Lock，↑ 表示 Shift。\n\ngithub.com/LiguoXia/InputBeacon")
    }
}
