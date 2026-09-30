// Run only in a disposable macOS CI account; selects Sogou in its own test field.
import AppKit
import Carbon

@main
final class SogouIntegration: NSObject, NSApplicationDelegate {
    private var window: NSWindow!
    private var view: NSTextView!
    private var timer: Timer?
    private var stage = 0
    private var started = Date()
    private var stageStarted = Date()
    private var stableReplies = 0
    private var replyCount = 0
    private var selected = false
    private var originalSource: TISInputSource?
    private var productionProbe: Process?
    private let center = DistributedNotificationCenter.default()
    private let expected: [InputMode] = [.chinese, .english, .chinese]

    static func main() {
        guard ProcessInfo.processInfo.environment["CI"] == "true" else { fatalError("This integration test is CI-only") }
        let app = NSApplication.shared
        let delegate = SogouIntegration(); app.delegate = delegate; app.run()
        withExtendedLifetime(delegate) {}
    }
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        window = NSWindow(contentRect: CGRect(x: 150, y: 150, width: 500, height: 200),
                          styleMask: [.titled, .closable], backing: .buffered, defer: false)
        view = NSTextView(frame: window.contentView!.bounds)
        window.contentView = view; window.makeKeyAndOrderFront(nil); window.makeFirstResponder(view)
        NSApp.activate(ignoringOtherApps: true)
        originalSource = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue()
        let appURL = URL(fileURLWithPath: "/Library/Input Methods/SogouInput.app")
        let registerResult = TISRegisterInputSource(appURL as CFURL)
        print("TISRegisterInputSource=\(registerResult)")
        let sources = TISCreateInputSourceList([kTISPropertyInputSourceID as String: "com.sogou.inputmethod.sogou.pinyin"] as CFDictionary, true).takeRetainedValue() as! [TISInputSource]
        guard let source = sources.first else { finish("Sogou source unavailable", success: false); return }
        print("TISEnableInputSource=\(TISEnableInputSource(source))")
        center.addObserver(self, selector: #selector(receive(_:)), name: .init(ThirdPartyInputProvider.sogou.responseName),
                           object: nil, suspensionBehavior: .deliverImmediately)
        started = Date(); stageStarted = Date()
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in self?.tick() }
    }
    private func tick() {
        if Date().timeIntervalSince(started) > 30 { finish("Timeout at stage \(stage), replies=\(replyCount)", success: false); return }
        // Allow IMK activation before configuring only this test application's mode.
        guard Date().timeIntervalSince(started) > 2 else { return }
        if !selected {
            NSApp.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil); window.makeFirstResponder(view)
            // Enabling changes the registered source; retrieve a fresh selectable object.
            let sources = TISCreateInputSourceList([kTISPropertyInputSourceID as String: "com.sogou.inputmethod.sogou.pinyin"] as CFDictionary, false).takeRetainedValue() as! [TISInputSource]
            if let source = sources.first {
                let result = TISSelectInputSource(source)
                print("Select refreshed Sogou source=\(result) frontmost=\(NSWorkspace.shared.frontmostApplication?.bundleIdentifier ?? "none")")
                if result == noErr {
                    selected = true; stageStarted = Date()
                    guard let path = ProcessInfo.processInfo.environment["INPUTBEACON_PROBE"] else { finish("Missing production probe", success: false); return }
                    let process = Process(); process.executableURL = URL(fileURLWithPath: path)
                    process.arguments = ["--probe-smoke"]
                    do { try process.run(); productionProbe = process }
                    catch { finish("Cannot launch production probe: \(error)", success: false) }
                }
            }
            return
        }
        if stage == expected.count {
            guard let process = productionProbe, !process.isRunning else { return }
            finish("Real Sogou and production probe sequence complete", success: process.terminationStatus == 0)
            return
        }
        guard Date().timeIntervalSince(stageStarted) > 0.5 else { return }
        let setting = stage == 1 ? "2" : "1"
        center.postNotificationName(.init("SGChangeSogouInputStatus"),
                                    object: "\(setting)::0::com.liguoxia.InputBeacon.SogouTest",
                                    userInfo: nil, deliverImmediately: true)
        center.postNotificationName(.init(ThirdPartyInputProvider.sogou.requestName), object: nil, userInfo: nil, deliverImmediately: true)
    }
    @objc private func receive(_ notification: Notification) {
        guard stage < expected.count else { return }
        replyCount += 1
        let mode = ThirdPartyInputProvider.sogou.decode(object: notification.object, userInfo: notification.userInfo)
        let raw = notification.userInfo?["inputStatus"] as? String ?? "missing"
        print("Sogou stage=\(stage) inputStatus=\(raw) decoded=\(mode?.rawValue ?? "unknown")")
        if mode == expected[stage] { stableReplies += 1 } else { stableReplies = 0 }
        if stableReplies >= 3 && Date().timeIntervalSince(stageStarted) > 1 {
            print("PASS real Sogou \(expected[stage].rawValue) with unchanged source ID")
            stage += 1; stableReplies = 0; stageStarted = Date()
        }
    }
    private func finish(_ message: String, success: Bool) {
        timer?.invalidate(); center.removeObserver(self)
        if let process = productionProbe, process.isRunning { process.terminate() }
        if let originalSource { TISSelectInputSource(originalSource) }
        print(message); fflush(stdout)
        if let path = ProcessInfo.processInfo.environment["INPUTBEACON_TEST_RESULT"] {
            try? (success ? "PASS" : "FAIL").write(toFile: path, atomically: true, encoding: .utf8)
        }
        exit(success ? 0 : 1)
    }
}
