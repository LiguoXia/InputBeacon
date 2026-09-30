import AppKit
import BeaconCore
import BeaconMac

enum ProbeDiagnostics {
    static func run(requireSequence: Bool) -> Never {
        if requireSequence && ProcessInfo.processInfo.environment["CI"] != "true" {
            fputs("--probe-smoke is reserved for disposable CI sessions\n", stderr); exit(2)
        }
        NSApplication.shared.setActivationPolicy(.prohibited)
        let probe = InputProbe()
        let started = ProcessInfo.processInfo.systemUptime
        var previous: InputState?
        var phase = 0
        let expected: [InputMode] = [.chinese, .english, .chinese]
        let timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { _ in
            let state = probe.read()
            if state != previous {
                print("Source=\(state.sourceID) Mode=\(state.mode.rawValue) Origin=\(state.modeOrigin)")
                previous = state
            }
            if requireSequence && state.modeOrigin == "sogou-status" && state.mode == expected[phase] {
                phase += 1
                if phase == expected.count {
                    print("PASS production InputProbe Chinese → English → Chinese\n\(probe.diagnostic)")
                    fflush(stdout); exit(0)
                }
            }
            if ProcessInfo.processInfo.systemUptime - started >= (requireSequence ? 35 : 10) {
                print(probe.diagnostic); fflush(stdout); exit(requireSequence ? 1 : 0)
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        RunLoop.main.run()
        exit(1)
    }
}
