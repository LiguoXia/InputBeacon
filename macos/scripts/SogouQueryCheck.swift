// Validates the actual vendor process's read-only protocol without selecting an IME.
import Foundation
import Carbon

@main
final class SogouQueryCheck: NSObject {
    private var replies = 0
    private var requests = 0
    private var pending = false
    private var lastRequest = Date.distantPast
    private let center = DistributedNotificationCenter.default()
    static func main() {
        guard ProcessInfo.processInfo.environment["CI"] == "true" else { fatalError("CI-only vendor integration check") }
        // A freshly copied IME needs registration in this disposable account.
        let url = URL(fileURLWithPath: "/Library/Input Methods/SogouInput.app")
        print("TISRegisterInputSource=\(TISRegisterInputSource(url as CFURL))")
        let check = SogouQueryCheck()
        check.center.addObserver(check, selector: #selector(check.receive(_:)),
                                 name: .init(ThirdPartyInputProvider.sogou.responseName), object: nil, suspensionBehavior: .deliverImmediately)
        let started = Date()
        let timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { _ in
            if Date().timeIntervalSince(started) > 20 { print("FAIL no valid Sogou response"); exit(1) }
            // The vendor may still be starting when the first request is sent.
            guard !check.pending || Date().timeIntervalSince(check.lastRequest) >= 1 else { return }
            check.pending = true; check.requests += 1
            check.lastRequest = Date()
            check.center.postNotificationName(.init(ThirdPartyInputProvider.sogou.requestName), object: nil, userInfo: nil, deliverImmediately: true)
        }
        RunLoop.main.add(timer, forMode: .common)
        withExtendedLifetime(check) { RunLoop.main.run() }
    }
    @objc private func receive(_ notification: Notification) {
        guard pending else { return }
        pending = false
        guard let mode = ThirdPartyInputProvider.sogou.decode(object: notification.object, userInfo: notification.userInfo),
              let version = notification.userInfo?["sogouVersion"] as? String, version.hasPrefix("6.25") else {
            print("FAIL unexpected Sogou status response"); fflush(stdout); exit(1)
        }
        replies += 1
        print("Real Sogou version=\(version) mode=\(mode.rawValue), reply \(replies)")
        if replies == 3 {
            print("PASS actual Sogou read-only status query (\(requests) requests)")
            print("NOT TESTED: native Shift/application switching; hosted runner TISSelectInputSource returned -50 during setup")
            fflush(stdout); exit(0)
        }
    }
}
