import Foundation

/// Monotonic seconds; no text or key events are stored. Mirrors the Windows
/// debounce, effective-case and focus-entry rules.
public struct FollowLifetime {
    private var context: UInt64?
    private var shownField: UInt64?
    private var mode: String?
    private var pendingMode: String?
    private var pendingSince = 0.0
    private var previousUppercase = false
    private var lastObserved: Double?
    private var expires = 0.0
    public private(set) var triggerCount = 0
    public private(set) var lastTrigger = "None"
    public init() {}

    public mutating func reset() { self = FollowLifetime() }

    public mutating func observe(state: InputState, field: UInt64?, seconds: Int, now: Double) {
        if let lastObserved, now < lastObserved || now - lastObserved > 0.4 { pendingMode = nil }
        lastObserved = now
        guard let field else { pendingMode = nil; return }
        if context != field {
            context = field; mode = state.modeKey; pendingMode = nil
            previousUppercase = state.uppercase; expires = 0
        } else {
            if previousUppercase != state.uppercase {
                previousUppercase = state.uppercase
                trigger(seconds: seconds, now: now, reason: "Letter case changed")
            }
            if let current = state.modeKey {
                if mode == nil { mode = current; pendingMode = nil }
                else if current == mode { pendingMode = nil }
                else if pendingMode != current { pendingMode = current; pendingSince = now }
                else if now - pendingSince >= 0.2 {
                    mode = current; pendingMode = nil
                    trigger(seconds: seconds, now: now, reason: "Input mode confirmed")
                }
            } else { pendingMode = nil }
        }
        if state.mode != .unknown && shownField != field {
            shownField = field
            trigger(seconds: seconds, now: now, reason: "Input field focused")
        }
    }

    private mutating func trigger(seconds: Int, now: Double, reason: String) {
        expires = now + Double(seconds); triggerCount += 1; lastTrigger = reason
    }

    public func shouldShow(enabled: Bool, seconds: Int, hasCaret: Bool, knownState: Bool, now: Double) -> Bool {
        enabled && hasCaret && knownState && (seconds == 0 || now < expires)
    }
}
