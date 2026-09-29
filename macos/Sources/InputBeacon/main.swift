import AppKit
import BeaconCore

if CommandLine.arguments.contains("--self-check") {
    precondition(InputState(mode: .english, caps: true, shift: true).uppercase == false)
    precondition(Preferences.color("#0071E3") != nil)
    precondition(Preferences.color("bad-color") == nil)
    print("InputBeacon macOS executable OK (\(ProcessInfo.processInfo.operatingSystemVersionString))")
} else {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    app.run()
    withExtendedLifetime(delegate) {}
}
